using System;
using UnityEngine;

// Profil komposisi musuh per wave untuk run Endless. Classic 3 ronde tetap memakai profil buatan tangan di RunManager.
//   Jumlah dasar  round(Base Start + Linear Growth x (wave - 1)) sampai Linear Until, lalu x Late Growth per wave.
//   Role          Charger sejak wave 1, Shooter sejak Shooter From, Guardian sejak Guardian From
//                 (maks Guardian Max, maks Guardian Max Late sejak Guardian Late From).
//   Boss wave     tiap Boss Every wave. Objective selalu Exterminate, minimal satu Guardian supaya Boss Room terisi,
//                 musuh di titik Boss Room lebih kuat, dan skor wave itu dikali.
//   Batas         setelah dikali pengali Mode Dungeon dan faktor DDA (di RunManager), jumlah musuh dibatasi
//                 Max Enemies Per Wave. Kelebihannya diubah menjadi pengali HP dan damage semua musuh wave itu.
[Serializable]
public class WaveGenerator
{
    [Header("Jumlah Musuh")]
    [Min(1f)] public float baseStart = 3f;
    [Min(0f)] public float linearGrowth = 0.6f;
    [Min(1)] public int linearUntil = 9;
    [Min(1f)] public float lateGrowth = 1.2f;

    [Header("Role")]
    [Min(1)] public int shooterFrom = 2;
    [Min(1)] public int guardianFrom = 3;
    [Min(0)] public int guardianMax = 1;
    [Min(1)] public int guardianLateFrom = 9;
    [Min(0)] public int guardianMaxLate = 2;
    [Min(0)] public int chargerWeight = 3;
    [Min(0)] public int shooterWeight = 2;
    [Min(0)] public int guardianWeight = 1;

    [Header("Batas")]
    [Min(1)] public int maxEnemiesPerWave = 12;
    // Pengali HP dan damage per musuh kelebihan: 1 + nilai ini x kelebihan.
    [Min(0f)] public float excessStatPerEnemy = 0.08f;

    [Header("Boss Wave")]
    // 0 = tanpa boss wave.
    [Min(0)] public int bossEvery = 6;
    // Pengali HP dan damage musuh di titik Boss Room pada boss wave.
    [Min(1f)] public float bossStatMultiplier = 1.5f;
    [Min(1f)] public float bossScoreMultiplier = 2f;

    public bool IsBossWave(int wave)
    {
        return bossEvery > 0 && wave > 0 && wave % bossEvery == 0;
    }

    public int BaseCount(int wave)
    {
        wave = Mathf.Max(1, wave);

        if (wave <= linearUntil)
            return Mathf.Max(1, Mathf.RoundToInt(baseStart + linearGrowth * (wave - 1)));

        float atLimit = Mathf.RoundToInt(baseStart + linearGrowth * (linearUntil - 1));
        return Mathf.Max(1, Mathf.RoundToInt(atLimit * Mathf.Pow(lateGrowth, wave - linearUntil)));
    }

    public EnemyRoundProfile ProfileFor(int wave)
    {
        int count = BaseCount(wave);
        bool boss = IsBossWave(wave);
        int guardians = wave >= guardianFrom ? (wave >= guardianLateFrom ? guardianMaxLate : guardianMax) : 0;

        EnemySpawnEntry charger = new EnemySpawnEntry(EnemyRole.Charger, chargerWeight, 1, count);
        EnemySpawnEntry shooter = new EnemySpawnEntry(EnemyRole.Shooter, shooterWeight, 0, wave >= shooterFrom ? count : 0);
        EnemySpawnEntry guardian = new EnemySpawnEntry(EnemyRole.Guardian, guardianWeight, boss && guardians > 0 ? 1 : 0, guardians);

        return new EnemyRoundProfile((boss ? "Boss Wave " : "Wave ") + wave, count, count, charger, shooter, guardian);
    }

    // Kelebihan jumlah musuh dijadikan pengali stat. Profil diubah langsung, jadi kirim salinan (Scaled).
    public float ApplyCap(EnemyRoundProfile profile, out int excess)
    {
        excess = Mathf.Max(0, profile.maxEnemies - maxEnemiesPerWave);

        if (excess == 0)
            return 1f;

        profile.minEnemies = Mathf.Min(profile.minEnemies, maxEnemiesPerWave);
        profile.maxEnemies = maxEnemiesPerWave;
        return 1f + excessStatPerEnemy * excess;
    }
}
