using UnityEngine;

// Semua angka tuning DDA. Dibuat lewat Create > MathGate > DDA Profile.
[CreateAssetMenu(fileName = "DifficultyProfile", menuName = "MathGate/DDA Profile")]
public class DifficultyProfile : ScriptableObject
{
    [Header("Skor Ronde")]
    [Range(0f, 1f)] public float weightHp = 0.4f;
    [Range(0f, 1f)] public float weightDamage = 0.35f;
    [Range(0f, 1f)] public float weightTime = 0.25f;
    // Dikurangkan dari skor setiap kali HP turun melewati Near Death Threshold.
    [Min(0f)] public float nearDeathPenalty = 0.15f;
    // Batas hampir mati, rasio dari MaxHP.
    [Range(0.01f, 1f)] public float nearDeathThreshold = 0.15f;
    // Damage sebesar rasio MaxHP ini membuat skor damage 0.
    [Min(0.01f)] public float damageRatioForZero = 0.6f;
    // Waktu wajar per musuh. Ronde yang selesai lebih cepat dari ini mendapat skor waktu 1.
    [Min(0.1f)] public float targetSecondsPerEnemy = 8f;

    [Header("Penghalusan (EMA)")]
    // Bobot skor ronde terbaru. 1 = hanya ronde terakhir yang dihitung.
    [Range(0f, 1f)] public float emaAlpha = 0.6f;

    [Header("Kontrol Faktor")]
    [Range(0f, 1f)] public float targetScore = 0.55f;
    // Selisih skor dari target yang lebih kecil dari ini tidak mengubah faktor.
    [Range(0f, 0.5f)] public float deadzone = 0.08f;
    // Asimetris, faktor turun lebih cepat daripada naik.
    [Min(0f)] public float gainUp = 0.5f;
    [Min(0f)] public float gainDown = 1f;
    [Min(0f)] public float maxStepUp = 0.1f;
    [Min(0f)] public float maxStepDown = 0.15f;
    [Min(0.1f)] public float minFactor = 0.75f;
    [Min(0.1f)] public float maxFactor = 1.25f;

    [Header("Death Streak")]
    // Faktor awal run dikurangi nilai ini untuk setiap kekalahan beruntun.
    [Min(0f)] public float deathStreakEase = 0.05f;
    [Min(0)] public int maxDeathStreak = 3;

    [Header("Knob")]
    // Heal antar ronde (rasio MaxHP) di faktor minimum dan maksimum.
    [Range(0f, 1f)] public float healRatioAtMin = 0.45f;
    [Range(0f, 1f)] public float healRatioAtMax = 0.2f;
    // Tambahan item per ronde di faktor minimum dan maksimum.
    public int itemBonusAtMin = 3;
    public int itemBonusAtMax = -1;
    // Batas token serangan AI.
    [Min(1)] public int tokensLow = 1;
    [Min(1)] public int tokensNormal = 2;
    [Min(1)] public int tokensHigh = 3;
    // Faktor di bawah Low Threshold memakai Tokens Low, di atas High Threshold memakai Tokens High.
    public float lowThreshold = 0.9f;
    public float highThreshold = 1.1f;
    // Lama tanpa pertempuran (detik) sebelum waypoint grup musuh muncul, di faktor minimum dan maksimum.
    // Pemain yang kesulitan dibantu lebih cepat.
    [Min(0f)] public float waypointDelayAtMin = 10f;
    [Min(0f)] public float waypointDelayAtMax = 30f;
    // Dipakai saat mode DDA Off.
    [Min(0f)] public float waypointDelayOff = 20f;

    private void OnValidate()
    {
        maxFactor = Mathf.Max(maxFactor, minFactor);
        highThreshold = Mathf.Max(highThreshold, lowThreshold);
    }
}
