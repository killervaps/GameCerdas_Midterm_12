using System.Collections.Generic;
using UnityEngine;

// Rumus stat murni, tanpa MonoBehaviour, supaya bisa dipakai PlayerStats, threat preview, dan uji di Editor.
//   nilai = (base + Σ add − Σ subtract) × Π multiply × Π (1 / divide)
//   nilai = max(nilai, clampMin)
// Selalu dihitung dari base, sehingga urutan modifier tidak memengaruhi hasil.
public static class StatCalculator
{
    // 'extra' opsional, dipakai Preview untuk menambahkan efek gate tanpa menyalin list.
    public static float Calculate(
        StatType stat,
        float baseValue,
        float clampMin,
        IReadOnlyList<ModifierEffect> modifiers,
        IReadOnlyList<ModifierEffect> extra = null)
    {
        float additive = baseValue;
        float multiplier = 1f;

        Accumulate(stat, modifiers, ref additive, ref multiplier);
        Accumulate(stat, extra, ref additive, ref multiplier);

        return Mathf.Max(additive * multiplier, clampMin);
    }

    private static void Accumulate(
        StatType stat,
        IReadOnlyList<ModifierEffect> modifiers,
        ref float additive,
        ref float multiplier)
    {
        if (modifiers == null)
            return;

        for (int i = 0; i < modifiers.Count; i++)
        {
            ModifierEffect effect = modifiers[i];

            if (effect.stat != stat)
                continue;

            if (!ModifierEffect.IsValidValue(effect.op, effect.value))
                continue;

            switch (effect.op)
            {
                case ModifierOp.Add:
                    additive += effect.value;
                    break;
                case ModifierOp.Subtract:
                    additive -= effect.value;
                    break;
                case ModifierOp.Multiply:
                    multiplier *= effect.value;
                    break;
                case ModifierOp.Divide:
                    multiplier /= effect.value;
                    break;
            }
        }
    }
}
