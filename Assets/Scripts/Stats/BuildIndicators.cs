using UnityEngine;

// Ringkasan build MC yang dibaca AI. Semua relatif terhadap stat dasar, jadi 1,0 = normal.
//   Mobilitas  = MoveSpeed / base MoveSpeed
//   Ketahanan  = EHP / base EHP          (EHP = MaxHP × (100 + Defense) / 100)
//   DayaSerang = DPS / base DPS          (DPS = Damage × AttackRate)
//   Jangkauan  = Range / base Range
public readonly struct BuildIndicators
{
    public readonly float mobility;
    public readonly float durability;
    public readonly float offense;
    public readonly float reach;

    public static readonly BuildIndicators Neutral = new BuildIndicators(1f, 1f, 1f, 1f);

    public BuildIndicators(float mobility, float durability, float offense, float reach)
    {
        this.mobility = mobility;
        this.durability = durability;
        this.offense = offense;
        this.reach = reach;
    }

    public static BuildIndicators From(PlayerStats stats)
    {
        return stats != null ? From(stats.GetSnapshot(), stats) : Neutral;
    }

    // Untuk stat hipotetis, mis. hasil PlayerStats.Preview(gate).
    public static BuildIndicators From(StatSnapshot current, PlayerStats stats)
    {
        if (stats == null || !current.IsValid)
            return Neutral;

        float baseSpeed = stats.GetBase(StatType.MoveSpeed);
        float baseRange = stats.GetBase(StatType.Range);
        float baseEhp = EffectiveHealth(stats.GetBase(StatType.MaxHP), stats.GetBase(StatType.Defense));
        float baseDps = stats.GetBase(StatType.Damage) * stats.GetBase(StatType.AttackRate);

        return new BuildIndicators(
            Ratio(current.Get(StatType.MoveSpeed), baseSpeed),
            Ratio(EffectiveHealth(current.Get(StatType.MaxHP), current.Get(StatType.Defense)), baseEhp),
            Ratio(current.Get(StatType.Damage) * current.Get(StatType.AttackRate), baseDps),
            Ratio(current.Get(StatType.Range), baseRange)
        );
    }

    public static float Dps(StatSnapshot snapshot)
    {
        return snapshot.Get(StatType.Damage) * snapshot.Get(StatType.AttackRate);
    }

    public static float EffectiveHealth(StatSnapshot snapshot)
    {
        return EffectiveHealth(snapshot.Get(StatType.MaxHP), snapshot.Get(StatType.Defense));
    }

    private static float EffectiveHealth(float maxHealth, float defense)
    {
        return DamageCalculator.EffectiveHealth(maxHealth, defense);
    }

    private static float Ratio(float value, float baseValue)
    {
        return baseValue > 0f ? value / baseValue : 1f;
    }

    public override string ToString()
    {
        return "Mob " + mobility.ToString("0.00") +
               "  Tahan " + durability.ToString("0.00") +
               "  Serang " + offense.ToString("0.00") +
               "  Jangkau " + reach.ToString("0.00");
    }
}
