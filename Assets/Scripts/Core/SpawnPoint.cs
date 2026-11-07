using UnityEngine;

// Titik khusus role yang ditaruh desainer, mis. puncak menara untuk Shooter (Accept Any Role mati).
// EnemySpawner memakainya dengan peluang Anchor Chance role itu. Titik yang menerima semua role tidak dipakai PCG.
public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private bool acceptAnyRole = true;
    // Dipakai hanya jika Accept Any Role dimatikan.
    [SerializeField] private EnemyRole role = EnemyRole.Charger;
    // Selalu dipakai jika role ini muncul, tanpa melihat Anchor Chance di EnemySpawner.
    // Contoh: Boss Room dungeon, Guardian hanya boleh muncul di room Goal (hard constraint).
    [SerializeField] private bool alwaysUse;

    // Untuk titik yang dibuat dari kode, mis. titik Boss Room di dungeon.
    public void Configure(EnemyRole dedicatedRole, bool forced)
    {
        acceptAnyRole = false;
        role = dedicatedRole;
        alwaysUse = forced;
    }

    public bool IsForcedFor(EnemyRole candidate)
    {
        return alwaysUse && IsDedicatedTo(candidate);
    }

    public bool Accepts(EnemyRole candidate)
    {
        return acceptAnyRole || role == candidate;
    }

    // Titik khusus satu role, mis. puncak menara untuk Shooter.
    public bool IsDedicated => !acceptAnyRole;

    public bool IsDedicatedTo(EnemyRole candidate)
    {
        return !acceptAnyRole && role == candidate;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = acceptAnyRole ? Color.white : RoleColors.Get(role);

        Vector3 p = transform.position + Vector3.up * 0.05f;

        Gizmos.DrawWireSphere(p, 0.5f);
        Gizmos.DrawLine(p, p + transform.forward * 0.8f);
        Gizmos.DrawLine(p, p + Vector3.up * 1.5f);
    }
}
