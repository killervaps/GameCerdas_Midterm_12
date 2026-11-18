using UnityEngine;

// Merekap performa MC selama satu ronde untuk DDA: waktu, kill, damage, heal, dan berapa kali hampir mati.
// Rekap ditutup saat ronde bersih (DifficultyModel.Evaluate) atau saat MC mati (DifficultyModel.RegisterDeath).
public class PlayerPerformanceTracker : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private BattleManager battle;
    [SerializeField] private PlayerHealth playerHealth;

    private WavePerformance current;
    private float startTime;

    public bool Recording { get; private set; }

    // Rekap ronde yang sedang berjalan. Skornya belum dihitung.
    public WavePerformance Live => current;

    private float NearDeathThreshold =>
        DifficultyModel.Current != null ? DifficultyModel.Current.Profile.nearDeathThreshold : 0.15f;

    private void Awake()
    {
        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();

        if (battle == null)
            battle = FindAnyObjectByType<BattleManager>();

        if (playerHealth == null)
            playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (runManager == null || battle == null || playerHealth == null)
            Debug.LogError("PlayerPerformanceTracker: RunManager, BattleManager, atau PlayerHealth tidak ditemukan.", this);
    }

    private void OnEnable()
    {
        if (runManager != null)
        {
            runManager.RunStarted += HandleRunStarted;
            runManager.RoundStarted += HandleRoundStarted;
            runManager.RoundCleared += HandleRoundCleared;
            runManager.RunLost += HandleRunLost;
            runManager.RunWon += HandleRunWon;
            runManager.StateChanged += HandleStateChanged;
        }

        if (battle != null)
            battle.EnemyDied += HandleEnemyDied;

        if (playerHealth != null)
        {
            playerHealth.Damaged += HandleDamaged;
            playerHealth.Healed += HandleHealed;
        }
    }

    private void OnDisable()
    {
        if (runManager != null)
        {
            runManager.RunStarted -= HandleRunStarted;
            runManager.RoundStarted -= HandleRoundStarted;
            runManager.RoundCleared -= HandleRoundCleared;
            runManager.RunLost -= HandleRunLost;
            runManager.RunWon -= HandleRunWon;
            runManager.StateChanged -= HandleStateChanged;
        }

        if (battle != null)
            battle.EnemyDied -= HandleEnemyDied;

        if (playerHealth != null)
        {
            playerHealth.Damaged -= HandleDamaged;
            playerHealth.Healed -= HandleHealed;
        }
    }

    private void Update()
    {
        if (!Recording)
            return;

        current.duration = Time.time - startTime;

        // RoundStarted dipanggil sebelum musuh dimunculkan, jadi jumlah musuh dibaca setelah pertempuran berjalan.
        if (battle != null)
            current.enemyCount = Mathf.Max(current.enemyCount, battle.TotalCount);
    }

    private void HandleRunStarted()
    {
        Recording = false;
    }

    private void HandleStateChanged(RunState state)
    {
        if (state == RunState.Idle)
            Recording = false;
    }

    private void HandleRoundStarted(int round)
    {
        current = new WavePerformance
        {
            round = round,
            maxHealth = playerHealth != null ? playerHealth.MaxHealth : 0f,
            hpStartRatio = HealthRatio()
        };

        startTime = Time.time;
        Recording = true;
    }

    private void HandleDamaged(float amount, DamageInfo info)
    {
        if (!Recording || playerHealth == null)
            return;

        current.damageTaken += amount;

        float threshold = playerHealth.MaxHealth * NearDeathThreshold;
        float after = playerHealth.CurrentHealth;
        float before = after + amount;

        if (before >= threshold && after < threshold)
            current.nearDeathCount++;
    }

    // Health orb adalah satu-satunya heal saat bertarung.
    private void HandleHealed(float amount)
    {
        if (Recording && runManager != null && runManager.State == RunState.Battle)
            current.healEvents++;
    }

    private void HandleEnemyDied(EnemyHealth enemy)
    {
        if (Recording)
            current.kills++;
    }

    private void HandleRoundCleared(int round)
    {
        if (!Recording)
            return;

        Close(false);

        if (DifficultyModel.Current != null)
            DifficultyModel.Current.Evaluate(current);
    }

    private void HandleRunLost()
    {
        if (!Recording)
            return;

        Close(true);

        if (DifficultyModel.Current != null)
            DifficultyModel.Current.RegisterDeath(current);
    }

    private void HandleRunWon()
    {
        if (DifficultyModel.Current != null)
            DifficultyModel.Current.RegisterVictory();
    }

    private void Close(bool died)
    {
        Recording = false;

        current.duration = Time.time - startTime;
        current.enemyCount = Mathf.Max(current.enemyCount, current.kills);
        current.died = died;
        current.hpEndRatio = died ? 0f : HealthRatio();

        if (playerHealth != null)
            current.maxHealth = playerHealth.MaxHealth;
    }

    private float HealthRatio()
    {
        return playerHealth != null && playerHealth.MaxHealth > 0f ? playerHealth.CurrentHealth / playerHealth.MaxHealth : 0f;
    }
}
