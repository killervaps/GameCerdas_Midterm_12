using System;
using UnityEngine;

// Memori satu musuh. Ditulis EnemyPerception setiap tick, dibaca brain.
// Posisi terakhir MC (LKP) dilupakan setelah Memory Duration detik tanpa melihat MC.
public class EnemyBlackboard : MonoBehaviour
{
    [Header("Memory")]
    // Ditimpa EnemyRoleData lewat EnemyController.
    [SerializeField] private float memoryDuration = 8f;

    [Header("Debug")]
    [SerializeField] private bool logMemory = false;

    public PlayerHealth Player { get; private set; }

    public bool CanSeePlayer { get; private set; }

    // Jarak datar ke MC. Valid selama MC ada, terlihat atau tidak.
    public float DistanceToPlayer { get; private set; } = float.PositiveInfinity;

    public bool HasLastKnownPosition { get; private set; }

    public Vector3 LastKnownPosition { get; private set; }

    // Kecepatan MC saat terakhir terlihat. Dipakai untuk memprediksi posisi (mis. Charger mencegat MC cepat).
    public Vector3 LastKnownVelocity { get; private set; }

    public float LastSeenTime { get; private set; } = float.NegativeInfinity;

    public float TimeSinceSeen => HasLastKnownPosition ? Time.time - LastSeenTime : float.PositiveInfinity;

    // Sisa waktu sebelum LKP dilupakan, 0 jika sedang melihat MC atau tidak punya LKP.
    public float MemoryRemaining =>
        HasLastKnownPosition && !CanSeePlayer ? Mathf.Max(0f, memoryDuration - TimeSinceSeen) : 0f;

    public float MemoryDuration => memoryDuration;

    // Musuh tahu (atau ingat) kira-kira di mana MC berada.
    public bool IsAware => CanSeePlayer || HasLastKnownPosition;

    public float LastDamagedTime { get; private set; } = float.NegativeInfinity;

    public GameObject LastAttacker { get; private set; }

    public event Action Forgot;

    public void SetMemoryDuration(float seconds)
    {
        memoryDuration = Mathf.Max(0f, seconds);
    }

    public void SetPlayer(PlayerHealth player)
    {
        Player = player;
    }

    // Dipanggil perception setiap tick.
    public void SetVisibility(bool canSee, float distance)
    {
        if (canSee && !CanSeePlayer)
            Log("melihat MC");
        else if (!canSee && CanSeePlayer)
            Log("kehilangan MC, LKP " + LastKnownPosition);

        CanSeePlayer = canSee;
        DistanceToPlayer = distance;
    }

    public void Observe(Vector3 position, Vector3 velocity)
    {
        HasLastKnownPosition = true;
        LastKnownPosition = position;
        LastKnownVelocity = velocity;
        LastSeenTime = Time.time;
    }

    // Musuh tahu posisi MC tanpa melihatnya, mis. saat terkena tembakan dari luar pandangan.
    public void Alert(Vector3 position, GameObject attacker)
    {
        HasLastKnownPosition = true;
        LastKnownPosition = position;
        LastKnownVelocity = Vector3.zero;
        LastSeenTime = Time.time;
        LastDamagedTime = Time.time;
        LastAttacker = attacker;

        Log("diserang, LKP diperbarui ke " + position);
    }

    public void ClearLastKnownPosition()
    {
        if (!HasLastKnownPosition)
            return;

        HasLastKnownPosition = false;
        LastKnownVelocity = Vector3.zero;
    }

    // Dipanggil EnemyController setiap tick setelah perception.
    public void TickMemory()
    {
        if (Player == null || Player.IsDead)
        {
            CanSeePlayer = false;
            ClearLastKnownPosition();
            return;
        }

        if (!CanSeePlayer && HasLastKnownPosition && TimeSinceSeen > memoryDuration)
        {
            ClearLastKnownPosition();
            Log("lupa posisi MC");
            Forgot?.Invoke();
        }
    }

    public bool WasDamagedWithin(float seconds)
    {
        return Time.time - LastDamagedTime <= seconds;
    }

    private void Log(string message)
    {
        if (logMemory)
            Debug.Log(name + " blackboard: " + message, this);
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !HasLastKnownPosition)
            return;

        // LKP: bola oranye, makin pudar saat makin dekat dengan lupa.
        float fade = memoryDuration > 0f && !CanSeePlayer ? MemoryRemaining / memoryDuration : 1f;

        Gizmos.color = new Color(1f, 0.55f, 0f, 0.25f + 0.75f * fade);
        Gizmos.DrawWireSphere(LastKnownPosition + Vector3.up * 0.1f, 0.35f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, LastKnownPosition + Vector3.up * 0.1f);

        if (LastKnownVelocity.sqrMagnitude > 0.01f)
            Gizmos.DrawLine(LastKnownPosition + Vector3.up * 0.1f, LastKnownPosition + Vector3.up * 0.1f + LastKnownVelocity * 0.5f);
    }
}
