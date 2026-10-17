using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBTController : MonoBehaviour, IEnemyBrain
{
    // Sub-fase Search: berjalan ke last known position, lalu menoleh.
    private enum SearchPhase
    {
        None,
        Move,
        Scan
    }

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private Transform safePoint;
    [SerializeField] private EnemyHealth health;

    // Opsional: jika diisi, menimpa nilai Perception, Combat, Flee, Search, dan Movement di bawah saat game dimulai.
    [SerializeField] private EnemyPersonality personality;

    [Header("Perception")]
    [SerializeField] private float visionRange = 10f;
    [SerializeField] private float visionAngle = 90f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Combat")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackExitRange = 2.75f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float attackDamage = 10f;

    [Header("Flee")]
    [SerializeField] private float lowHealthThreshold = 30f;
    [SerializeField] private float safeDistance = 12f;

    [Header("Search")]
    [SerializeField] private float searchSpeed = 3f;
    [SerializeField] private float searchScanDuration = 4f;
    [SerializeField] private float searchScanAngle = 70f;
    [SerializeField] private float searchTimeout = 15f;
    [SerializeField] private float searchSampleDistance = 2f;

    [Header("Movement")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float chaseSpeed = 4f;
    [SerializeField] private float fleeSpeed = 5f;

    [Header("Debug")]
    [SerializeField] private EnemyAction activeAction = EnemyAction.Patrol;

    private NavMeshAgent agent;
    private BTNode rootNode;
    private PlayerHealth playerHealth;

    private int currentPatrolIndex = 0;

    // Flee hanya dipicu sekali, sama seperti fleeTriggered pada FSM.
    private bool fleeCompleted = false;

    // Memori Search BT bersifat stateless, jadi posisi terakhir player dan progres pencarian disimpan di sini.
    private bool hasLastKnownPosition = false;
    private Vector3 lastKnownPosition;
    private SearchPhase searchPhase = SearchPhase.None;
    private float searchStartTime;
    private float scanStartTime;
    private float scanBaseYaw;

    public string BrainName =>
        personality != null
            ? "BT/" + personality.DisplayName
            : "BT";

    public EnemyAction CurrentAction => activeAction;

    // True saat enemy sudah tiba di last known position dan sedang menoleh
    // (dipakai EnemyAnimation untuk memilih animasi Idle).
    public bool IsScanning =>
        activeAction == EnemyAction.Search &&
        searchPhase == SearchPhase.Scan;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (health == null)
            health = GetComponent<EnemyHealth>();

        if (health == null)
        {
            Debug.LogWarning(
                name
                + ": EnemyHealth belum terpasang, ditambahkan otomatis. "
                + "Tambahkan komponen EnemyHealth lewat Inspector."
            );

            health = gameObject.AddComponent<EnemyHealth>();
        }

        if (player != null)
            playerHealth = player.GetComponent<PlayerHealth>();

        ApplyPersonality();
    }

    private void ApplyPersonality()
    {
        if (personality == null)
            return;

        visionRange = personality.VisionRange;
        visionAngle = personality.VisionAngle;

        attackRange = personality.AttackRange;
        attackExitRange = personality.AttackExitRange;
        attackCooldown = personality.AttackCooldown;
        attackDamage = personality.AttackDamage;

        // BT memakai ambang HP absolut, sedangkan personality memakai rasio.
        lowHealthThreshold =
            personality.FleeStartHealthRatio * health.MaxHealth;
        safeDistance = personality.SafeDistance;

        searchScanDuration = personality.SearchScanDuration;

        patrolSpeed = personality.PatrolSpeed;
        chaseSpeed = personality.ChaseSpeed;
        fleeSpeed = personality.FleeSpeed;
    }

    private void Start()
    {
        BuildBehaviorTree();
    }

    private void Update()
    {
        if (rootNode != null)
        {
            rootNode.Tick();
        }
    }

    private void BuildBehaviorTree()
    {
        // ------------------------------
        // DEAD
        // ------------------------------

        BTNode deadSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(IsDead),
                    new ActionNode(Die)
                }
            );

        // ------------------------------
        // FLEE
        // ------------------------------

        BTNode fleeSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(ShouldFlee),
                    new ActionNode(Flee)
                }
            );

        // ------------------------------
        // ATTACK
        // ------------------------------

        // Cooldown hanya membungkus pukulan; EngageTarget dijalankan setiap tick agar enemy tetap berhenti dan menghadap player selama menunggu cooldown (seperti UpdateAttack pada FSM).
        BTNode strikeWithCooldown =
            new CooldownDecorator(
                new ActionNode(StrikePlayer),
                attackCooldown
            );

        BTNode attackSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(CanSeePlayer),
                    new ConditionNode(IsPlayerInAttackRange),
                    new ActionNode(EngageTarget),
                    strikeWithCooldown
                }
            );

        // ------------------------------
        // CHASE
        // ------------------------------

        BTNode chaseSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(CanSeePlayer),
                    new ActionNode(ChasePlayer)
                }
            );

        // ------------------------------
        // SEARCH
        // ------------------------------

        BTNode searchSequence =
            new SequenceNode(
                new List<BTNode>
                {
                    new ConditionNode(HasLastKnownPosition),
                    new ActionNode(SearchLastKnownPosition)
                }
            );

        // ------------------------------
        // PATROL
        // ------------------------------

        BTNode patrolAction =
            new ActionNode(Patrol);

        // ------------------------------
        // ROOT SELECTOR
        // ------------------------------

        rootNode =
            new SelectorNode(
                new List<BTNode>
                {
                    deadSequence,
                    fleeSequence,
                    attackSequence,
                    chaseSequence,
                    searchSequence,
                    patrolAction
                }
            );
    }

    // ==================================================
    // CONDITIONS
    // ==================================================

    private bool IsDead()
    {
        return health.IsDead;
    }

    private bool ShouldFlee()
    {
        return !fleeCompleted &&
               health.CurrentHealth <= lowHealthThreshold;
    }

    private bool HasLastKnownPosition()
    {
        return hasLastKnownPosition;
    }

    private bool IsPlayerInAttackRange()
    {
        if (player == null)
            return false;

        float distance =
            Vector3.Distance(
                transform.position,
                player.position
            );

        // Histeresis seperti FSM: enemy yang sedang menyerang baru dianggap keluar jangkauan setelah melewati attackExitRange.
        float range =
            activeAction == EnemyAction.Attack
                ? attackExitRange
                : attackRange;

        return distance <= range;
    }

    private bool CanSeePlayer()
    {
        if (player == null)
            return false;

        // Player yang sudah mati tidak lagi terlihat oleh enemy.
        if (playerHealth != null && playerHealth.IsDead)
            return false;

        Vector3 eyePosition =
            transform.position
            + Vector3.up * 1.5f;

        Vector3 targetPosition =
            player.position
            + Vector3.up * 1f;

        Vector3 directionToPlayer =
            targetPosition - eyePosition;

        float distanceToPlayer =
            directionToPlayer.magnitude;

        // Player terlalu jauh.
        if (distanceToPlayer > visionRange)
            return false;

        // Player berada di luar Field of View.
        float angle =
            Vector3.Angle(
                transform.forward,
                directionToPlayer
            );

        if (angle > visionAngle * 0.5f)
            return false;

        // Periksa apakah ada obstacle.
        bool blocked =
            Physics.Raycast(
                eyePosition,
                directionToPlayer.normalized,
                distanceToPlayer,
                obstacleMask
            );

        return !blocked;
    }

    // ==================================================
    // ACTIONS
    // ==================================================

    private NodeState Patrol()
    {
        SetAction(EnemyAction.Patrol);

        if (patrolPoints == null ||
            patrolPoints.Length == 0)
        {
            return NodeState.Failure;
        }

        agent.isStopped = false;
        agent.speed = patrolSpeed;
        agent.stoppingDistance = 0.2f;

        Transform target =
            patrolPoints[currentPatrolIndex];

        agent.SetDestination(target.position);

        if (!agent.pathPending &&
            agent.remainingDistance <= 0.5f)
        {
            currentPatrolIndex++;

            if (currentPatrolIndex
                >= patrolPoints.Length)
            {
                currentPatrolIndex = 0;
            }
        }

        return NodeState.Running;
    }

    private NodeState ChasePlayer()
    {
        if (player == null)
            return NodeState.Failure;

        SetAction(EnemyAction.Chase);

        RememberPlayerPosition();

        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.stoppingDistance =
            attackRange * 0.8f;

        agent.SetDestination(player.position);

        return NodeState.Running;
    }

    // Berhenti dan menghadap player. Dijalankan setiap tick selama player berada dalam jangkauan serang.
    private NodeState EngageTarget()
    {
        if (player == null)
            return NodeState.Failure;

        SetAction(EnemyAction.Attack);

        RememberPlayerPosition();

        agent.isStopped = true;

        FacePlayer();

        return NodeState.Success;
    }

    // Pukulan sebenarnya; hanya dipanggil saat cooldown sudah habis.
    private NodeState StrikePlayer()
    {
        Debug.Log(
            name
            + " attacks Player! Damage = "
            + attackDamage
        );

        if (playerHealth != null)
        {
            playerHealth.TakeDamage(
                attackDamage
            );
        }

        return NodeState.Success;
    }

    private NodeState Flee()
    {
        if (safePoint == null)
            return NodeState.Failure;

        SetAction(EnemyAction.Flee);

        // Enemy yang kabur tidak boleh kembali mencari ke arah player.
        ClearLastKnownPosition();

        agent.isStopped = false;
        agent.speed = fleeSpeed;
        agent.stoppingDistance = 0.5f;

        agent.SetDestination(
            safePoint.position
        );

        float playerDistance =
            player != null
                ? Vector3.Distance(
                    transform.position,
                    player.position
                )
                : Mathf.Infinity;

        float safePointDistance =
            Vector3.Distance(
                transform.position,
                safePoint.position
            );

        // Selesai jika sudah cukup jauh dari player atau sudah sampai di safe point (kondisi yang sama dengan FSM).
        if (playerDistance >= safeDistance ||
            safePointDistance <= 1f)
        {
            fleeCompleted = true;

            return NodeState.Success;
        }

        return NodeState.Running;
    }

    // Mencari ke last known position: Move (berjalan ke sana) lalu scan (berhenti dan menoleh kiri-kanan) sampai durasi habis.
    private NodeState SearchLastKnownPosition()
    {
        // Player yang sudah mati tidak perlu dicari lagi.
        if (playerHealth != null && playerHealth.IsDead)
        {
            ClearLastKnownPosition();

            return NodeState.Failure;
        }

        SetAction(EnemyAction.Search);

        if (searchPhase == SearchPhase.None)
        {
            // Posisi player bisa berada di luar NavMesh (misalnya di atas obstacle), jadi di-snap ke titik NavMesh terdekat.
            NavMeshHit hit;

            if (!NavMesh.SamplePosition(
                    lastKnownPosition,
                    out hit,
                    searchSampleDistance,
                    NavMesh.AllAreas))
            {
                ClearLastKnownPosition();

                return NodeState.Failure;
            }

            lastKnownPosition = hit.position;
            searchStartTime = Time.time;
            searchPhase = SearchPhase.Move;

            agent.isStopped = false;
            agent.speed = searchSpeed;
            agent.stoppingDistance = 0.2f;
            agent.SetDestination(lastKnownPosition);
        }

        if (searchPhase == SearchPhase.Move)
        {
            bool arrived =
                !agent.pathPending &&
                agent.remainingDistance <= 0.5f;

            // Batas waktu total sebagai pengaman jika tujuan tak tercapai.
            if (Time.time - searchStartTime >= searchTimeout)
            {
                ClearLastKnownPosition();

                return NodeState.Success;
            }

            if (!arrived)
                return NodeState.Running;

            searchPhase = SearchPhase.Scan;
            scanStartTime = Time.time;
            scanBaseYaw = transform.eulerAngles.y;

            agent.isStopped = true;
            agent.ResetPath();
        }

        // Scan: satu siklus ayunan kanan-tengah-kiri-tengah selama durasi scan. Timer dihitung sejak tiba di tujuan.
        float elapsed = Time.time - scanStartTime;

        if (elapsed >= searchScanDuration)
        {
            ClearLastKnownPosition();

            return NodeState.Success;
        }

        float yawOffset =
            Mathf.Sin(
                elapsed / searchScanDuration
                * Mathf.PI * 2f
            )
            * searchScanAngle;

        transform.rotation =
            Quaternion.Euler(
                0f,
                scanBaseYaw + yawOffset,
                0f
            );

        return NodeState.Running;
    }

    // Dipanggil setiap tick saat player terlihat; begitu pandangan hilang, nilai terakhir ini menjadi tujuan Search.
    private void RememberPlayerPosition()
    {
        lastKnownPosition = player.position;
        hasLastKnownPosition = true;
        searchPhase = SearchPhase.None;
    }

    private void ClearLastKnownPosition()
    {
        hasLastKnownPosition = false;
        searchPhase = SearchPhase.None;
    }

    private NodeState Die()
    {
        SetAction(EnemyAction.Dead);

        if (!agent.isStopped)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        return NodeState.Success;
    }

    private void SetAction(EnemyAction action)
    {
        if (activeAction == action)
            return;

        activeAction = action;

        Debug.Log(
            name
            + " → State: "
            + activeAction
        );
    }

    private void FacePlayer()
    {
        Vector3 direction =
            player.position
            - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude
            < 0.001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(direction);

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                10f * Time.deltaTime
            );
    }

    // ==================================================
    // GIZMOS
    // ==================================================

    private void OnDrawGizmosSelected()
    {
        // Vision Range
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            transform.position,
            visionRange
        );

        // Attack Range
        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position,
            attackRange
        );

        // Attack Exit Range
        Gizmos.color = Color.magenta;

        Gizmos.DrawWireSphere(
            transform.position,
            attackExitRange
        );

        // Last Known Position
        if (hasLastKnownPosition)
        {
            Gizmos.color = Color.blue;

            Gizmos.DrawWireSphere(
                lastKnownPosition,
                0.4f
            );
        }

        // FOV direction
        Gizmos.color = Color.yellow;

        Vector3 leftDirection =
            Quaternion.Euler(
                0,
                -visionAngle * 0.5f,
                0
            )
            * transform.forward;

        Vector3 rightDirection =
            Quaternion.Euler(
                0,
                visionAngle * 0.5f,
                0
            )
            * transform.forward;

        Gizmos.DrawRay(
            transform.position,
            leftDirection * visionRange
        );

        Gizmos.DrawRay(
            transform.position,
            rightDirection * visionRange
        );
    }
}
