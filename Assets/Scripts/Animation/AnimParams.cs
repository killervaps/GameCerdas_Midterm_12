using UnityEngine;

// Nama parameter Animator yang dipakai driver animasi dan menu pembuat Animator Controller.
// Ubah di sini saja supaya keduanya tetap cocok.
public static class AnimParams
{
    // Kecepatan maju (+) atau mundur (−) relatif terhadap speed dasar karakter. 0 diam, 1 lari penuh.
    public const string Forward = "Forward";
    // Besar kecepatan relatif tanpa arah.
    public const string Speed = "Speed";
    // Kecepatan lokal relatif terhadap speed dasar untuk blend tree 2D. X = kanan (+) / kiri (−), Z = maju (+) / mundur (−).
    public const string MoveX = "MoveX";
    public const string MoveZ = "MoveZ";
    // Musuh sedang menoleh di fase scan Search.
    public const string Scanning = "Scanning";
    // Aksi terkunci Charger: 0 tidak ada, 1 Windup, 2 Lunge, 3 Recover.
    public const string Special = "Special";
    public const string IsDead = "IsDead";

    // Trigger sekali jalan di layer upper-body.
    public const string Attack = "Attack";
    public const string Hit = "Hit";
    public const string Cast = "Cast";
    public const string Burst = "Burst";

    public static readonly int ForwardHash = Animator.StringToHash(Forward);
    public static readonly int SpeedHash = Animator.StringToHash(Speed);
    public static readonly int MoveXHash = Animator.StringToHash(MoveX);
    public static readonly int MoveZHash = Animator.StringToHash(MoveZ);
    public static readonly int ScanningHash = Animator.StringToHash(Scanning);
    public static readonly int SpecialHash = Animator.StringToHash(Special);
    public static readonly int IsDeadHash = Animator.StringToHash(IsDead);
    public static readonly int AttackHash = Animator.StringToHash(Attack);
    public static readonly int HitHash = Animator.StringToHash(Hit);
    public static readonly int CastHash = Animator.StringToHash(Cast);
    public static readonly int BurstHash = Animator.StringToHash(Burst);

    public const int SpecialNone = 0;
    public const int SpecialWindup = 1;
    public const int SpecialLunge = 2;
    public const int SpecialRecover = 3;

    public static int ToSpecial(EnemyAction action)
    {
        switch (action)
        {
            case EnemyAction.Windup: return SpecialWindup;
            case EnemyAction.Lunge: return SpecialLunge;
            case EnemyAction.Recover: return SpecialRecover;
            default: return SpecialNone;
        }
    }
}
