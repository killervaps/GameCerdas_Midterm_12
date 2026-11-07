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

// Medan pertempuran tiap ronde. Arena = salah satu dari 4 arena buatan tangan, Dungeon = dungeon BSP (Praktikum 10).
public enum GameMode
{
    Arena,
    Dungeon
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

    [Header("Mode Dungeon (Praktikum 10)")]
    // Kosong = dicari otomatis. Tanpa DungeonGenerator di scene, Mode Dungeon tidak bisa dipilih.
    [SerializeField] private DungeonGenerator dungeon;
    // Arena buatan tangan dinonaktifkan selama Mode Dungeon dan dinyalakan lagi saat kembali ke Mode Arena atau menu.
    [SerializeField] private bool hideArenasInDungeon = true;
    // Dungeon jauh lebih luas dari arena, jadi jumlah musuh, budget, dan batas Max per role dikali nilai ini.
    [SerializeField, Min(1f)] private float dungeonEnemyMultiplier = 2f;

    [Header("Rounds (PCG)")]
    // Satu profil per ronde, urut dari ronde 1. Jumlah profil = jumlah ronde.
    [SerializeField] private EnemyRoundProfile[] rounds = EnemyRoundProfile.Defaults();
    // Challenge 4: biaya tiap role untuk Difficulty Budget. Hanya dipakai profil ronde yang Difficulty Budget-nya > 0.
    [SerializeField] private RoleCost[] roleCosts = RoleCost.Defaults();

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
    private Arena dungeonArena;
    private DungeonGoalPortal openPortal;

    public RunState State { get; private set; } = RunState.Idle;

    // Dimulai dari 1.
    public int CurrentRound { get; private set; }

    public int TotalRounds => rounds != null ? rounds.Length : 0;

    // Wave ronde saat ini hasil PCG. Saat Preparing, ini wave yang akan dihadapi setelah memilih gate.
    public WaveData CurrentWave => currentWave;

    public IReadOnlyList<RoleCost> RoleCosts => roleCosts;

    // Difficulty Budget ronde ini (0 = mati) dan total biaya musuh hasil PCG.
    public int CurrentBudget { get; private set; }

    public int CurrentBudgetSpent { get; private set; }

    public EnemyRoundProfile CurrentProfile =>
        rounds != null && CurrentRound >= 1 && CurrentRound <= rounds.Length ? rounds[CurrentRound - 1] : null;

    // Dipilih dari menu utama sebelum run dimulai. Retry memakai mode yang sama.
    public GameMode Mode { get; private set; } = GameMode.Arena;

    public bool DungeonAvailable => dungeon != null && dungeon.GetComponent<Arena>() != null;

    public DungeonGenerator Dungeon => dungeon;

    // Portal Goal yang sedang terbuka (Mode Dungeon, ronde bersih). Dipakai penanda arah di HUD.
    public DungeonGoalPortal OpenPortal => openPortal;

    // Arena ronde saat ini (di Mode Dungeon: dungeon ronde ini). Null jika tidak memakai ArenaSelector.
    public Arena CurrentArena => Mode == GameMode.Dungeon ? dungeonArena : arenaSelector != null ? arenaSelector.Current : null;

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

    // Arena dan komposisi ronde berikutnya selesai dibuat (masuk Safezone atau regenerate dari panel debug PCG).
    public event Action<int> RoundPrepared;

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

