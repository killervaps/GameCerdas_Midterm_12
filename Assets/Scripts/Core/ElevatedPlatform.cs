using UnityEngine;

// Penanda tempat tinggi, mis. menara. Pasang di root menara (tower_1, tower_2).
// Collider di dalam objek ini tidak menghalangi pandangan dan tembakan musuh yang sedang berdiri di atasnya,
// jadi Shooter di puncak bisa menembak ke bawah melewati tepi lantai menaranya sendiri.
// Shooter di atas platform juga bertahan di sana dan baru pergi (lompat) jika MC ikut naik.
public class ElevatedPlatform : MonoBehaviour
{
    private static readonly RaycastHit[] buffer = new RaycastHit[16];

    public bool Contains(Collider collider)
    {
        return collider != null && collider.transform.IsChildOf(transform);
    }

    // Platform tepat di bawah posisi kaki ini, atau null jika berdiri di tanah biasa.
    public static ElevatedPlatform Below(Vector3 feet, float maxDistance = 2.5f)
    {
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask(
            GameLayers.Player, GameLayers.Enemy, GameLayers.PlayerProjectile, GameLayers.EnemyProjectile);

        if (!Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, maxDistance, mask, QueryTriggerInteraction.Ignore))
            return null;

        return hit.collider.GetComponentInParent<ElevatedPlatform>();
    }

    // Seperti Physics.Linecast, tapi collider milik platform yang dikecualikan tidak dihitung menghalangi.
    public static bool LineBlocked(Vector3 from, Vector3 to, int mask, ElevatedPlatform ignore)
    {
        if (ignore == null)
            return Physics.Linecast(from, to, mask, QueryTriggerInteraction.Ignore);

        Vector3 delta = to - from;
        float length = delta.magnitude;

        if (length < 0.001f)
            return false;

        int count = Physics.RaycastNonAlloc(from, delta / length, buffer, length, mask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (!ignore.Contains(buffer[i].collider))
                return true;
        }

        return false;
    }
}
