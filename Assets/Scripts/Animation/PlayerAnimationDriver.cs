using UnityEngine;

// Menerjemahkan keadaan MC ke Animator. Pengganti PlayerAnimation dari praktikum.
//   Locomotion  kecepatan lokal relatif terhadap arah hadap (MoveX, MoveZ). MC sering menghadap target sambil bergerak,
//               jadi mundur dan menyamping memakai animasi lari mundur dan strafe.
//   Trigger     Cast setiap Bola Aura ditembakkan, Burst saat Aura Burst, Hit saat terkena damage.
//   Revive      Animator di-reset saat MC hidup lagi, supaya tidak tertahan di pose mati.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAnimationDriver : MonoBehaviour
{
    [Header("References")]
    // Kosongkan untuk mencari Animator di objek ini atau child-nya.
    [SerializeField] private Animator animator;

    [Header("Tuning")]
    // Penghalusan parameter gerak supaya blend arah tidak gelisah saat MC berputar menghadap target.
    [SerializeField] private float dampTime = 0.15f;
    [SerializeField] private float hitCooldown = 0.5f;

    private PlayerMotor motor;
    private PlayerHealth health;
    private PlayerStats stats;
    private AuraCaster caster;
    private AuraBurst burst;
    private float nextHitTime;
    private bool wasDead;

    private void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        health = GetComponent<PlayerHealth>();
        stats = GetComponent<PlayerStats>();
        caster = GetComponent<AuraCaster>();
        burst = GetComponent<AuraBurst>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null)
        {
            animator.applyRootMotion = false;

            // Gerak karakter terjadi di Update (NavMeshAgent / CharacterController). Animator yang diperbarui di FixedUpdate
            // (Animate Physics) membuat model tertinggal atau mendahului posisinya sendiri sehingga terlihat bergetar.
            if (animator.updateMode != AnimatorUpdateMode.Normal)
            {
                Debug.Log(name + ": Animator Update Mode diubah ke Normal supaya tidak bergetar.", this);
                animator.updateMode = AnimatorUpdateMode.Normal;
            }
        }
    }

    private void OnEnable()
    {
        if (caster != null)
            caster.CastStarted += HandleCastStarted;

        if (burst != null)
            burst.BurstStarted += HandleBurstStarted;

        if (health != null)
        {
            health.Damaged += HandleDamaged;
            health.Revived += ResetAnimator;
        }
    }

    private void OnDisable()
    {
        if (caster != null)
            caster.CastStarted -= HandleCastStarted;

        if (burst != null)
            burst.BurstStarted -= HandleBurstStarted;

        if (health != null)
        {
            health.Damaged -= HandleDamaged;
            health.Revived -= ResetAnimator;
        }
    }

    private void Update()
    {
        if (animator == null)
            return;

        bool dead = health != null && health.IsDead;

        // Pengaman jika HP dipulihkan tanpa lewat RestoreFull.
        if (wasDead && !dead)
            ResetAnimator();

        wasDead = dead;
        animator.SetBool(AnimParams.IsDeadHash, dead);

        if (dead)
            return;

        Vector3 velocity = motor.Velocity;
        velocity.y = 0f;

        // Dinormalisasi terhadap MoveSpeed build saat ini, jadi lari penuh = 1 walau gate mengubah kecepatan.
        // Saat menembak (×0,6) nilainya turun ke antara ring jalan dan lari.
        float baseSpeed = Mathf.Max(0.01f, stats != null ? stats.Get(StatType.MoveSpeed) : motor.MoveSpeed);

        float forward = Vector3.Dot(velocity, transform.forward) / baseSpeed;
        float right = Vector3.Dot(velocity, transform.right) / baseSpeed;

        animator.SetFloat(AnimParams.ForwardHash, forward, dampTime, Time.deltaTime);
        animator.SetFloat(AnimParams.MoveZHash, forward, dampTime, Time.deltaTime);
        animator.SetFloat(AnimParams.MoveXHash, right, dampTime, Time.deltaTime);
        animator.SetFloat(AnimParams.SpeedHash, velocity.magnitude / baseSpeed, dampTime, Time.deltaTime);
    }

    // Kembali ke pose awal: semua state, parameter, dan trigger di-reset.
    private void ResetAnimator()
    {
        if (animator == null)
            return;

        wasDead = false;

        animator.Rebind();
        animator.Update(0f);
    }

    private void HandleCastStarted(EnemyHealth target)
    {
        if (animator != null)
            animator.SetTrigger(AnimParams.CastHash);
    }

    private void HandleBurstStarted()
    {
        if (animator != null)
            animator.SetTrigger(AnimParams.BurstHash);
    }

    private void HandleDamaged(float amount, DamageInfo info)
    {
        if (animator == null || Time.time < nextHitTime || (health != null && health.IsDead))
            return;

        nextHitTime = Time.time + hitCooldown;
        animator.SetTrigger(AnimParams.HitHash);
    }
}
