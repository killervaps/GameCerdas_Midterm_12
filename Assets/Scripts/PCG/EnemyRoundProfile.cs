using System;
using System.Collections.Generic;
using UnityEngine;

// Satu baris tabel role di profil ronde.
[Serializable]
public class EnemySpawnEntry
{
    public EnemyRole role = EnemyRole.Charger;
    // Bobot weighted random. 0 = tidak pernah terpilih lewat undian (tetap muncul sebanyak Min).
    [Min(0)] public int weight = 1;
    // Jumlah yang selalu muncul sebelum undian dimulai.
    [Min(0)] public int min;
    // Batas jumlah role ini dalam satu ronde. Role yang sudah mencapai batas tidak ikut diundi lagi, 0 = role tidak muncul.
    [Min(0)] public int max = 3;

    public EnemySpawnEntry()
    {
    }

    public EnemySpawnEntry(EnemyRole role, int weight, int min, int max)
    {
        this.role = role;
        this.weight = weight;
        this.min = min;
        this.max = max;
    }
}

// Challenge 4: biaya satu role dalam Difficulty Budget. Makin kuat role, makin mahal.
[Serializable]
public class RoleCost
{
    public EnemyRole role = EnemyRole.Charger;
    [Min(1)] public int cost = 1;

    public RoleCost()
    {
    }

    public RoleCost(EnemyRole role, int cost)
    {
        this.role = role;
        this.cost = cost;
    }

    // Role yang tidak ada di tabel dianggap berbiaya 1. Biaya minimal 1 supaya undian budget selalu berhenti.
    public static int Of(EnemyRole role, IReadOnlyList<RoleCost> costs)
    {
        if (costs != null)
        {
            for (int i = 0; i < costs.Count; i++)
            {
                if (costs[i] != null && costs[i].role == role)
                    return Math.Max(1, costs[i].cost);
            }
        }

        return 1;
    }

    // Charger dan Shooter setara, Guardian (tank, HP 150) lebih mahal.
    public static RoleCost[] Defaults()
    {
        return new[]
        {
            new RoleCost(EnemyRole.Charger, 2),
            new RoleCost(EnemyRole.Shooter, 2),
            new RoleCost(EnemyRole.Guardian, 3)
        };
    }
}

// Penyesuaian komposisi oleh arena bertema, mis. arena menara memperbanyak Shooter.
// Dipasang di PcgSpawnArea tiap arena. Role yang tidak ada di daftar arena memakai profil ronde apa adanya.
[Serializable]
public class ArenaRoleBias
{
    public EnemyRole role = EnemyRole.Shooter;
    // Pengali bobot undian role ini. 2 = dua kali lebih sering terpilih. Bobot 0 di profil tetap 0.
    [Min(0f)] public float weightMultiplier = 2f;
    // Tambahan batas Max role ini di arena ini. Profil dengan Max 0 tetap 0 (role sengaja dimatikan di ronde itu).
    [Min(0)] public int maxBonus;

    public ArenaRoleBias()
    {
    }

    public ArenaRoleBias(EnemyRole role, float weightMultiplier, int maxBonus)
    {
        this.role = role;
        this.weightMultiplier = weightMultiplier;
        this.maxBonus = maxBonus;
    }
}

// Aturan komposisi musuh satu ronde, pengganti WaveData yang isinya tetap.
// Jumlah total diacak di [Min Enemies, Max Enemies], jatah minimum tiap role diisi dulu,
// lalu sisanya diundi weighted random dari tabel role.
[Serializable]
public class EnemyRoundProfile
{
    public string label = "Ronde";
    [Min(1)] public int minEnemies = 2;
    [Min(1)] public int maxEnemies = 3;
    // Challenge 4: Difficulty Budget. Lebih dari 0 = jumlah musuh ditentukan budget (total biaya role, lihat Role Costs
    // di RunManager), bukan Min/Max Enemies. Undian tetap weighted random, tapi hanya role yang biayanya masih muat.
    // 0 = mati, memakai Min/Max Enemies seperti biasa.
    [Min(0)] public int difficultyBudget;
    public EnemySpawnEntry[] roles = Array.Empty<EnemySpawnEntry>();

    public EnemyRoundProfile()
    {
    }

    public EnemyRoundProfile(string label, int minEnemies, int maxEnemies, params EnemySpawnEntry[] roles)
    {
        this.label = label;
        this.minEnemies = minEnemies;
        this.maxEnemies = maxEnemies;
        this.roles = roles;
    }

    // Isi awal yang mendekati wave lama: R1 2 Charger, R2 Charger + Shooter, R3 satu tiap role.
    public static EnemyRoundProfile[] Defaults()
    {
        return new[]
        {
            new EnemyRoundProfile("Ronde 1", 2, 3,
                new EnemySpawnEntry(EnemyRole.Charger, 3, 1, 3),
                new EnemySpawnEntry(EnemyRole.Shooter, 1, 0, 1),
                new EnemySpawnEntry(EnemyRole.Guardian, 0, 0, 0)),
            new EnemyRoundProfile("Ronde 2", 2, 3,
                new EnemySpawnEntry(EnemyRole.Charger, 2, 1, 2),
                new EnemySpawnEntry(EnemyRole.Shooter, 2, 1, 2),
                new EnemySpawnEntry(EnemyRole.Guardian, 0, 0, 0)),
            new EnemyRoundProfile("Ronde 3", 3, 4,
                new EnemySpawnEntry(EnemyRole.Charger, 2, 1, 2),
                new EnemySpawnEntry(EnemyRole.Shooter, 2, 1, 2),
                new EnemySpawnEntry(EnemyRole.Guardian, 1, 1, 1))
        };
    }
}
