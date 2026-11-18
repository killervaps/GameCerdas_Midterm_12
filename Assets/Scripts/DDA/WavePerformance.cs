using System;

// Rekap performa MC dalam satu ronde. Diisi PlayerPerformanceTracker, skornya dihitung DifficultyModel.
[Serializable]
public struct WavePerformance
{
    public int round;
    // Detik dari ronde mulai sampai ronde bersih atau MC mati.
    public float duration;
    public int enemyCount;
    public int kills;
    // Damage setelah Defense.
    public float damageTaken;
    public float maxHealth;
    public float hpStartRatio;
    public float hpEndRatio;
    // Berapa kali MC mendapat heal saat bertarung (health orb).
    public int healEvents;
    // Berapa kali HP turun melewati batas hampir mati.
    public int nearDeathCount;
    public bool died;

    // Hasil hitung, 0 sampai 1.
    public float hpScore;
    public float damageScore;
    public float timeScore;
    public float score;
}