        if (dungeon == null)
            dungeon = FindAnyObjectByType<DungeonGenerator>(FindObjectsInactive.Include);
    }

    // Dipanggil menu utama sebelum StartRun. Mode Dungeon tanpa DungeonGenerator jatuh kembali ke Mode Arena.
    public void SetMode(GameMode mode)
    {
        if (mode == GameMode.Dungeon && !DungeonAvailable)
        {
            Debug.LogWarning("RunManager: Mode Dungeon butuh DungeonGenerator dengan komponen Arena (MathGate > PCG > 4). Memakai Mode Arena.", this);
            mode = GameMode.Arena;
        }

        Mode = mode;
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

        ApplyModeScene();

        PcgDirector.BeginNewRun();
        RunStarted?.Invoke();

        // Wave diberikan dulu supaya kartu gate bisa menghitung ancaman.
        PrepareRound();

        // Build kembali ke base dulu, baru HP dipulihkan supaya penuh terhadap MaxHP dasar.
        gateRoom.ResetRun();
        playerHealth.RestoreFull();

        Log("run baru, " + TotalRounds + " ronde, mode " + Mode);

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

        // Menu utama menampilkan dunia seperti Mode Arena.
        ShowArenas(true);

        if (dungeon != null)
            dungeon.ClearDungeon();

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

        // Mode Dungeon: portal Goal terbuka, MC berjalan ke sana lalu menahan E untuk kembali ke Safezone.
        if (Mode == GameMode.Dungeon && dungeon != null && dungeon.GoalPortal != null)
        {
            Log("ronde " + CurrentRound + " bersih, portal Goal terbuka");

            SetState(RunState.RoundCleared);

            openPortal = dungeon.GoalPortal;
            openPortal.Entered += HandlePortalEntered;
            openPortal.Open();
            return;
        }

        Log("ronde " + CurrentRound + " bersih, kembali ke Safezone dalam " + returnDelay.ToString("0.#") + " s");

        SetState(RunState.RoundCleared);
        pendingReturn = StartCoroutine(ReturnAfterDelay());
    }

    private void HandlePortalEntered(DungeonGoalPortal portal)
    {
        portal.Entered -= HandlePortalEntered;

        if (State != RunState.RoundCleared || portal != openPortal)
            return;

        Log("MC keluar lewat portal Goal");

        ReturnToSafezone();
    }

    private IEnumerator ReturnAfterDelay()
    {
        yield return new WaitForSeconds(returnDelay);

        pendingReturn = null;

        ReturnToSafezone();
    }

    private void ReturnToSafezone()
    {
        ClosePortal();
        ApplyHealBetweenRounds();

        CurrentRound++;
        spawner.ClearWave();
        PrepareRound();
        gateRoom.ReturnToSafezone();

        SetState(RunState.Preparing);
    }

    // Bagian PCG yang dibuat di Safezone: arena, lalu komposisi musuh. Posisi musuh baru dibuat saat ronde mulai.
    private void PrepareRound(bool repick = false)
    {
        PrepareArena(repick);
        GenerateWave();
        gateRoom.SetUpcomingWave(CurrentWave);

        RoundPrepared?.Invoke(CurrentRound);
    }

    // Debug PCG: membuat ulang ronde ini dari seed run yang sedang dipakai.
    //   Di Safezone: arena dan komposisi dipilih ulang, kartu gate dihitung ulang.
    //   Saat bertarung: komposisi tetap, musuh dan item dimunculkan ulang di posisi baru (HP musuh kembali penuh).
    public bool CanRegenerate => State == RunState.Preparing || State == RunState.Battle;

    public void RegenerateRound()
    {
        if (!Application.isPlaying)
            return;

        if (State == RunState.Preparing)
        {
            PrepareRound(true);
            gateRoom.RefreshOfferCards();
            Log("ronde " + CurrentRound + " dibuat ulang (Safezone)");
        }
        else if (State == RunState.Battle)
        {
            battle.BeginBattle(spawner.SpawnWave(CurrentWave, CurrentRound));
            Log("ronde " + CurrentRound + " dibuat ulang (posisi musuh dan item)");
        }
    }

    // Memilih arena ronde berikutnya, lalu mengarahkan teleport MC dan spawner musuh ke arena itu.
    private void PrepareArena(bool repick = false)
    {
        if (Mode == GameMode.Dungeon)
        {
            PrepareDungeon();
            return;
        }

        if (arenaSelector == null)
            return;

        SeededRandom random = PcgDirector.Stream(CurrentRound, PcgStream.Arena);
        Arena arena = repick ? arenaSelector.Repick(random) : arenaSelector.PickNext(random);

        if (arena == null)
            return;

        spawner.UseArena(arena);
        gateRoom.SetArenaSpawn(arena.PlayerEntry);

        Log("ronde " + CurrentRound + " di arena " + arena.DisplayName);
    }

    // Mode Dungeon: dungeon ronde ini dibangun dari seed run (aliran Dungeon), jadi seed sama = dungeon sama.
    // Dungeon dipakai seperti arena biasa: titik masuk MC di Start, area spawn PCG seukuran grid, NavMesh di-bake saat dibangun.
    private void PrepareDungeon()
    {
        SeededRandom random = PcgDirector.Stream(CurrentRound, PcgStream.Dungeon);
        dungeon.Generate(random.Seed);

        dungeonArena = dungeon.GetComponent<Arena>();

        if (!dungeon.HasLayout || dungeonArena == null || dungeonArena.PlayerEntry == null)
        {
            Debug.LogError("RunManager: dungeon ronde " + CurrentRound + " gagal dibuat.", dungeon);
            return;
        }

        spawner.UseArena(dungeonArena);
        gateRoom.SetArenaSpawn(dungeonArena.PlayerEntry);

        Log("ronde " + CurrentRound + " di dungeon seed " + random.Seed + ", " + dungeon.Rooms.Count + " room");
    }

    // Arena buatan tangan disembunyikan selama Mode Dungeon, dungeon dihapus selama Mode Arena.
    private void ApplyModeScene()
    {
        bool dungeonMode = Mode == GameMode.Dungeon;

        ShowArenas(!dungeonMode || !hideArenasInDungeon);

        if (!dungeonMode && dungeon != null)
            dungeon.ClearDungeon();
    }

    private void ShowArenas(bool visible)
    {
        if (arenaSelector == null || arenaSelector.Arenas == null)
            return;

        foreach (Arena arena in arenaSelector.Arenas)
        {
            if (arena != null && arena.gameObject.activeSelf != visible)
                arena.gameObject.SetActive(visible);
        }
    }

    private void GenerateWave()
    {
        if (currentWave != null)
            Destroy(currentWave);

        EnemyRoundProfile profile = CurrentProfile;

        if (profile != null && Mode == GameMode.Dungeon && dungeonEnemyMultiplier > 1f)
            profile = profile.Scaled(dungeonEnemyMultiplier);

        SeededRandom random = PcgDirector.Stream(CurrentRound, PcgStream.EnemyComposition);
        // Arena sudah dipilih lebih dulu, jadi tema arena bisa ikut menentukan komposisi.
        PcgSpawnArea area = spawner.CurrentArea;
        IReadOnlyList<ArenaRoleBias> bias = area != null ? area.RoleBias : null;
        WaveEntry[] entries = PcgEnemyComposition.Generate(profile, random, bias, roleCosts, out int spent);
        string label = profile != null && !string.IsNullOrEmpty(profile.label) ? profile.label : "Ronde " + CurrentRound;

        CurrentBudget = profile != null ? profile.difficultyBudget : 0;
        CurrentBudgetSpent = spent;

        currentWave = WaveData.CreateRuntime(label, entries);

        string budgetText = CurrentBudget > 0 ? ", budget " + spent + "/" + CurrentBudget : ", biaya " + spent;

        Log("komposisi " + label + " (PCG" + (bias != null && bias.Count > 0 ? ", bias " + area.name : "") + budgetText + "): " + currentWave.Describe());
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

    private void ClosePortal()
    {
        if (openPortal != null)
            openPortal.Entered -= HandlePortalEntered;

        openPortal = null;
    }

    private void CancelPendingReturn()
    {
        ClosePortal();

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
