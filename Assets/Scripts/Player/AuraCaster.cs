using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum AuraFireMode
{
    // Menembak sendiri selama ada target.
    Auto,
    // Menembak hanya saat tombol Attack ditahan, dan MC melambat selama menembak.
    Hold
}

// Serangan utama MC. Menembak Bola Aura ke target dari TargetSelector setiap 1 / AttackRate detik.
// Mode Hold memberi pilihan ke pemain antara menembak (lambat) atau fokus bergerak (cepat).
[RequireComponent(typeof(PlayerStats))]
public class AuraCaster : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TargetSelector targetSelector;
    [SerializeField] private PlayerMotor motor;
    [SerializeField] private PlayerHealth health;
    [SerializeField] private ProjectilePool projectilePool;
    // Titik keluar proyektil. Kosongkan untuk memakai Muzzle Offset.
    [SerializeField] private Transform muzzle;

    [Header("Fire Mode")]
    [SerializeField] private AuraFireMode fireMode = AuraFireMode.Hold;
    [SerializeField] private string attackActionName = "Player/Attack";
    // Pengali MoveSpeed selama menembak di mode Hold. 1 berarti tanpa penalti.
    [SerializeField, Range(0.1f, 1f)] private float moveSpeedWhileFiring = 0.6f;

    [Header("Muzzle")]
    [SerializeField] private Vector3 muzzleOffset = new Vector3(0f, 1f, 0.6f);

    [Header("Debug")]
    [SerializeField] private bool logShots = false;

    private PlayerStats stats;
    private InputAction attackAction;
    private float timer;

    public AuraFireMode FireMode => fireMode;

    public float Damage => stats.Get(StatType.Damage);

    public float AttackRate => stats.Get(StatType.AttackRate);

    public float Interval => 1f / Mathf.Max(0.01f, AttackRate);

    // 0 baru menembak, 1 siap menembak.
    public float Readiness => Mathf.Clamp01(timer / Interval);

    // True selama MC sedang dalam kondisi menembak (mode Auto: ada target; mode Hold: tombol ditahan dan ada target).
    public bool IsFiring { get; private set; }

    public event Action<EnemyHealth> Fired;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();

        if (targetSelector == null)
            targetSelector = GetComponent<TargetSelector>();

        if (motor == null)
            motor = GetComponent<PlayerMotor>();

        if (health == null)
            health = GetComponent<PlayerHealth>();

        if (projectilePool == null)
            Debug.LogError("AuraCaster: Projectile Pool belum diisi.", this);

        attackAction = InputSystem.actions != null
            ? InputSystem.actions.FindAction(attackActionName)
            : null;

        if (attackAction == null && fireMode == AuraFireMode.Hold)
            Debug.LogError("AuraCaster: action '" + attackActionName + "' tidak ditemukan.", this);
    }

    private void OnEnable()
    {
        attackAction?.Enable();
    }

    private void OnDisable()
    {
        SetFiring(false);
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            SetFiring(false);
            return;
        }

        float interval = Interval;

        // Timer ditahan di batas interval, jadi tembakan langsung keluar begitu mulai menembak.
        timer = Mathf.Min(timer + Time.deltaTime, interval);

        EnemyHealth target = targetSelector != null ? targetSelector.CurrentTarget : null;
        bool hasTarget = target != null && !target.IsDead;

        bool wantsToFire = fireMode == AuraFireMode.Auto ||
                           (attackAction != null && attackAction.IsPressed());

        // Penalti hanya berlaku saat benar-benar ada yang ditembak, menahan tombol tanpa target tidak memperlambat.
        SetFiring(wantsToFire && hasTarget);

        if (!IsFiring || timer < interval)
            return;

        Fire(target);
        timer = 0f;
    }

    private void SetFiring(bool firing)
    {
        IsFiring = firing;

        if (motor == null)
            return;

        bool slowed = firing && fireMode == AuraFireMode.Hold;
        motor.SetSpeedMultiplier(slowed ? moveSpeedWhileFiring : 1f);
    }

    private void Fire(EnemyHealth target)
    {
        if (projectilePool == null)
            return;

        Projectile projectile = projectilePool.Spawn();

        if (projectile == null)
            return;

        Vector3 origin = muzzle != null
            ? muzzle.position
            : transform.TransformPoint(muzzleOffset);

        projectile.Launch(
            projectilePool,
            origin,
            target.AimPoint - origin,
            target,
            Damage,
            gameObject
        );

        if (logShots)
            Debug.Log("AuraCaster: tembak " + target.name + " damage " + Damage.ToString("0.#"), this);

        Fired?.Invoke(target);
    }
}
