using System.Collections.Generic;
using UnityEngine;

// Pengetahuan bersama kelompok musuh, diperbarui TacticalCoordinator setiap loop.
// Termasuk indikator build MC, ancaman live, dan titik objective bersama (Hold Zone), yang dibaca semua brain
// lewat TacticalCoordinator.
public class GroupBlackboard
{
    public PlayerHealth Player { get; private set; }

    public Vector3 PlayerPosition { get; private set; }

    public Vector3 PlayerVelocity { get; private set; }

    public float PlayerRange { get; private set; }

    // Musuh yang sedang dikunci MC lewat target lock. Musuh bisa bereaksi, mis. tidak maju saat dikunci.
    public EnemyHealth PlayerLockedTarget { get; private set; }

    // Ada minimal satu musuh yang tahu posisi MC.
    public bool AnyAware { get; private set; }

    public BuildIndicators Indicators { get; private set; } = BuildIndicators.Neutral;

    public PlayerStats Stats => stats;

    // Ancaman musuh yang masih hidup terhadap build MC sekarang. Rumus sama dengan label di kartu gate.
    public bool HasThreat { get; private set; }

    public ThreatResult Threat { get; private set; }

    public void SetThreat(ThreatResult threat)
    {
        Threat = threat;
        HasThreat = true;
    }

    // ---------- Titik objective (Hold Zone) ----------

    private readonly List<Vector3> objectiveDoors = new List<Vector3>();

    // Ada titik objective bersama yang harus didatangi musuh walau tidak melihat MC.
    public bool HasObjectivePoint => ObjectiveAnchor != null;

    public Vector3 ObjectivePoint => ObjectiveAnchor != null ? ObjectiveAnchor.position : Vector3.zero;

    public float ObjectiveRadius { get; private set; }

    // Transform pusat zona, dipakai Guardian sebagai anchor.
    public Transform ObjectiveAnchor { get; private set; }

    // MC berada di dalam radius objective. Diperbarui tiap loop coordinator.
    public bool PlayerInObjective { get; private set; }

    // Titik tengah tiap pintu room zona, untuk arah jaga musuh di dalam zona.
    public IReadOnlyList<Vector3> ObjectiveDoors => objectiveDoors;

    // Diisi HoldZoneObjective saat ronde mulai.
    public void SetObjective(Transform anchor, float radius, IReadOnlyList<Vector3> doors)
    {
        ObjectiveAnchor = anchor;
        ObjectiveRadius = Mathf.Max(0f, radius);
        objectiveDoors.Clear();

        if (doors != null)
            objectiveDoors.AddRange(doors);

        PlayerInObjective = false;
    }

    // Dikosongkan HoldZoneObjective saat ronde selesai.
    public void ClearObjective()
    {
        ObjectiveAnchor = null;
        ObjectiveRadius = 0f;
        objectiveDoors.Clear();
        PlayerInObjective = false;
    }

    // Pintu zona terdekat dari posisi ini. False jika zona tidak punya data pintu.
    public bool TryNearestObjectiveDoor(Vector3 from, out Vector3 door)
    {
        door = ObjectivePoint;
        float best = float.MaxValue;

        foreach (Vector3 candidate in objectiveDoors)
        {
            float distance = (candidate - from).sqrMagnitude;

            if (distance < best)
            {
                best = distance;
                door = candidate;
            }
        }

        return objectiveDoors.Count > 0;
    }

    private void UpdateObjective()
    {
        if (!HasObjectivePoint || Player == null)
        {
            PlayerInObjective = false;
            return;
        }

        Vector3 offset = PlayerPosition - ObjectivePoint;
        offset.y = 0f;
        PlayerInObjective = offset.sqrMagnitude <= ObjectiveRadius * ObjectiveRadius;
    }

    private PlayerMotor motor;
    private PlayerStats stats;
    private TargetSelector targetSelector;

    public void Update(PlayerHealth player, bool anyAware)
    {
        if (player != Player)
        {
            Player = player;
            motor = player != null ? player.GetComponent<PlayerMotor>() : null;
            stats = player != null ? player.GetComponent<PlayerStats>() : null;
            targetSelector = player != null ? player.GetComponent<TargetSelector>() : null;
        }

        AnyAware = anyAware;

        if (player == null)
            return;

        PlayerPosition = player.transform.position;
        PlayerVelocity = motor != null ? motor.Velocity : Vector3.zero;
        PlayerRange = stats != null ? stats.Get(StatType.Range) : 8f;
        PlayerLockedTarget = targetSelector != null ? targetSelector.LockedTarget : null;
        Indicators = BuildIndicators.From(stats);
        UpdateObjective();
    }
}
