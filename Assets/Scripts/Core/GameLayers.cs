using UnityEngine;

// Nama layer gameplay
// Layer dibuat lewat menu MathGate > Setup > Layers & Physics Matrix, jadi nama di sini harus sama dengan nama di Tags and Layers.
public static class GameLayers
{
    public const string Ground = "Ground";
    public const string NavBlock = "NavBlock";
    public const string Decor = "Decor";
    public const string Player = "Player";
    public const string Enemy = "Enemy";
    public const string PlayerProjectile = "PlayerProjectile";
    public const string EnemyProjectile = "EnemyProjectile";

    public static readonly string[] All =
    {
        Ground,
        NavBlock,
        Decor,
        Player,
        Enemy,
        PlayerProjectile,
        EnemyProjectile
    };

    // Mask untuk raycast line of sight. Hanya NavBlock yang menghalangi pandangan.
    public static LayerMask ObstacleMask =>
        LayerMask.GetMask(NavBlock);

    public static LayerMask EnemyMask =>
        LayerMask.GetMask(Enemy);

    public static LayerMask PlayerMask =>
        LayerMask.GetMask(Player);
}
