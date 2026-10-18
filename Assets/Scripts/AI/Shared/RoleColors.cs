using UnityEngine;

// Warna penanda role, dipakai gizmo, tint placeholder, dan nanti label debug.
public static class RoleColors
{
    public static readonly Color Charger = new Color(1f, 0.45f, 0.2f);
    public static readonly Color Shooter = new Color(0.3f, 0.6f, 1f);
    public static readonly Color Guardian = new Color(0.4f, 0.85f, 0.4f);

    public static Color Get(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Shooter: return Shooter;
            case EnemyRole.Guardian: return Guardian;
            default: return Charger;
        }
    }

    public static string Hex(EnemyRole role)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(Get(role));
    }
}
