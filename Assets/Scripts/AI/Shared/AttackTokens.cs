// Izin menyerang. Brain meminta token sebelum menyerang dan melepasnya setelah selesai.
// Tanpa penyedia (sebelum TacticalCoordinator ada), semua permintaan dikabulkan.
public interface IAttackTokenProvider
{
    bool Request(EnemyController requester);

    void Release(EnemyController holder);

    bool Holds(EnemyController enemy);
}

public static class AttackTokens
{
    // Diisi TacticalCoordinator saat aktif, dikosongkan saat nonaktif.
    public static IAttackTokenProvider Provider { get; set; }
}
