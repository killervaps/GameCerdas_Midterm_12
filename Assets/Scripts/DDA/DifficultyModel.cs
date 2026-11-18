using System;
using System.Collections.Generic;
using UnityEngine;

public enum DdaMode
{
    // Faktor 1, semua knob kembali ke nilai Inspector masing-masing.
    Off,
    Adaptive,
    // Faktor tetap di Min Factor atau Max Factor, untuk demo dan perbandingan.
    ForceEasy,
    ForceHard
}

// Dynamic Difficulty Adjustment.
// Skor ronde dari HP akhir, damage, dan waktu dihaluskan dengan EMA, lalu faktor kesulitan digeser ke arah skor target.
// Faktor mengatur jumlah musuh, jumlah item, heal antar ronde, dan batas token serangan AI.
// PlayerPerformanceTracker memanggil Evaluate saat ronde bersih, sebelum ronde berikutnya disiapkan,
// jadi komposisi musuh dan heal ronde berikutnya sudah memakai faktor baru.
[DisallowMultipleComponent]
public class DifficultyModel : MonoBehaviour
{
    private const string DeathStreakKey = "dda_death_streak";

    [SerializeField] private DifficultyProfile profile;
    // Kosong = dicari otomatis.
    [SerializeField] private RunManager runManager;
    [SerializeField] private DdaMode mode = DdaMode.Adaptive;
    [SerializeField] private bool logRounds = true;

    private readonly List<WavePerformance> history = new List<WavePerformance>();
    private readonly List<float> factorHistory = new List<float>();

    // Faktor hasil kontrol adaptif. Tetap disimpan saat mode lain dipakai, jadi kembali ke Adaptive tidak mengulang dari 1.
    private float adaptiveFactor = 1f;
    private float smoothed;
    private bool hasSmoothed;
    private int deathStreak;

    public static DifficultyModel Current { get; private set; }

    public DifficultyProfile Profile => profile;

    public DdaMode Mode => mode;

    // False = mode Off, knob tidak mengubah apa pun.
    public bool Active => mode != DdaMode.Off;

    public float Factor { get; private set; } = 1f;

    public float SmoothedScore => smoothed;

    public bool HasSmoothedScore => hasSmoothed;

    public int DeathStreak => deathStreak;

    public float MinFactor => profile.minFactor;

    public float MaxFactor => profile.maxFactor;

    // Satu entri per ronde yang dinilai di run ini, termasuk ronde saat MC mati.
    public IReadOnlyList<WavePerformance> History => history;

    // Faktor awal run, lalu faktor setelah setiap ronde.
    public IReadOnlyList<float> FactorHistory => factorHistory;

    // ---------- Knob ----------

    public float EnemyMultiplier => Active ? Factor : 1f;

    public int ItemBonus => Active ? Mathf.RoundToInt(Mathf.Lerp(profile.itemBonusAtMin, profile.itemBonusAtMax, FactorT)) : 0;

    // Rasio MaxHP untuk heal antar ronde. Hanya dipakai jika Active.
    public float HealRatio => Mathf.Lerp(profile.healRatioAtMin, profile.healRatioAtMax, FactorT);

    public int MaxTokens =>
        Factor < profile.lowThreshold ? profile.tokensLow :
        Factor > profile.highThreshold ? profile.tokensHigh :
        profile.tokensNormal;

    private float FactorT => Mathf.InverseLerp(profile.minFactor, profile.maxFactor, Factor);

    // Dipanggil setiap kali satu ronde dinilai, juga jika faktornya tidak berubah. Argumen: faktor lama, faktor baru, rekap ronde.
    public event Action<float, float, WavePerformance> FactorChanged;

    public event Action<DdaMode> ModeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    private void Awake()
    {
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<DifficultyProfile>();
            profile.name = "DifficultyProfile (bawaan)";
            Debug.LogWarning("DifficultyModel: Profile kosong, memakai nilai bawaan. Buat asset lewat Create > MathGate > DDA Profile.", this);
        }

        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();

