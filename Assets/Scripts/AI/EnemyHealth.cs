using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    // Daftar enemy yang sedang aktif, dipakai player untuk memilih target terdekat.
    // Enemy yang di-SetActive(false) otomatis tidak ikut.
    private static readonly List<EnemyHealth> active = new List<EnemyHealth>();

    public static IReadOnlyList<EnemyHealth> Active => active;

    // Untuk sistem yang mendengar semua musuh sekaligus (BattleManager, TacticalCoordinator).
    public static event Action<EnemyHealth> AnyDied;

    [SerializeField] private float maxHealth = 100f;
    // Musuh boleh punya Defense, rumusnya sama dengan MC.
    [SerializeField] private float defense = 0f;

    [Header("Death")]
    // Musuh dimatikan (SetActive false) setelah jeda ini. Isi negatif jika ingin tetap tampil, mis. untuk animasi mati.
    [SerializeField] private float disableDelay = 1.5f;
    // Mayat tidak menghalangi proyektil dan tidak lagi terkena burst.
    [SerializeField] private bool disableColliderOnDeath = true;

    [Header("Debug")]
    [SerializeField] private bool logDamage = true;

    private Collider bodyCollider;

    public float CurrentHealth { get; private set; }

    public float MaxHealth => maxHealth;

    public float Defense => defense;

    public bool IsDead =>
        CurrentHealth <= 0f;

    public float HealthRatio => maxHealth > 0f ? CurrentHealth / maxHealth : 0f;

    public Vector3 AimPoint => bodyCollider != null && bodyCollider.enabled
        ? bodyCollider.bounds.center
        : transform.position + Vector3.up;

    // Damage akhir setelah Defense beserta paket aslinya.
    public event Action<float, DamageInfo> Damaged;

    public event Action<EnemyHealth> Died;

    private void Awake()
    {
        bodyCollider = GetComponentInChildren<Collider>();

        CurrentHealth = maxHealth;

        WarnIfWrongLayer();
    }

    private void OnEnable()
    {
        active.Add(this);
    }

    private void OnDisable()
    {
        active.Remove(this);
    }

    // Dipanggil spawner atau EnemyRoleData untuk mengatur HP per role.
    public void SetMaxHealth(float value, bool refill = true)
    {
        maxHealth = Mathf.Max(1f, value);

        if (refill)
            CurrentHealth = maxHealth;
    }

    public void SetDefense(float value)
    {
        defense = Mathf.Max(0f, value);
    }

    // Dipertahankan untuk script praktikum yang memberi damage langsung.
    public void TakeDamage(float damage)
    {
        TakeDamage(new DamageInfo(damage));
    }

    public float TakeDamage(DamageInfo info)
    {
        if (IsDead)
            return 0f;

        float finalDamage = DamageCalculator.Apply(info.rawAmount, defense);

        CurrentHealth = Mathf.Clamp(
            CurrentHealth - finalDamage,
            0f,
            maxHealth
        );

        if (logDamage)
        {
            Debug.Log(
                name + " kena " + info.kind + " " + finalDamage.ToString("0.#") +
                ", HP " + CurrentHealth.ToString("0.#") + "/" + maxHealth.ToString("0"),
                this
            );
        }

        Damaged?.Invoke(finalDamage, info);

        if (CurrentHealth <= 0f)
            Die();

        return finalDamage;
    }

    private void Die()
    {
        if (logDamage)
            Debug.Log(name + " mati", this);

        if (disableColliderOnDeath && bodyCollider != null)
            bodyCollider.enabled = false;

        Died?.Invoke(this);
        AnyDied?.Invoke(this);

        if (disableDelay >= 0f)
            Invoke(nameof(DisableSelf), disableDelay);
    }

    private void DisableSelf()
    {
        gameObject.SetActive(false);
    }

    // Aura Burst dan nanti perception musuh memakai layer mask Enemy. Musuh di layer lain tidak akan terdeteksi.
    private void WarnIfWrongLayer()
    {
        int enemyLayer = LayerMask.NameToLayer(GameLayers.Enemy);

        if (enemyLayer < 0)
            return;

        if (gameObject.layer != enemyLayer)
        {
            Debug.LogWarning(
                name + ": layer objek '" + LayerMask.LayerToName(gameObject.layer) +
                "', seharusnya 'Enemy' (beserta child). Aura Burst tidak akan mengenai musuh ini.",
                this
            );
        }
        else if (bodyCollider != null && bodyCollider.gameObject.layer != enemyLayer)
        {
            Debug.LogWarning(name + ": collider " + bodyCollider.name + " tidak berada di layer 'Enemy'.", this);
        }
    }

    [ContextMenu("Test Damage 25")]
    private void TestDamage25()
    {
        TakeDamage(25f);
    }

    [ContextMenu("Reset Health")]
    private void ResetHealth()
    {
        CancelInvoke(nameof(DisableSelf));
        CurrentHealth = maxHealth;

        if (bodyCollider != null)
            bodyCollider.enabled = true;
    }
}
