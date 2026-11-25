using System;
using UnityEngine;

// Skor Aura satu run, plus rekor lokal (PlayerPrefs) per mode medan dan panjang run.
//   Kill       poin per role (boss = musuh di titik Boss Room) x (1 + Wave Bonus x (wave - 1)).
//   Objective  bonus Objective Bonus x wave setiap objective ronde selesai.
//   Boss wave  semua poin wave itu dikali Boss Score Multiplier (WaveGenerator).
//   Unbanked   skor sementara selama run. Ekstraksi (atau menang di Classic3) memindahkan semuanya ke Banked.
//              MC mati: skor akhir = Banked + Death Keep x Unbanked, sisanya hilang.
// Rekor disimpan saat run berakhir: skor terbaik dan wave terjauh yang dibersihkan.
// RunManager menambahkan komponen ini otomatis jika belum ada di scene.
[DisallowMultipleComponent]
public class AuraScore : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private BattleManager battle;
    [SerializeField] private EnemySpawner spawner;

    [Header("Poin Kill")]
    [SerializeField, Min(0)] private int chargerPoints = 10;
    [SerializeField, Min(0)] private int shooterPoints = 12;
    [SerializeField, Min(0)] private int guardianPoints = 20;
    [SerializeField, Min(0)] private int bossPoints = 50;
    // Pengali per wave: 1 + nilai ini x (wave - 1).
    [SerializeField, Min(0f)] private float waveBonus = 0.1f;

    [Header("Objective")]
    // Bonus = nilai ini x wave.
    [SerializeField, Min(0)] private int objectiveBonus = 25;

    [Header("Kematian")]
    // Bagian skor sementara yang tetap dihitung saat MC mati.
    [SerializeField, Range(0f, 1f)] private float deathKeep = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool logEvents = true;

    private ObjectiveRunner subscribedRunner;

    public static AuraScore Current { get; private set; }

    public int Banked { get; private set; }

    public int Unbanked { get; private set; }

    // Skor sementara yang hilang karena MC mati. 0 selama run masih berjalan atau setelah ekstraksi.
    public int Lost { get; private set; }

    // Skor akhir run yang sudah berakhir. Selama run berjalan sama dengan Banked + Unbanked.
    public int Final => RunEnded ? Banked : Banked + Unbanked;

    public bool RunEnded { get; private set; }

    // Wave terjauh yang dibersihkan di run ini.
    public int WavesCleared { get; private set; }

    public bool IsNewRecord { get; private set; }

    public bool IsNewWaveRecord { get; private set; }

    public int BestScore => PlayerPrefs.GetInt(RecordKey("score"), 0);

    public int BestWave => PlayerPrefs.GetInt(RecordKey("wave"), 0);

    // Dikirim setiap kali skor berubah. Argumen: poin yang ditambahkan (0 saat bank atau reset).
    public event Action<int> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    private void Awake()
    {
        if (runManager == null)
            runManager = GetComponent<RunManager>() != null ? GetComponent<RunManager>() : FindAnyObjectByType<RunManager>();

        if (battle == null)
            battle = FindAnyObjectByType<BattleManager>();

        if (spawner == null)
            spawner = FindAnyObjectByType<EnemySpawner>();

        if (runManager == null || battle == null)
            Debug.LogError("AuraScore: RunManager atau BattleManager tidak ditemukan.", this);
    }

    private void OnEnable()
    {
        Current = this;

        if (runManager != null)
        {
            runManager.RunStarted += HandleRunStarted;
            runManager.RoundCleared += HandleRoundCleared;
            runManager.RunWon += HandleRunWon;
            runManager.RunLost += HandleRunLost;
            runManager.RunExtracted += HandleRunExtracted;
        }

        if (battle != null)
            battle.EnemyDied += HandleEnemyDied;
    }

    private void OnDisable()
    {
        if (runManager != null)
        {
            runManager.RunStarted -= HandleRunStarted;
            runManager.RoundCleared -= HandleRoundCleared;
            runManager.RunWon -= HandleRunWon;
            runManager.RunLost -= HandleRunLost;
            runManager.RunExtracted -= HandleRunExtracted;
        }

        if (battle != null)
            battle.EnemyDied -= HandleEnemyDied;

        SubscribeRunner(null);

        if (Current == this)
            Current = null;
    }

    private void Update()
    {
        // ObjectiveRunner bisa dipasang setelah komponen ini aktif, jadi dicari ulang sampai ketemu.
        if (subscribedRunner != ObjectiveRunner.Current)
            SubscribeRunner(ObjectiveRunner.Current);
    }

    private void SubscribeRunner(ObjectiveRunner runner)
    {
        if (subscribedRunner != null)
            subscribedRunner.ObjectiveCompleted -= HandleObjectiveCompleted;

        subscribedRunner = runner;

        if (subscribedRunner != null)
            subscribedRunner.ObjectiveCompleted += HandleObjectiveCompleted;
    }

    // ---------- Skor ----------

    private int Wave => runManager != null ? Mathf.Max(1, runManager.CurrentRound) : 1;

    private float WaveMultiplier => 1f + waveBonus * (Wave - 1);

    private float BossWaveMultiplier =>
        runManager != null && runManager.IsBossWave ? runManager.EndlessWaves.bossScoreMultiplier : 1f;

    private void HandleRunStarted()
    {
        Banked = 0;
        Unbanked = 0;
        Lost = 0;
        WavesCleared = 0;
        RunEnded = false;
        IsNewRecord = false;
        IsNewWaveRecord = false;
        Changed?.Invoke(0);
    }

    private void HandleEnemyDied(EnemyHealth enemy)
    {
        if (RunEnded)
            return;

        int basePoints = KillPoints(enemy);
        Add(Mathf.RoundToInt(basePoints * WaveMultiplier * BossWaveMultiplier), "kill " + enemy.name);
    }

    private int KillPoints(EnemyHealth enemy)
    {
        if (spawner != null && spawner.IsBoss(enemy))
            return bossPoints;

        EnemyRole role = EnemyRole.Charger;

        if (spawner == null || !spawner.TryGetRole(enemy, out role))
        {
            EnemyController controller = enemy != null ? enemy.GetComponent<EnemyController>() : null;
            role = controller != null ? controller.Role : EnemyRole.Charger;
        }

        switch (role)
        {
            case EnemyRole.Shooter: return shooterPoints;
            case EnemyRole.Guardian: return guardianPoints;
            default: return chargerPoints;
        }
    }

    private void HandleObjectiveCompleted(IWaveObjective objective)
    {
        if (RunEnded)
            return;

        Add(Mathf.RoundToInt(objectiveBonus * Wave * BossWaveMultiplier), "objective " + objective.Type);
    }

    private void HandleRoundCleared(int round)
    {
        WavesCleared = Mathf.Max(WavesCleared, round);
    }

    private void Add(int points, string reason)
    {
        if (points <= 0)
            return;

        Unbanked += points;
        Log("+" + points + " (" + reason + "), sementara " + Unbanked + ", aman " + Banked);
        Changed?.Invoke(points);
    }

    // ---------- Akhir run ----------

    private void HandleRunExtracted()
    {
        Bank("ekstraksi");
    }

    private void HandleRunWon()
    {
        Bank("menang");
    }

    private void HandleRunLost()
    {
        if (RunEnded)
            return;

        int kept = Mathf.RoundToInt(Unbanked * deathKeep);
        Lost = Unbanked - kept;
        Banked += kept;
        Unbanked = 0;

        Log("MC mati, " + kept + " diamankan, " + Lost + " hilang, skor akhir " + Banked);
        Finish();
    }

    private void Bank(string reason)
    {
        if (RunEnded)
            return;

        Banked += Unbanked;
        Unbanked = 0;
        Lost = 0;

        Log(reason + ", semua skor diamankan, skor akhir " + Banked);
        Finish();
    }

    private void Finish()
    {
        RunEnded = true;

        string scoreKey = RecordKey("score");
        string waveKey = RecordKey("wave");

        IsNewRecord = Banked > PlayerPrefs.GetInt(scoreKey, 0);
        IsNewWaveRecord = WavesCleared > PlayerPrefs.GetInt(waveKey, 0);

        if (IsNewRecord)
            PlayerPrefs.SetInt(scoreKey, Banked);

        if (IsNewWaveRecord)
            PlayerPrefs.SetInt(waveKey, WavesCleared);

        PlayerPrefs.Save();

        Log("rekor " + RecordLabel() + ": skor " + BestScore + (IsNewRecord ? " (BARU)" : "") +
            ", wave " + BestWave + (IsNewWaveRecord ? " (BARU)" : ""));

        Changed?.Invoke(0);
    }

    // ---------- Rekor ----------

    // Rekor dipisah per mode medan (Arena/Dungeon) dan panjang run (Classic3/Endless).
    private string RecordKey(string field)
    {
        GameMode mode = runManager != null ? runManager.Mode : GameMode.Arena;
        RunLength length = runManager != null ? runManager.Length : RunLength.Classic3;
        return "aura_" + field + "_" + mode + "_" + length;
    }

    public string RecordLabel()
    {
        GameMode mode = runManager != null ? runManager.Mode : GameMode.Arena;
        RunLength length = runManager != null ? runManager.Length : RunLength.Classic3;
        return mode + " " + (length == RunLength.Endless ? "Endless" : "Klasik");
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log("AuraScore: " + message, this);
    }
}
