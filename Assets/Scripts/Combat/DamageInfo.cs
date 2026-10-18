using UnityEngine;

public enum DamageKind
{
    Generic,
    Projectile,
    Burst,
    Melee,
    Lunge
}

// Paket damage mentah (sebelum Defense). Source dipakai AI untuk tahu siapa penyerangnya.
public readonly struct DamageInfo
{
    public readonly float rawAmount;
    public readonly GameObject source;
    public readonly Vector3 point;
    public readonly DamageKind kind;

    public DamageInfo(float rawAmount, GameObject source, Vector3 point, DamageKind kind)
    {
        this.rawAmount = rawAmount;
        this.source = source;
        this.point = point;
        this.kind = kind;
    }

    public DamageInfo(float rawAmount)
        : this(rawAmount, null, Vector3.zero, DamageKind.Generic)
    {
    }
}
