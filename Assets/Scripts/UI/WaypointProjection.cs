using UnityEngine;

// Proyeksi titik dunia ke canvas untuk penanda arah (GoalIndicator, EnemyGroupWaypoint, ZoneIndicator).
public static class WaypointProjection
{
    // Area canvas yang dipakai panel lain (mis. minimap Mode Dungeon), dalam koordinat canvas dengan titik tengah
    // layar = (0,0). Penanda yang jatuh di area ini dipindah ke tepi bawah area supaya tidak tertutup panel.
    private static Rect reserved;
    private static bool hasReserved;
    private const float ReservedGap = 28f;

    public static void SetReserved(Rect area)
    {
        reserved = area;
        hasReserved = true;
    }

    public static void ClearReserved()
    {
        hasReserved = false;
    }

    // True = titik terlihat di dalam layar (dikurangi margin). False = local tidak dipakai, penanda ditempel di tepi
    // layar pada direction * edgeScale. Titik di belakang kamera (z < 0) dibalik supaya panah tepi tidak terbalik.
    public static bool Project(Camera cam, RectTransform canvasRect, float edgeMargin, Vector3 world,
        out Vector2 local, out Vector2 direction, out float edgeScale)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);
        bool behind = screen.z < 0f;

        if (behind)
            screen = -screen;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);

        Vector2 half = canvasRect.rect.size * 0.5f - new Vector2(edgeMargin, edgeMargin);
        bool onScreen = !behind && Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;

        direction = local.sqrMagnitude > 0.01f ? local.normalized : Vector2.up;
        edgeScale = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(direction.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(direction.y)));

        if (hasReserved)
        {
            // Penanda di layar atau di tepi yang jatuh di area panel dipindah tepat di bawah area itu. Direction dan
            // edgeScale diubah supaya pemanggil tetap cukup memakai direction * edgeScale.
            Vector2 point = onScreen ? local : direction * edgeScale;

            if (reserved.Contains(point))
            {
                Vector2 moved = new Vector2(point.x, reserved.yMin - ReservedGap);
                onScreen = false;
                direction = moved.sqrMagnitude > 0.01f ? moved.normalized : Vector2.up;
                edgeScale = moved.magnitude;
            }
        }

        return onScreen;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        hasReserved = false;
    }
}
