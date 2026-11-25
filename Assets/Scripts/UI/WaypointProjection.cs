using UnityEngine;

// Proyeksi titik dunia ke canvas untuk penanda arah (GoalIndicator, EnemyGroupWaypoint).
public static class WaypointProjection
{
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
        return onScreen;
    }
}
