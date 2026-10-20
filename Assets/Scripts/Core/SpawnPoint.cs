using UnityEngine;

// Titik muncul musuh di arena. Bisa dikhususkan untuk satu role atau menerima semua role.
public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private bool acceptAnyRole = true;
    // Dipakai hanya jika Accept Any Role dimatikan.
    [SerializeField] private EnemyRole role = EnemyRole.Charger;

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
