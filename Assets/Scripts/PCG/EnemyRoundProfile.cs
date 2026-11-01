using System;
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
