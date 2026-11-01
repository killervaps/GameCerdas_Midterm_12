using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RunState
{
    Idle,
    // Di Safezone, memilih gate untuk ronde berikutnya.
    Preparing,
    Battle,
    // Ronde bersih, jeda sebelum kembali ke Safezone.
    RoundCleared,
    Victory,
    Defeat
}

public enum HealRule
{
    None,
    Partial,
    Full
}

// Alur satu run.
// Safezone (pilih gate) -> teleport ke arena -> spawn wave -> pertempuran -> ronde bersih -> kembali ke Safezone.
// Arena dan komposisi musuh tiap ronde dibuat PCG dari seed run saat masuk Safezone, posisi musuh saat ronde mulai.
// Menang jika ronde terakhir bersih, kalah jika HP MC habis. Build menumpuk sepanjang run dan di-reset saat run baru.
public class RunManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GateSelectionRoom gateRoom;
    [SerializeField] private EnemySpawner spawner;
    [SerializeField] private BattleManager battle;
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Arena")]
    // Opsional. Jika diisi, arena dipilih acak ber-seed tiap ronde (arena ronde sebelumnya peluangnya dikurangi).
    // Kosong = selalu memakai titik masuk yang sudah diatur di GateSelectionRoom.
    [SerializeField] private ArenaSelector arenaSelector;

    [Header("Rounds (PCG)")]
    // Satu profil per ronde, urut dari ronde 1. Jumlah profil = jumlah ronde.
    [SerializeField] private EnemyRoundProfile[] rounds = EnemyRoundProfile.Defaults();

    [Header("Between Rounds")]
    // TODO(desain): aturan heal antar ronde belum diputuskan. Sementara pulih 30% MaxHP.
    [SerializeField] private HealRule healRule = HealRule.Partial;
    [SerializeField, Range(0f, 1f)] private float partialHealRatio = 0.3f;
    // Jeda setelah ronde bersih sebelum MC dikembalikan ke Safezone.
    [SerializeField] private float returnDelay = 2f;

    [Header("Flow")]
    [SerializeField] private bool startOnPlay = true;

    [Header("Debug")]
    [SerializeField] private bool logEvents = true;

    private Coroutine pendingReturn;
    private WaveData currentWave;

    public RunState State { get; private set; } = RunState.Idle;

    // Dimulai dari 1.
    public int CurrentRound { get; private set; }

    public int TotalRounds => rounds != null ? rounds.Length : 0;

    // Wave ronde saat ini hasil PCG. Saat Preparing, ini wave yang akan dihadapi setelah memilih gate.
    public WaveData CurrentWave => currentWave;

    public EnemyRoundProfile CurrentProfile =>
        rounds != null && CurrentRound >= 1 && CurrentRound <= rounds.Length ? rounds[CurrentRound - 1] : null;

    // Arena ronde saat ini. Null jika tidak memakai ArenaSelector.
    public Arena CurrentArena => arenaSelector != null ? arenaSelector.Current : null;

    // Dimatikan GameUI saat menu utama dipakai, supaya run baru dimulai dari tombol "Mulai".
    public bool StartOnPlay
    {
        get => startOnPlay;
        set => startOnPlay = value;
    }

    public float RunStartTime { get; private set; }

    public float RunEndTime { get; private set; }

    public float RunDuration => (State == RunState.Victory || State == RunState.Defeat ? RunEndTime : Time.time) - RunStartTime;

    public event Action<RunState> StateChanged;

    // Run baru dimulai, sebelum arena dan wave ronde 1 disiapkan (seed PCG sudah dibuat).
    public event Action RunStarted;

    public event Action<int> RoundStarted;

    public event Action<int> RoundCleared;

    public event Action RunWon;

    public event Action RunLost;

    private void Awake()
    {
        if (gateRoom == null || spawner == null || battle == null || playerHealth == null)
        {
            Debug.LogError("RunManager: isi Gate Room, Spawner, Battle, dan Player Health di Inspector.", this);
            enabled = false;
            return;
        }

        if (TotalRounds == 0)
            Debug.LogError("RunManager: daftar Rounds masih kosong.", this);

        // Gate dipilih sekali per ronde, jadi batas gate sama dengan jumlah ronde.
        gateRoom.UseExternalDriver(TotalRounds);
    }

    private void OnEnable()
    {
        if (gateRoom != null)
            gateRoom.GateChosen += HandleGateChosen;

        if (battle != null)
        {
            battle.AllEnemiesDead += HandleAllEnemiesDead;
            battle.PlayerDied += HandlePlayerDied;
        }
    }

    private void OnDisable()
    {
        if (gateRoom != null)
            gateRoom.GateChosen -= HandleGateChosen;

        if (battle != null)
        {
            battle.AllEnemiesDead -= HandleAllEnemiesDead;
            battle.PlayerDied -= HandlePlayerDied;
        }
    }

    private void OnDestroy()
    {
        if (currentWave != null)
            Destroy(currentWave);
    }

    private void Start()
    {
        if (startOnPlay)
            StartRun();
    }

    // Run baru dari ronde 1. Juga dipakai tombol retry.
    [ContextMenu("Start Run")]
    public void StartRun()
    {
        if (!Application.isPlaying)
            return;

        CancelPendingReturn();

        battle.EndBattle();
        spawner.ClearWave();

        CurrentRound = 1;
        RunStartTime = Time.time;
        RunEndTime = 0f;

        if (arenaSelector != null)
            arenaSelector.ResetHistory();

        PcgDirector.BeginNewRun();
        RunStarted?.Invoke();

        // Wave diberikan dulu supaya kartu gate bisa menghitung ancaman.
        PrepareRound();

        // Build kembali ke base dulu, baru HP dipulihkan supaya penuh terhadap MaxHP dasar.
        gateRoom.ResetRun();
        playerHealth.RestoreFull();

        Log("run baru, " + TotalRounds + " ronde");

        SetState(RunState.Preparing);
    }

    // Menghentikan run tanpa hasil (kembali ke menu utama). Musuh dibersihkan dan state kembali Idle.
    public void StopRun()
    {
        if (!Application.isPlaying)
            return;

        CancelPendingReturn();

        battle.EndBattle();
        spawner.ClearWave();

        // MC yang kalah lalu kembali ke menu tidak boleh tetap dalam pose mati di Safezone.
        playerHealth.RestoreFull();

        Log("run dihentikan");

        SetState(RunState.Idle);
    }

    private void HandleGateChosen(MathGateData gate)
    {
        if (State != RunState.Preparing)
            return;

        BeginRound();
    }

    private void BeginRound()
    {
        SetState(RunState.Battle);

        Log("ronde " + CurrentRound + " mulai, " + (CurrentWave != null ? CurrentWave.Describe() : "-"));

        RoundStarted?.Invoke(CurrentRound);

        battle.BeginBattle(spawner.SpawnWave(CurrentWave, CurrentRound));
    }

    private void HandleAllEnemiesDead()
    {
        if (State != RunState.Battle)
            return;

        RoundCleared?.Invoke(CurrentRound);

        if (CurrentRound >= TotalRounds)
        {
            RunEndTime = Time.time;
            Log("MENANG, " + TotalRounds + " ronde bersih dalam " + RunDuration.ToString("0.0") + " s");

            SetState(RunState.Victory);
            RunWon?.Invoke();
            return;
        }

        Log("ronde " + CurrentRound + " bersih, kembali ke Safezone dalam " + returnDelay.ToString("0.#") + " s");

        SetState(RunState.RoundCleared);
        pendingReturn = StartCoroutine(ReturnAfterDelay());
    }

    private IEnumerator ReturnAfterDelay()
    {
        yield return new WaitForSeconds(returnDelay);

        pendingReturn = null;

        ApplyHealBetweenRounds();

        CurrentRound++;
        spawner.ClearWave();
        PrepareRound();
        gateRoom.ReturnToSafezone();

        SetState(RunState.Preparing);
    }

    // Bagian PCG yang dibuat di Safezone: arena, lalu komposisi musuh. Posisi musuh baru dibuat saat ronde mulai.
    private void PrepareRound()
    {
        PrepareArena();
        GenerateWave();
        gateRoom.SetUpcomingWave(CurrentWave);
    }

    // Memilih arena ronde berikutnya, lalu mengarahkan teleport MC dan spawner musuh ke arena itu.
    private void PrepareArena()
    {
        if (arenaSelector == null)
            return;

        Arena arena = arenaSelector.PickNext(PcgDirector.Stream(CurrentRound, PcgStream.Arena));

        if (arena == null)
            return;

        spawner.UseArena(arena);
        gateRoom.SetArenaSpawn(arena.PlayerEntry);

        Log("ronde " + CurrentRound + " di arena " + arena.DisplayName);
    }

    private void GenerateWave()
    {
        if (currentWave != null)
            Destroy(currentWave);

        EnemyRoundProfile profile = CurrentProfile;
        SeededRandom random = PcgDirector.Stream(CurrentRound, PcgStream.EnemyComposition);
        // Arena sudah dipilih lebih dulu, jadi tema arena bisa ikut menentukan komposisi.
        PcgSpawnArea area = spawner.CurrentArea;
        IReadOnlyList<ArenaRoleBias> bias = area != null ? area.RoleBias : null;
        WaveEntry[] entries = PcgEnemyComposition.Generate(profile, random, bias);
        string label = profile != null && !string.IsNullOrEmpty(profile.label) ? profile.label : "Ronde " + CurrentRound;

        currentWave = WaveData.CreateRuntime(label, entries);

        Log("komposisi " + label + " (PCG" + (bias != null && bias.Count > 0 ? ", bias " + area.name : "") + "): " + currentWave.Describe());
    }

    private void HandlePlayerDied()
    {
        if (State != RunState.Battle)
            return;

        CancelPendingReturn();

        RunEndTime = Time.time;
        Log("KALAH di ronde " + CurrentRound);

        SetState(RunState.Defeat);
        RunLost?.Invoke();
    }

    private void ApplyHealBetweenRounds()
    {
        switch (healRule)
        {
            case HealRule.Full:
                playerHealth.RestoreFull();
                break;
            case HealRule.Partial:
                playerHealth.Heal(playerHealth.MaxHealth * partialHealRatio);
                break;
        }
    }

    private void CancelPendingReturn()
    {
        if (pendingReturn != null)
        {
            StopCoroutine(pendingReturn);
            pendingReturn = null;
        }
    }

    private void SetState(RunState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log("RunManager: " + message, this);
    }
}
