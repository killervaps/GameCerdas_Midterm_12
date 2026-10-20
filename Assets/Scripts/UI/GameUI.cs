using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Akar seluruh UI game: Canvas, EventSystem, dan semua layar (menu utama, HUD, preview musuh,
// modal gate, jeda, tutorial, hasil run). Semua isi dibangun dari kode saat Awake.
//
// Tidak perlu setup manual: jika scene punya RunManager tetapi belum ada GameUI, objek ini
// dibuat otomatis saat Play. Untuk mengubah pengaturan di Inspector, pakai menu
// MathGate/Setup/6. Game UI (scene terbuka) supaya objeknya tersimpan di scene.
//
// UI hanya mendengarkan event dari RunManager, GateSelectionRoom, BattleManager, dan PlayerStats.
// Logika game tidak dipindah ke sini.
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class GameUI : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private GateSelectionRoom gateRoom;
    [SerializeField] private BattleManager battle;
    [SerializeField] private EnemySpawner spawner;
    [SerializeField] private ThreatEstimator threatEstimator;
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerMotor playerMotor;

    [Header("Flow")]
    // Jika aktif, run tidak dimulai otomatis. Pemain menekan "Mulai" di menu utama.
    [SerializeField] private bool showMainMenu = true;
    // Panel OnGUI lama (RunStatusPanel) disembunyikan karena sudah digantikan HUD dan layar hasil.
    [SerializeField] private bool hideLegacyRunPanel = true;
    // CombatDebugPanel (OnGUI) menutupi panel kiri, jadi hanya tampil saat AI Debug Mode (F1) aktif.
    [SerializeField] private bool combatDebugFollowsF1 = true;
    // Jeda sebelum layar menang/kalah muncul, supaya momen terakhir pertempuran terlihat.
    [SerializeField] private float resultDelay = 1.2f;
    // Di menu utama kamera pindah ke close-up MC di Safezone (MenuCameraShowcase di kamera utama).
    [SerializeField] private bool menuCameraShowcase = true;
    // Health bar di atas kepala tiap musuh selama pertempuran.
    [SerializeField] private bool enemyHealthBars = true;
    [SerializeField] private string playerActionMap = "Player";

    [Header("Tutorial")]
    // Petunjuk singkat yang muncul sekali di momen pertama (Safezone, pertempuran, dst.).
    [SerializeField] private bool contextualHints = true;
    // Jika aktif, petunjuk yang sudah dilihat diingat antar sesi (PlayerPrefs). Matikan untuk demo.
    [SerializeField] private bool rememberHintsAcrossSessions = false;

    [Header("Text")]
    [SerializeField] private string gameTitle = "MATH GATE";
    [SerializeField] private string gameSubtitle = "Pilih gate. Hitung risikonya. Bertahan sampai ronde terakhir.";
    [SerializeField] private string creditLine = "Game Cerdas - Midterm Kelompok 12";

    private MainMenuScreen mainMenu;
    private EnemyHealthBars healthBars;
    private DamageFeedback damageFeedback;
    private RoundAnnouncement announcement;
    private MenuCameraShowcase showcase;
    private HudPanel hud;
    private EnemyPreviewPanel enemyPreview;
    private GateInfoModal gateModal;
    private HintToast hints;
    private PauseScreen pause;
    private TutorialScreen tutorial;
    private ResultScreen result;

    private InputActionMap playerMap;
    private bool gameplayInputAllowed = true;
    private float timeScaleBeforePause = 1f;
    private Coroutine pendingResult;
    private CombatDebugPanel combatDebugPanel;
    private bool started;

    public static GameUI Current { get; private set; }

    public RunManager Run => runManager;
    public HudPanel Hud => hud;
    public bool IsAnnouncing => announcement != null && announcement.IsPlaying;
    public GateSelectionRoom GateRoom => gateRoom;
    public BattleManager Battle => battle;
    public EnemySpawner Spawner => spawner;
    public ThreatEstimator Threat => threatEstimator;
    public PlayerStats Stats => playerStats;
    public PlayerHealth Health => playerHealth;

    public string GameTitle => gameTitle;
    public string GameSubtitle => gameSubtitle;
    public string CreditLine => creditLine;

    public bool IsMainMenuOpen => mainMenu != null && mainMenu.IsVisible;
    public bool IsPaused { get; private set; }
    public bool IsTutorialOpen => tutorial != null && tutorial.IsVisible;
    public bool IsResultOpen => result != null && result.IsVisible;

    public RunState State => runManager != null ? runManager.State : RunState.Idle;

    // ---------- Bootstrap otomatis ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureExists();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureExists();
    }

    private static void EnsureExists()
    {
        if (FindAnyObjectByType<GameUI>(FindObjectsInactive.Include) != null)
            return;

        if (FindAnyObjectByType<RunManager>() == null)
            return;

        new GameObject("GameUI").AddComponent<GameUI>();
    }

    // ---------- Lifecycle ----------

    private void Awake()
    {
        Current = this;

        ResolveReferences();

        // Harus sebelum RunManager.Start supaya run tidak langsung dimulai di belakang menu.
        if (showMainMenu && runManager != null)
            runManager.StartOnPlay = false;

        if (hideLegacyRunPanel)
        {
            RunStatusPanel legacy = FindAnyObjectByType<RunStatusPanel>();

            if (legacy != null)
                legacy.enabled = false;
        }

        if (combatDebugFollowsF1)
            combatDebugPanel = FindAnyObjectByType<CombatDebugPanel>();

        playerMap = InputSystem.actions != null ? InputSystem.actions.FindActionMap(playerActionMap) : null;

        EnsureEventSystem();
        BuildCanvas();

        if (menuCameraShowcase && showMainMenu)
            SetupShowcase();
    }

    private void OnEnable()
    {
        if (runManager != null)
        {
            runManager.StateChanged += HandleRunStateChanged;
            runManager.RoundStarted += HandleRoundStarted;
            runManager.RoundCleared += HandleRoundCleared;
        }

        if (gateRoom != null)
        {
            gateRoom.PortalFocused += HandlePortalFocused;
            gateRoom.PortalUnfocused += HandlePortalUnfocused;
            gateRoom.SelectionOpened += HandleSelectionOpened;
            gateRoom.GateChosen += HandleGateChosen;
        }

        if (playerStats != null)
            playerStats.StatsChanged += HandleStatsChanged;
    }

    private void OnDisable()
    {
        if (runManager != null)
        {
            runManager.StateChanged -= HandleRunStateChanged;
            runManager.RoundStarted -= HandleRoundStarted;
            runManager.RoundCleared -= HandleRoundCleared;
        }

        if (gateRoom != null)
        {
            gateRoom.PortalFocused -= HandlePortalFocused;
            gateRoom.PortalUnfocused -= HandlePortalUnfocused;
            gateRoom.SelectionOpened -= HandleSelectionOpened;
            gateRoom.GateChosen -= HandleGateChosen;
        }

        if (playerStats != null)
            playerStats.StatsChanged -= HandleStatsChanged;

        // Jangan tinggalkan game dalam keadaan beku atau tanpa input.
        if (IsPaused)
        {
            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = false;
            IsPaused = false;
        }

        if (playerMap != null && !playerMap.enabled)
            playerMap.Enable();
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }

    private void Start()
    {
        started = true;

        if (showMainMenu && runManager != null)
        {
            // Pengaman jika run sudah terlanjur dimulai sebelum GameUI sempat mematikan StartOnPlay.
            if (runManager.State != RunState.Idle)
                runManager.StopRun();

            ShowMainMenu();
        }
        else
        {
            RefreshPanels();
        }

        ApplyInputGate(true);
    }

    private void Update()
    {
        if (!started)
            return;

        if (CancelPressed())
            HandleCancel();

        ApplyInputGate(false);

        if (combatDebugPanel != null && combatDebugPanel.enabled != AIDebugSettings.Enabled)
            combatDebugPanel.enabled = AIDebugSettings.Enabled;
    }

    private static bool CancelPressed()
    {
        bool keyboard = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool gamepad = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
        return keyboard || gamepad;
    }

    private void HandleCancel()
    {
        if (IsTutorialOpen)
        {
            CloseTutorial();
            return;
        }

        if (IsMainMenuOpen || IsResultOpen)
            return;

        if (IsPaused)
        {
            Resume();
            return;
        }

        if (CanPause())
            Pause();
    }

    private bool CanPause()
    {
        RunState state = State;
        return state == RunState.Preparing || state == RunState.Battle || state == RunState.RoundCleared;
    }

    // ---------- Aksi dari tombol ----------

    public void StartGame()
    {
        if (showcase != null)
            showcase.End();

        mainMenu.Hide();
        result.Hide();
        CancelPendingResult();
        announcement.Stop();

        if (runManager != null)
            runManager.StartRun();

        RefreshPanels();
    }

    public void Pause()
    {
        if (IsPaused)
            return;

        IsPaused = true;
        timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        gateModal.Hide();
        pause.Show();
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        IsPaused = false;
        Time.timeScale = timeScaleBeforePause;
        AudioListener.pause = false;

        pause.Hide();
        tutorial.Hide();

        // Jika MC masih berdiri di depan gate, kartunya dibuka lagi.
        if (gateRoom != null && gateRoom.ActivePortal != null)
            HandlePortalFocused(gateRoom.ActivePortal);
    }

    public void RestartRun()
    {
        Resume();
        StartGame();
    }

    public void BackToMainMenu()
    {
        Resume();
        CancelPendingResult();
        announcement.Stop();

        if (runManager != null)
            runManager.StopRun();

        result.Hide();
        gateModal.Hide();
        hints.Hide();
        ShowMainMenu();
        RefreshPanels();
    }

    // MC dipindah ke Safezone lalu kamera beralih ke mode showcase.
    private void ShowMainMenu()
    {
        if (gateRoom != null)
            gateRoom.TeleportToSafezone();

        mainMenu.Show();

        if (showcase != null && playerMotor != null)
            showcase.Begin(playerMotor.transform, playerMotor);
    }

    private void SetupShowcase()
    {
        TopDownCameraFollow follow = FindAnyObjectByType<TopDownCameraFollow>();
        Camera cam = follow != null ? follow.GetComponent<Camera>() : Camera.main;

        if (cam == null)
            return;

        showcase = cam.GetComponent<MenuCameraShowcase>();

        if (showcase == null)
            showcase = cam.gameObject.AddComponent<MenuCameraShowcase>();
    }

    public void OpenTutorial()
    {
        tutorial.Open(0);
    }

    public void CloseTutorial()
    {
        tutorial.Hide();

        // Kembalikan fokus keyboard ke layar di bawahnya.
        if (IsPaused)
            pause.FocusFirst();
        else if (IsMainMenuOpen)
            mainMenu.FocusFirst();
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- Event game ----------

    private void HandleRunStateChanged(RunState state)
    {
        if (state != RunState.Preparing)
            gateModal.Hide();

        if (state == RunState.Victory || state == RunState.Defeat)
        {
            CancelPendingResult();
            pendingResult = StartCoroutine(ShowResultAfterDelay(state == RunState.Victory));
        }

        if (state == RunState.Preparing)
        {
            enemyPreview.Refresh();
            ShowHint("safezone", "Dekati salah satu gate untuk membaca efeknya. <b>Tahan [E]</b> untuk memilih.");
        }
        else if (state == RunState.Battle)
        {
            ShowHint("battle", "<b>WASD</b> bergerak  |  <b>Tahan Klik Kiri</b> menembak  |  <b>Tab</b> ganti target  |  <b>Space</b> Aura Burst");
        }
        else if (state == RunState.RoundCleared)
        {
            ShowHint("cleared", "Ronde bersih! HP pulih sebagian, lalu kamu kembali ke Safezone untuk memilih gate berikutnya.");
        }

        RefreshPanels();
    }

    // Pengumuman megah saat ronde bersih. Overlay lain disembunyikan selama pengumuman.
    private void HandleRoundCleared(int round)
    {
        if (runManager == null)
            return;

        bool final = round >= runManager.TotalRounds;
        float battleTime = battle != null ? Time.time - battle.BattleStartTime : 0f;
        string hp = playerHealth != null
            ? "HP tersisa " + Mathf.CeilToInt(playerHealth.CurrentHealth) + " / " + Mathf.CeilToInt(playerHealth.MaxHealth)
            : string.Empty;

        string kicker = final ? "SEMUA RONDE SELESAI" : "RONDE " + round + " / " + runManager.TotalRounds;
        string title = final ? "KEMENANGAN!" : "RONDE BERSIH!";
        string subtitle = "Musuh dikalahkan dalam <b>" + UIKit.FormatTime(battleTime) + "</b>" +
                          (hp.Length > 0 ? "   |   " + hp : string.Empty) +
                          (final ? string.Empty : "   |   kembali ke Safezone...");

        gateModal.Hide();
        hints.Hide();
        announcement.Play(kicker, title, subtitle, final ? UIKit.Accent : new Color(0.45f, 0.9f, 0.55f), final ? 3.4f : 2.8f);
        RefreshPanels();
    }

    private void HandleRoundStarted(int round)
    {
        hud.Flash("RONDE " + round, runManager != null && runManager.CurrentWave != null
            ? runManager.CurrentWave.Describe()
            : string.Empty);
    }

    private void HandlePortalFocused(GatePortal portal)
    {
        if (portal == null || !portal.IsOpen || State != RunState.Preparing || IsPaused || IsMainMenuOpen || IsAnnouncing)
            return;

        gateModal.Open(portal.Gate);
        enemyPreview.SetCompareGate(portal.Gate);

        ShowHint("gate", "<color=" + UIKit.BuffHex + ">Hijau</color> = buff, <color=" + UIKit.TradeOffHex +
                         ">merah</color> = trade-off. Perhatikan perubahan <b>Ancaman</b> di panel kiri.");
    }

    private void HandlePortalUnfocused(GatePortal portal)
    {
        gateModal.Hide();
        enemyPreview.SetCompareGate(null);
    }

    private void HandleSelectionOpened()
    {
        enemyPreview.SetCompareGate(null);
        enemyPreview.Refresh();
    }

    private void HandleGateChosen(MathGateData gate)
    {
        gateModal.Hide();
        enemyPreview.SetCompareGate(null);
        hud.RefreshBuild();
    }

    private void HandleStatsChanged()
    {
        enemyPreview.Refresh();
        hud.RefreshBuild();
    }

    private IEnumerator ShowResultAfterDelay(bool victory)
    {
        yield return new WaitForSecondsRealtime(resultDelay);

        // Tunggu pengumuman "KEMENANGAN!" selesai dulu.
        while (IsAnnouncing)
            yield return null;

        pendingResult = null;
        gateModal.Hide();
        hints.Hide();
        result.ShowResult(victory);
        RefreshPanels();
    }

    private void CancelPendingResult()
    {
        if (pendingResult != null)
        {
            StopCoroutine(pendingResult);
            pendingResult = null;
        }
    }

    private void ShowHint(string key, string message)
    {
        if (!contextualHints || IsMainMenuOpen || IsAnnouncing)
            return;

        hints.ShowOnce(key, message, rememberHintsAcrossSessions);
    }

    // ---------- Visibilitas & input ----------

    private void RefreshPanels()
    {
        RunState state = State;
        bool inGame = !IsMainMenuOpen && state != RunState.Idle;
        bool ended = state == RunState.Victory || state == RunState.Defeat;

        bool announcing = IsAnnouncing;

        hud.SetVisible(inGame && !IsResultOpen && !announcing);
        bool combat = inGame && !announcing &&
                      (state == RunState.Battle || state == RunState.RoundCleared || state == RunState.Defeat);
        healthBars.SetVisible(enemyHealthBars && combat);
        damageFeedback.SetVisible(combat);
        enemyPreview.SetVisible(inGame && state == RunState.Preparing);

        if (!inGame || ended)
            gateModal.Hide();
    }

    private void ApplyInputGate(bool force)
    {
        if (playerMap == null)
            return;

        bool allowed = !IsMainMenuOpen && !IsPaused && !IsTutorialOpen && !IsResultOpen;

        // Skrip gameplay meng-Enable action-nya sendiri di OnEnable, jadi saat input harus mati
        // map dicek ulang setiap frame.
        if (!allowed && playerMap.enabled)
            playerMap.Disable();
        else if (allowed && (force || !gameplayInputAllowed))
            playerMap.Enable();

        gameplayInputAllowed = allowed;
    }

    // ---------- Build ----------

    private void ResolveReferences()
    {
        if (runManager == null) runManager = FindAnyObjectByType<RunManager>();
        if (gateRoom == null) gateRoom = FindAnyObjectByType<GateSelectionRoom>();
        if (battle == null) battle = FindAnyObjectByType<BattleManager>();
        if (spawner == null) spawner = FindAnyObjectByType<EnemySpawner>();
        if (threatEstimator == null) threatEstimator = FindAnyObjectByType<ThreatEstimator>();
        if (playerStats == null) playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (playerMotor == null) playerMotor = FindAnyObjectByType<PlayerMotor>();

        if (runManager == null)
            Debug.LogWarning("GameUI: RunManager tidak ditemukan. Menu utama dan HUD ronde tidak akan berfungsi.", this);
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        go.transform.SetParent(null);
    }

    private void BuildCanvas()
    {
        gameObject.layer = 5;

        Canvas canvas = GetComponent<Canvas>();

        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = GetComponent<CanvasScaler>();

        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // Urutan = urutan gambar (yang belakang digambar di atas).
        healthBars = CreateScreen<EnemyHealthBars>("EnemyHealthBars", false);
        damageFeedback = CreateScreen<DamageFeedback>("DamageFeedback", false);
        hud = CreateScreen<HudPanel>("HUD", false);
        enemyPreview = CreateScreen<EnemyPreviewPanel>("EnemyPreview", false);
        gateModal = CreateScreen<GateInfoModal>("GateInfoModal", false);
        hints = CreateScreen<HintToast>("HintToast", false);
        announcement = CreateScreen<RoundAnnouncement>("RoundAnnouncement", false);
        announcement.Finished += RefreshPanels;
        result = CreateScreen<ResultScreen>("ResultScreen", true);
        pause = CreateScreen<PauseScreen>("PauseMenu", true);
        mainMenu = CreateScreen<MainMenuScreen>("MainMenu", true);
        tutorial = CreateScreen<TutorialScreen>("Tutorial", true);
    }

    private T CreateScreen<T>(string screenName, bool interactive) where T : UIScreen
    {
        RectTransform screenRect = UIKit.CreateRect(screenName, transform);
        UIKit.Stretch(screenRect);

        T screen = screenRect.gameObject.AddComponent<T>();
        screen.Initialize(this, interactive);
        return screen;
    }
}
