using System.Collections.Generic;
using UnityEngine;

public enum ThreatLevel
{
    Safe,
    Medium,
    Danger
}

public readonly struct ThreatResult
{
    public readonly float enemyPower;
    public readonly float playerPower;
    public readonly float ratio;
    public readonly ThreatLevel level;

    public ThreatResult(float enemyPower, float playerPower, float ratio, ThreatLevel level)
    {
        this.enemyPower = enemyPower;
        this.playerPower = playerPower;
        this.ratio = ratio;
        this.level = level;
    }

    public string Label => ThreatEstimator.LevelLabel(level);

    public string ColorHex => ThreatEstimator.LevelColorHex(level);
}

// Satu rumus threat untuk dua sisi. Label di kartu gate dan keputusan AI memakai perhitungan yang sama.
//   Power      = DPS × EHP
//   ThreatRatio = Σ Power musuh / Power MC
// Alasan: waktu MC menghabisi musuh ≈ EHP musuh / DPS MC, waktu musuh menghabisi MC ≈ EHP MC / DPS musuh.
// Perbandingan keduanya sama dengan rasio power.
public class ThreatEstimator : MonoBehaviour
{
    [Header("Role Data")]
    // Stat musuh dibaca dari prefab di spawner. Isi daftar di bawah jika ingin menimpa atau tanpa spawner.
    [SerializeField] private EnemySpawner spawner;
    [SerializeField] private EnemyRoleData[] roleDataOverrides;

    [Header("Thresholds")]
    [SerializeField] private float safeBelow = 0.8f;
    [SerializeField] private float dangerAbove = 1.25f;

    public static ThreatEstimator Current { get; private set; }

    private void OnEnable()
    {
        Current = this;
    }

    private void OnDisable()
    {
        if (Current == this)
            Current = null;
    }

    public static float PlayerPower(StatSnapshot stats)
    {
        return BuildIndicators.Dps(stats) * BuildIndicators.EffectiveHealth(stats);
    }

    public static float EnemyPower(EnemyRoleData data)
    {
        return data != null ? data.DPS * data.EffectiveHealth : 0f;
    }

    public bool TryGetRoleData(EnemyRole role, out EnemyRoleData data)
    {
        if (roleDataOverrides != null)
        {
            foreach (EnemyRoleData candidate in roleDataOverrides)
            {
                if (candidate != null && candidate.Role == role)
                {
                    data = candidate;
                    return true;
                }
            }
        }

        if (spawner != null && spawner.TryGetRoleData(role, out data))
            return true;

        data = null;
        return false;
    }

    public float WavePower(WaveData wave)
    {
        if (wave == null)
            return 0f;

        float total = 0f;

        foreach (WaveEntry entry in wave.Entries)
        {
            if (entry.count <= 0)
                continue;

            if (TryGetRoleData(entry.role, out EnemyRoleData data))
                total += entry.count * EnemyPower(data);
            else
                Debug.LogWarning("ThreatEstimator: Role Data untuk " + entry.role + " tidak ditemukan.", this);
        }

        return total;
    }

    // Threat preview di kartu gate: wave berikutnya terhadap stat MC (hipotetis) setelah gate.
    public ThreatResult Evaluate(WaveData wave, StatSnapshot playerStats)
    {
        return Build(WavePower(wave), PlayerPower(playerStats));
    }

    // Threat saat bertarung: musuh yang masih hidup (EHP dari sisa HP) terhadap build MC sekarang.
    public ThreatResult EvaluateLive(IReadOnlyList<EnemyController> enemies, StatSnapshot playerStats)
    {
        float total = 0f;

        foreach (EnemyController enemy in enemies)
        {
            if (enemy == null || enemy.Health.IsDead || enemy.RoleData == null)
                continue;

            float ehp = DamageCalculator.EffectiveHealth(enemy.Health.CurrentHealth, enemy.Health.Defense);
            total += enemy.RoleData.DPS * ehp;
        }

        return Build(total, PlayerPower(playerStats));
    }

    public ThreatLevel Classify(float ratio)
    {
        if (ratio < safeBelow)
            return ThreatLevel.Safe;

        return ratio > dangerAbove ? ThreatLevel.Danger : ThreatLevel.Medium;
    }

    private ThreatResult Build(float enemyPower, float playerPower)
    {
        float ratio = playerPower > 0f ? enemyPower / playerPower : float.PositiveInfinity;
        return new ThreatResult(enemyPower, playerPower, ratio, Classify(ratio));
    }

    public static string LevelLabel(ThreatLevel level)
    {
        switch (level)
        {
            case ThreatLevel.Safe: return "Aman";
            case ThreatLevel.Danger: return "Bahaya";
            default: return "Sedang";
        }
    }

    public static string LevelColorHex(ThreatLevel level)
    {
        switch (level)
        {
            case ThreatLevel.Safe: return "#55dd55";
            case ThreatLevel.Danger: return "#ff5555";
            default: return "#ffcc33";
        }
    }
}
