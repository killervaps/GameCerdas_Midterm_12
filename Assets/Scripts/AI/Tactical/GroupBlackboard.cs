using UnityEngine;

// Pengetahuan bersama kelompok musuh, diperbarui TacticalCoordinator setiap loop.
// Termasuk indikator build MC dan ancaman live, yang dibaca semua brain lewat TacticalCoordinator.
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
    }
}
