using System;
using UnityEngine;

// Menyimpan HP saat ini. MaxHP dan Defense selalu dibaca dari PlayerStats, tidak disimpan di sini.
[RequireComponent(typeof(PlayerStats))]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Debug")]
    [SerializeField] private bool logDamage = false;

    private PlayerStats stats;
    private CharacterController controller;

    public float CurrentHealth { get; private set; }

    // Salinan MaxHP terakhir, dibutuhkan untuk menjaga rasio HP saat MaxHP berubah.
    public float MaxHealth { get; private set; }

    public float Defense => stats.Get(StatType.Defense);

    public bool IsDead =>
        CurrentHealth <= 0f;

    public Vector3 AimPoint => controller != null
        ? transform.TransformPoint(controller.center)
        : transform.position + Vector3.up;

    // MC yang sedang aktif di scene. Dibaca perception musuh supaya tidak perlu mencari objek setiap tick.
    public static PlayerHealth Current { get; private set; }

    public event Action Died;

    // Damage akhir setelah Defense beserta paket aslinya.
    public event Action<float, DamageInfo> Damaged;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();
        controller = GetComponent<CharacterController>();

        MaxHealth = stats.Get(StatType.MaxHP);
        CurrentHealth = MaxHealth;
    }

    private void OnEnable()
    {
        Current = this;
        stats.StatsChanged += HandleStatsChanged;
    }

    private void OnDisable()
    {
        if (Current == this)
            Current = null;

        stats.StatsChanged -= HandleStatsChanged;
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

    // Dipertahankan untuk script musuh praktikum yang memberi damage langsung.
    public void TakeDamage(float damage)
    {
        TakeDamage(new DamageInfo(damage));
    }

    public float TakeDamage(DamageInfo info)
    {
        if (IsDead)
            return 0f;

        float finalDamage = DamageCalculator.Apply(info.rawAmount, Defense);

        CurrentHealth = Mathf.Clamp(
            CurrentHealth - finalDamage,
            0f,
            MaxHealth
        );

        if (logDamage)
        {
            Debug.Log(
                "Player kena " + info.kind + " " + info.rawAmount.ToString("0.#") +
                " -> " + finalDamage.ToString("0.#") + " (Def " + Defense.ToString("0") + "), HP " +
                CurrentHealth.ToString("0.#"),
                this
            );
        }

        Damaged?.Invoke(finalDamage, info);

        if (CurrentHealth <= 0f)
        {
            if (logDamage)
                Debug.Log("Player Dead", this);

            Died?.Invoke();
        }

        return finalDamage;
    }

    // Untuk awal run baru atau heal penuh antar ronde. Juga menghidupkan kembali MC yang sudah mati.
    public void RestoreFull()
    {
        CurrentHealth = MaxHealth;
    }

    // Uji sementara selama musuh belum bisa menyerang. Klik titik tiga di komponen saat Play.
    // Damage melewati rumus Defense, jadi 25 mentah dengan Defense 10 menjadi sekitar 22,7.
    [ContextMenu("Debug/Test Damage 25")]
    private void DebugDamage25()
    {
        if (Application.isPlaying)
            TakeDamage(new DamageInfo(25f, null, AimPoint, DamageKind.Generic));
    }

    // Langsung ke HP 0 tanpa Defense, untuk menguji alur kalah.
    [ContextMenu("Debug/Kill")]
    private void DebugKill()
    {
        if (!Application.isPlaying || IsDead)
            return;

        CurrentHealth = 0f;

        if (logDamage)
            Debug.Log("Player Dead (debug)", this);

        Died?.Invoke();
    }

    // Heal sebagian. Tidak berlaku untuk MC yang sudah mati.
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        CurrentHealth = Mathf.Min(CurrentHealth + amount, MaxHealth);
    }
}
