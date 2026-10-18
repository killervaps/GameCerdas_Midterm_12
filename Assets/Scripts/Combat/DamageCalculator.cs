using UnityEngine;

public static class DamageCalculator
{
    // damageDiterima = rawDamage × 100 / (100 + Defense)
    // Konsisten dengan rumus EHP = MaxHP × (100 + Defense) / 100 dan tidak pernah membuat damage nol.
    public static float Apply(float rawDamage, float defense)
    {
        if (rawDamage <= 0f)
            return 0f;

        return rawDamage * 100f / (100f + Mathf.Max(0f, defense));
    }

    public static float EffectiveHealth(float maxHealth, float defense)
    {
        return maxHealth * (100f + Mathf.Max(0f, defense)) / 100f;
    }
}
