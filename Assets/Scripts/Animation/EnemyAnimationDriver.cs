using UnityEngine;

// Menerjemahkan keadaan AI musuh ke Animator. Pengganti EnemyAnimation dari praktikum.
//   Locomotion  dari kecepatan nyata NavMeshAgent relatif terhadap arah hadap (MoveX, MoveZ), jadi
//               Idle, Guard, Chase, Search, Return, Flee, Enraged, Kite (mundur), dan Reposition (menyamping)
//               tidak perlu state terpisah.
//   Scanning    Looking Around saat menoleh di fase scan Search.
//   Special     Windup, Lunge, Recover dari aksi Charger.
//   Trigger     Attack saat brain menyerang (tembak, pukul), Hit saat terkena damage, IsDead saat mati.
[RequireComponent(typeof(EnemyController))]
public class EnemyAnimationDriver : MonoBehaviour
{
    [Header("References")]
    // Kosongkan untuk mencari Animator di objek ini atau child-nya.
    [SerializeField] private Animator animator;

    [Header("Tuning")]
    // Penghalusan parameter gerak. Kecepatan NavMeshAgent naik-turun sedikit setiap repath, damping meredamnya.
    [SerializeField] private float dampTime = 0.2f;
    // Jeda minimal antar animasi kena hit, supaya tembakan beruntun tidak membuat model kaku.
    [SerializeField] private float hitCooldown = 0.4f;
    // Animasi lari penuh diputar saat kecepatan mencapai porsi ini dari Move Speed role.
    // Musuh yang sering bergerak jarak pendek (Shooter kiting) jarang mencapai kecepatan penuh, jadi nilai kecil
    // (mis. 0,6) membuatnya tetap memakai animasi lari. Guardian sebaiknya tetap 1 karena memakai set jalan.
    [SerializeField, Range(0.3f, 1f)] private float fullRunAt = 1f;

    private EnemyController controller;
    private EnemyBrain subscribedBrain;
    private int lastSpecial = -1;
    private float nextHitTime;

    private void Awake()
    {
        controller = GetComponent<EnemyController>();

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
        if (controller.Health != null)
        {
            controller.Health.Damaged += HandleDamaged;
            controller.Health.Died += HandleDied;
        }
    }

    private void Start()
    {
        // Brain baru tersedia setelah Awake EnemyController.
        subscribedBrain = controller.Brain;

        if (subscribedBrain != null)
            subscribedBrain.AttackPerformed += HandleAttack;

        if (animator != null)
            animator.SetBool(AnimParams.IsDeadHash, false);
    }

    private void OnDisable()
    {
        if (controller.Health != null)
        {
            controller.Health.Damaged -= HandleDamaged;
            controller.Health.Died -= HandleDied;
        }
    }

    private void OnDestroy()
    {
        if (subscribedBrain != null)
            subscribedBrain.AttackPerformed -= HandleAttack;
    }

    private void Update()
    {
        if (animator == null || controller.Health.IsDead)
            return;

        Vector3 velocity = controller.Motor.Velocity;
        velocity.y = 0f;

        float baseSpeed = Mathf.Max(0.01f, controller.Motor.BaseSpeed * fullRunAt);
        float forward = Vector3.Dot(velocity, transform.forward) / baseSpeed;
        float right = Vector3.Dot(velocity, transform.right) / baseSpeed;

        animator.SetFloat(AnimParams.ForwardHash, forward, dampTime, Time.deltaTime);
        animator.SetFloat(AnimParams.MoveZHash, forward, dampTime, Time.deltaTime);
        animator.SetFloat(AnimParams.MoveXHash, right, dampTime, Time.deltaTime);
        animator.SetFloat(AnimParams.SpeedHash, velocity.magnitude / baseSpeed, dampTime, Time.deltaTime);

        animator.SetBool(AnimParams.ScanningHash, controller.Brain != null && controller.Brain.IsScanning);

        int special = controller.Brain != null ? AnimParams.ToSpecial(controller.Brain.CurrentAction) : AnimParams.SpecialNone;

        if (special != lastSpecial)
        {
            lastSpecial = special;
            animator.SetInteger(AnimParams.SpecialHash, special);
        }
    }

    private void HandleAttack()
    {
        if (animator != null && !controller.Health.IsDead)
            animator.SetTrigger(AnimParams.AttackHash);
    }

    private void HandleDamaged(float amount, DamageInfo info)
    {
        // Aksi terkunci (windup, lunge) tidak diselingi animasi kena hit.
        if (animator == null || controller.Health.IsDead || lastSpecial != AnimParams.SpecialNone)
            return;

        if (Time.time < nextHitTime)
            return;

        nextHitTime = Time.time + hitCooldown;
        animator.SetTrigger(AnimParams.HitHash);
    }

    private void HandleDied(EnemyHealth health)
    {
        if (animator == null)
            return;

        animator.SetInteger(AnimParams.SpecialHash, AnimParams.SpecialNone);
        animator.SetBool(AnimParams.IsDeadHash, true);
    }
}