        deathStreak = Mathf.Clamp(PlayerPrefs.GetInt(DeathStreakKey, 0), 0, profile.maxDeathStreak);
    }

    private void OnEnable()
    {
        if (Current != null && Current != this)
            Debug.LogWarning("DifficultyModel: ada lebih dari satu di scene, yang terakhir aktif dipakai.", this);

        Current = this;

        if (runManager != null)
            runManager.RunStarted += HandleRunStarted;
    }

    private void OnDisable()
    {
        if (runManager != null)
            runManager.RunStarted -= HandleRunStarted;

        if (Current == this)
            Current = null;
    }

    // ---------- Run ----------

    private void HandleRunStarted()
    {
        history.Clear();
        factorHistory.Clear();
        smoothed = 0f;
        hasSmoothed = false;

        adaptiveFactor = Mathf.Clamp(1f - profile.deathStreakEase * deathStreak, profile.minFactor, profile.maxFactor);
        Factor = EffectiveFactor();
        factorHistory.Add(Factor);
        ApplyTokens();

        Log("DDA run baru, mode " + mode + ", death streak " + deathStreak + ", faktor awal " + Factor.ToString("0.00") + " | " + DescribeKnobs());
    }

    // Ronde bersih. Dipanggil sinkron dari handler RoundCleared.
    public void Evaluate(WavePerformance perf)
    {
        Score(ref perf);

        float before = Factor;

        UpdateSmoothed(perf.score);

        if (mode == DdaMode.Adaptive)
            adaptiveFactor = Step(adaptiveFactor, smoothed);

        Factor = EffectiveFactor();

        history.Add(perf);
        factorHistory.Add(Factor);
        ApplyTokens();

        Log("DDA R" + perf.round + " skor " + perf.score.ToString("0.00") + " (EMA " + smoothed.ToString("0.00") + ") -> faktor " +
            before.ToString("0.00") + "->" + Factor.ToString("0.00") + " | " + DescribeKnobs() + " | " + DescribeScore(perf));

        FactorChanged?.Invoke(before, Factor, perf);
    }

    // MC mati. Faktor run ini tidak diubah lagi, death streak naik untuk run berikutnya.
    public void RegisterDeath(WavePerformance perf)
    {
        Score(ref perf);
        UpdateSmoothed(perf.score);

        history.Add(perf);
        SetDeathStreak(deathStreak + 1);

        Log("DDA R" + perf.round + " MC mati, skor " + perf.score.ToString("0.00") + ", death streak " + deathStreak +
            ", faktor awal run berikutnya " + Mathf.Clamp(1f - profile.deathStreakEase * deathStreak, profile.minFactor, profile.maxFactor).ToString("0.00"));

        FactorChanged?.Invoke(Factor, Factor, perf);
    }

    public void RegisterVictory()
    {
        SetDeathStreak(0);
        Log("DDA run menang, death streak di-reset");
    }

    public void ResetDeathStreak()
    {
        SetDeathStreak(0);
        Log("DDA death streak di-reset, berlaku mulai run berikutnya");
    }

    // ---------- Mode ----------

    public void SetMode(DdaMode newMode)
    {
        if (newMode == mode)
            return;

        float before = Factor;

        mode = newMode;
        Factor = EffectiveFactor();
        ApplyTokens();

        Log("DDA mode " + mode + ", faktor " + before.ToString("0.00") + "->" + Factor.ToString("0.00") + " | " + DescribeKnobs());

        ModeChanged?.Invoke(mode);
    }

    public void CycleMode()
    {
        SetMode((DdaMode)(((int)mode + 1) % 4));
    }

    // ---------- Rumus ----------

    private void Score(ref WavePerformance perf)
    {
        perf.hpScore = Mathf.Clamp01(perf.hpEndRatio);

        float damageForZero = perf.maxHealth * profile.damageRatioForZero;
        perf.damageScore = damageForZero > 0f ? 1f - Mathf.Clamp01(perf.damageTaken / damageForZero) : 1f;

        perf.timeScore = perf.enemyCount > 0 && perf.duration > 0.01f
            ? Mathf.Clamp01(profile.targetSecondsPerEnemy * perf.enemyCount / perf.duration)
            : 1f;

        perf.score = Mathf.Clamp01(
            profile.weightHp * perf.hpScore +
            profile.weightDamage * perf.damageScore +
            profile.weightTime * perf.timeScore -
            profile.nearDeathPenalty * perf.nearDeathCount
        );
    }

    private void UpdateSmoothed(float score)
    {
        smoothed = hasSmoothed ? Mathf.Lerp(smoothed, score, profile.emaAlpha) : score;
        hasSmoothed = true;
    }

    private float Step(float factor, float score)
    {
        float delta = score - profile.targetScore;

        if (Mathf.Abs(delta) < profile.deadzone)
            return factor;

        float step = delta > 0f
            ? Mathf.Min(delta * profile.gainUp, profile.maxStepUp)
            : Mathf.Max(delta * profile.gainDown, -profile.maxStepDown);

        return Mathf.Clamp(factor + step, profile.minFactor, profile.maxFactor);
    }

    private float EffectiveFactor()
    {
        switch (mode)
        {
            case DdaMode.Off: return 1f;
            case DdaMode.ForceEasy: return profile.minFactor;
            case DdaMode.ForceHard: return profile.maxFactor;
            default: return adaptiveFactor;
        }
    }

    private void ApplyTokens()
    {
        TacticalCoordinator coordinator = TacticalCoordinator.Current;

        if (coordinator == null || coordinator.Tokens == null)
            return;

        if (Active)
            coordinator.Tokens.SetMaxTokens(MaxTokens);
        else
            coordinator.Tokens.RestoreDefaultLimits();
    }

    private void SetDeathStreak(int value)
    {
        deathStreak = Mathf.Clamp(value, 0, profile.maxDeathStreak);
        PlayerPrefs.SetInt(DeathStreakKey, deathStreak);
        PlayerPrefs.Save();
    }

    // ---------- Teks ----------

    public string DescribeKnobs()
    {
        if (!Active)
            return "knob default";

        int bonus = ItemBonus;

        return "musuh x" + EnemyMultiplier.ToString("0.00") +
               ", item " + (bonus >= 0 ? "+" : "") + bonus +
               ", heal " + Mathf.RoundToInt(HealRatio * 100f) + "%" +
               ", token " + MaxTokens;
    }

    public static string DescribeScore(WavePerformance perf)
    {
        return "HP " + perf.hpScore.ToString("0.00") +
               ", damage " + perf.damageScore.ToString("0.00") +
               ", waktu " + perf.timeScore.ToString("0.00") +
               ", hampir mati " + perf.nearDeathCount;
    }

    private void Log(string message)
    {
        if (logRounds)
            Debug.Log(message, this);
    }
}
