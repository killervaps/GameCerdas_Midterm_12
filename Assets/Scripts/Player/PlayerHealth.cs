using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    // Dipakai hanya jika objek tidak punya PlayerStats. Jika ada, MaxHP diambil dari PlayerStats.
    [SerializeField] private float maxHealth = 100f;

    [Header("Debug")]
    [SerializeField] private bool logDamage = false;

    private PlayerStats stats;

    public float CurrentHealth { get; private set; }

    public float MaxHealth { get; private set; }

    public bool IsDead =>
        CurrentHealth <= 0f;

    public event System.Action Died;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();

        MaxHealth = maxHealth;
        CurrentHealth = MaxHealth;
    }

    private void OnEnable()
    {
        if (stats != null)
            stats.StatsChanged += HandleStatsChanged;
    }

    private void OnDisable()
    {
        if (stats != null)
            stats.StatsChanged -= HandleStatsChanged;
    }

    private void Start()
    {
        if (stats == null)
            return;

        MaxHealth = stats.Get(StatType.MaxHP);
        CurrentHealth = MaxHealth;
    }

    // Saat MaxHP berubah karena gate, rasio HP saat ini dipertahankan.
    // TODO(desain): aturan heal antar ronde belum diputuskan (penuh, sebagian, atau tidak sama sekali).
    private void HandleStatsChanged()
    {
        float newMax = stats.Get(StatType.MaxHP);

        if (Mathf.Approximately(newMax, MaxHealth))
            return;

        float ratio = MaxHealth > 0f
            ? CurrentHealth / MaxHealth
            : 1f;

        MaxHealth = newMax;

        if (!IsDead)
            CurrentHealth = Mathf.Clamp(ratio * MaxHealth, 0f, MaxHealth);
    }

    public void TakeDamage(float damage)
    {
        if (IsDead)
            return;

        CurrentHealth -= damage;

        CurrentHealth = Mathf.Clamp(
            CurrentHealth,
            0f,
            MaxHealth
        );

        if (logDamage)
        {
            Debug.Log(
                "Player HP: " + CurrentHealth
            );
        }

        if (CurrentHealth <= 0f)
        {
            if (logDamage)
                Debug.Log("Player Dead");

            Died?.Invoke();
        }
    }

    // Untuk awal run baru atau heal penuh antar ronde.
    public void RestoreFull()
    {
        CurrentHealth = MaxHealth;
    }
}
