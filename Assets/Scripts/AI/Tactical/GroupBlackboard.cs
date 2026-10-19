using UnityEngine;

// Pengetahuan bersama kelompok musuh, diperbarui TacticalCoordinator setiap loop.
// Indikator build MC (Mobilitas, Ketahanan, DayaSerang, Jangkauan) ditambahkan di M7.
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
    }
}
