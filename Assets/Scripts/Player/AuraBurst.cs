using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Skill aktif. Damage area di sekitar MC dengan cooldown.
// Mengenai semua musuh dalam radius, counter alami untuk formasi Guardian + Shooter yang berdekatan.
[RequireComponent(typeof(PlayerStats))]
public class AuraBurst : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth health;

    [Header("Burst")]
    [SerializeField] private float radius = 3.5f;
    // Damage burst = Damage MC × nilai ini.
    [SerializeField] private float damageMultiplier = 2f;
    [SerializeField] private float cooldown = 8f;
    [SerializeField] private int maxColliders = 32;

    [Header("Input")]
    [SerializeField] private string actionName = "Player/AuraBurst";

    [Header("Visual (opsional)")]
    // Mis. efek dari Cartoon FX. Dibuat di posisi MC lalu dihancurkan setelah Visual Lifetime.
    [SerializeField] private GameObject burstEffectPrefab;
    [SerializeField] private float visualLifetime = 2f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logBurst = true;

    private readonly List<IDamageable> damagedThisBurst = new List<IDamageable>();

    private PlayerStats stats;
    private Collider[] hitBuffer;
    private InputAction burstAction;
    private int enemyMask;
    private float lastBurstTime = float.NegativeInfinity;

    public float CooldownRemaining { get; private set; }

    public float Cooldown => cooldown;

    public bool IsReady => CooldownRemaining <= 0f;

    // 0 baru dipakai, 1 siap.
    public float Readiness => cooldown > 0f ? 1f - CooldownRemaining / cooldown : 1f;

    public float Radius => radius;

    public float Damage => stats.Get(StatType.Damage) * damageMultiplier;

    // Jumlah musuh yang terkena.
    public event Action<int> Burst;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();

        if (health == null)
            health = GetComponent<PlayerHealth>();

        hitBuffer = new Collider[Mathf.Max(1, maxColliders)];
        enemyMask = GameLayers.EnemyMask;

        burstAction = InputSystem.actions != null
            ? InputSystem.actions.FindAction(actionName)
            : null;

        if (burstAction == null)
            Debug.LogError("AuraBurst: action '" + actionName + "' tidak ditemukan.", this);
    }

    private void OnEnable()
    {
        burstAction?.Enable();
    }

    private void Update()
    {
        if (CooldownRemaining > 0f)
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - Time.deltaTime);

        if (health != null && health.IsDead)
            return;

        if (burstAction != null && burstAction.WasPressedThisFrame() && IsReady)
            Activate();
    }

    public void Activate()
    {
        Vector3 center = transform.position;
        float damage = Damage;

        int count = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            hitBuffer,
            enemyMask,
            QueryTriggerInteraction.Ignore
        );

        damagedThisBurst.Clear();

        for (int i = 0; i < count; i++)
        {
            // Satu musuh bisa punya beberapa collider, jangan kena dua kali.
            IDamageable target = hitBuffer[i].GetComponentInParent<IDamageable>();

            if (target == null || target.IsDead || damagedThisBurst.Contains(target))
                continue;

            damagedThisBurst.Add(target);

            target.TakeDamage(new DamageInfo(damage, gameObject, target.AimPoint, DamageKind.Burst));
        }

        CooldownRemaining = cooldown;
        lastBurstTime = Time.time;

        if (burstEffectPrefab != null)
        {
            GameObject effect = Instantiate(burstEffectPrefab, center, Quaternion.identity);
            Destroy(effect, visualLifetime);
        }

        if (logBurst)
            Debug.Log("AuraBurst: kena " + damagedThisBurst.Count + " musuh, damage " + damage.ToString("0.#"), this);

        Burst?.Invoke(damagedThisBurst.Count);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        // Merah sebentar setelah burst, kuning saat siap, abu-abu saat cooldown.
        bool justBurst = Application.isPlaying && Time.time - lastBurstTime < 0.3f;

        Gizmos.color = justBurst
            ? Color.red
            : !Application.isPlaying || IsReady
                ? new Color(1f, 0.9f, 0.2f, 0.7f)
                : new Color(0.5f, 0.5f, 0.5f, 0.4f);

        Vector3 center = transform.position + Vector3.up * 0.05f;
        const int segments = 40;
        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
