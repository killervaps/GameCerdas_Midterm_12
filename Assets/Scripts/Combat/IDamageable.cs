using UnityEngine;

// Apa pun yang bisa menerima damage: MC, musuh, nanti mungkin objek arena.
public interface IDamageable
{
    bool IsDead { get; }

    // Titik bidik proyektil, kira-kira tengah badan.
    Vector3 AimPoint { get; }

    // Mengembalikan damage akhir setelah Defense.
    float TakeDamage(DamageInfo info);
}
