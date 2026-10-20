using System;
using System.Collections;
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
// Menang jika ronde terakhir bersih, kalah jika HP MC habis. Build menumpuk sepanjang run dan di-reset saat run baru.
public class RunManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GateSelectionRoom gateRoom;
    [SerializeField] private EnemySpawner spawner;
    [SerializeField] private BattleManager battle;
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Arena")]
    // Opsional. Jika diisi, arena dipilih acak tiap ronde (arena ronde sebelumnya peluangnya dikurangi).
    // Kosong = selalu memakai arena dan SpawnPoint yang sudah diatur di GateSelectionRoom dan EnemySpawner.
    [SerializeField] private ArenaSelector arenaSelector;

    [Header("Waves")]
    // Satu WaveData per ronde, urut dari ronde 1.
    [SerializeField] private WaveData[] waves;

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

    public RunState State { get; private set; } = RunState.Idle;

    // Dimulai dari 1.
    public int CurrentRound { get; private set; }

    public int TotalRounds => waves != null ? waves.Length : 0;

    // Wave ronde saat ini. Saat Preparing, ini wave yang akan dihadapi setelah memilih gate.
    public WaveData CurrentWave =>
        waves != null && CurrentRound >= 1 && CurrentRound <= waves.Length ? waves[CurrentRound - 1] : null;

    // Arena ronde saat ini. Null jika tidak memakai ArenaSelector.
    public Arena CurrentArena => arenaSelector != null ? arenaSelector.Current : null;

    public float RunStartTime { get; private set; }

    public float RunEndTime { get; private set; }

    public float RunDuration => (State == RunState.Victory || State == RunState.Defeat ? RunEndTime : Time.time) - RunStartTime;

    public event Action<RunState> StateChanged;

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
            Debug.LogError("RunManager: daftar Waves masih kosong.", this);

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

        PrepareArena();

        // Wave diberikan dulu supaya kartu gate bisa menghitung ancaman.
        gateRoom.SetUpcomingWave(CurrentWave);

        // Build kembali ke base dulu, baru HP dipulihkan supaya penuh terhadap MaxHP dasar.
        gateRoom.ResetRun();
        playerHealth.RestoreFull();

        Log("run baru, " + TotalRounds + " ronde");

        SetState(RunState.Preparing);
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

        battle.BeginBattle(spawner.SpawnWave(CurrentWave));
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
        PrepareArena();
        gateRoom.SetUpcomingWave(CurrentWave);
        gateRoom.ReturnToSafezone();

        SetState(RunState.Preparing);
    }

    // Memilih arena ronde berikutnya, lalu mengarahkan teleport MC dan SpawnPoint musuh ke arena itu.
    private void PrepareArena()
    {
        if (arenaSelector == null)
            return;

        Arena arena = arenaSelector.PickNext();

        if (arena == null)
            return;

        spawner.UseSpawnPoints(arena.SpawnPoints);
        gateRoom.SetArenaSpawn(arena.PlayerEntry);

        Log("ronde " + CurrentRound + " di arena " + arena.DisplayName);
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
