using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Enemy alternatif yang memilih aksi dengan Utility AI. Aksi dan executor-nya sama dengan EnemyBTController; yang berbeda hanya cara memilihnya.
public class EnemyUtilityController : MonoBehaviour, IEnemyBrain
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

    // Opsional: jika diisi, menimpa nilai Perception, Combat, Flee, Search, Movement, dan kurva fleeUrgencyByHealth saat game dimulai.
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
    [SerializeField] private float safeDistance = 12f;
    [SerializeField] private float fleeCooldown = 10f;

    [Header("Search")]
    [SerializeField] private float searchSpeed = 3f;
    [SerializeField] private float searchScanDuration = 4f;
    [SerializeField] private float searchScanAngle = 70f;
    [SerializeField] private float searchTimeout = 15f;
    [SerializeField] private float searchSampleDistance = 2f;
    [SerializeField] private float searchPatience = 12f;

    [Header("Movement")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float chaseSpeed = 4f;
    [SerializeField] private float fleeSpeed = 5f;

    [Header("Utility")]
    [SerializeField] private float decisionInterval = 0.25f;
    [SerializeField] private float inertiaBonus = 0.1f;

    // Plafon skor tiap aksi; mengatur prioritas kasar antar aksi.
    [SerializeField] private float fleeWeight = 1f;
    [SerializeField] private float attackWeight = 0.9f;
    [SerializeField] private float chaseWeight = 0.7f;
    [SerializeField] private float searchWeight = 0.5f;
    [SerializeField] private float patrolWeight = 0.2f;

    // x = rasio HP (0-1), y = skor (0-1).
    [SerializeField] private AnimationCurve fleeUrgencyByHealth =
        new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.3f, 1f),
            new Keyframe(0.5f, 0f)
        );

    [SerializeField] private AnimationCurve attackWillingnessByHealth =
        AnimationCurve.Linear(0f, 0.5f, 1f, 1f);

    [Header("Debug")]
    [SerializeField] private EnemyAction activeAction = EnemyAction.Patrol;
    [SerializeField] private float scoreFlee;
    [SerializeField] private float scoreAttack;
    [SerializeField] private float scoreChase;
    [SerializeField] private float scoreSearch;
    [SerializeField] private float scorePatrol;

    private NavMeshAgent agent;
    private UtilitySelector selector;
    private PlayerHealth playerHealth;

    private int currentPatrolIndex = 0;
    private float nextAttackTime = 0f;

    // Setelah flee selesai, flee tidak dipertimbangkan lagi selama fleeCooldown agar enemy tidak bolak-balik di safe point.
    private float fleeCooldownUntil = 0f;

    // Hasil sensing frame ini.
    private bool canSee;

    // Memori Search.
    private bool hasLastKnownPosition = false;
    private Vector3 lastKnownPosition;
    private float lastSeenTime;
    private SearchPhase searchPhase = SearchPhase.None;
    private float searchStartTime;
    private float scanStartTime;
    private float scanBaseYaw;

    public string BrainName =>
        personality != null
            ? "Utility/" + personality.DisplayName
            : "Utility";

    public EnemyAction CurrentAction => activeAction;

    public bool IsScanning =>
        activeAction == EnemyAction.Search &&
        searchPhase == SearchPhase.Scan;

    private float HealthRatio =>
        health.MaxHealth > 0f
            ? health.CurrentHealth / health.MaxHealth
            : 0f;

    private float PlayerDistance =>
        player != null
            ? Vector3.Distance(
                transform.position,
                player.position
            )
            : Mathf.Infinity;

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

        safeDistance = personality.SafeDistance;

        searchScanDuration = personality.SearchScanDuration;

        patrolSpeed = personality.PatrolSpeed;
        chaseSpeed = personality.ChaseSpeed;
        fleeSpeed = personality.FleeSpeed;

        // Utility tidak memakai ambang tetap: rasio HP mulai kabur diterjemahkan menjadi kurva. 
        // Urgensi penuh di bawah rasio itu dan turun ke nol 0.2 di atasnya (bentuk yang sama dengan kurva bawaan untuk rasio 0.3).
        float start = Mathf.Clamp(
            personality.FleeStartHealthRatio,
            0.05f,
            0.8f
        );

        fleeUrgencyByHealth =
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(start, 1f),
                new Keyframe(start + 0.2f, 0f)
            );
    }

    private void Start()
    {
        BuildUtilityActions();
    }

    private void Update()
    {
        if (selector == null)
            return;

        // Dead adalah gerbang, bukan hasil penilaian.
        if (health.IsDead)
        {
            Die();

            return;
        }

        Sense();

        selector.Tick();

        UpdateDebugScores();
    }

    private void BuildUtilityActions()
    {
        // Urutan hanya berpengaruh saat skor seri (yang pertama menang).
        selector =
            new UtilitySelector(
                new List<UtilityAction>
                {
                    new UtilityAction(EnemyAction.Flee, ScoreFlee, ExecuteFlee),
                    new UtilityAction(EnemyAction.Attack, ScoreAttack, ExecuteAttack),
                    new UtilityAction(EnemyAction.Chase, ScoreChase, ExecuteChase),
                    new UtilityAction(EnemyAction.Search, ScoreSearch, ExecuteSearch),
                    new UtilityAction(EnemyAction.Patrol, ScorePatrol, ExecutePatrol)
                },
                decisionInterval,
                inertiaBonus
            );
    }

    // ==================================================
    // SENSING
    // ==================================================

    // Dijalankan setiap frame: memperbarui persepsi dan memori Search.
    private void Sense()
    {
        canSee = CanSeePlayer();

        if (playerHealth != null && playerHealth.IsDead)
            ClearLastKnownPosition();

        if (canSee)
        {
            lastKnownPosition = player.position;
            lastSeenTime = Time.time;
            hasLastKnownPosition = true;
            searchPhase = SearchPhase.None;
        }
        else if (hasLastKnownPosition && LostFor() >= 1f)
        {
            // Kesabaran habis: berhenti mencari.
            ClearLastKnownPosition();
        }
    }

    // Seberapa lama player hilang dari pandangan, 0 (baru hilang) sampai 1 (kesabaran habis).
    private float LostFor()
    {
        if (!hasLastKnownPosition)
            return 1f;

        // Setelah tiba, enemy menuntaskan scan seperti pada BT; kesabaran hanya membatasi perjalanan menuju last known position.
        if (searchPhase == SearchPhase.Scan)
            return 0f;

        return Mathf.Clamp01(
            (Time.time - lastSeenTime) / searchPatience
        );
    }

    private bool IsInAttackRange()
    {
        if (player == null)
            return false;

        // Histeresis seperti BT/FSM: enemy yang sedang menyerang baru dianggap keluar jangkauan setelah melewati attackExitRange.
        float range =
            activeAction == EnemyAction.Attack
                ? attackExitRange
                : attackRange;

        return PlayerDistance <= range;
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
    // SCORES
    // ==================================================

    // Keinginan kabur dari HP. 0 selama masa cooldown setelah flee selesai.
    private float FleeUrgency()
    {
        if (Time.time < fleeCooldownUntil)
            return 0f;

        return Mathf.Clamp01(
            fleeUrgencyByHealth.Evaluate(HealthRatio)
        );
    }

    private float ScoreFlee()
    {
        if (safePoint == null)
            return 0f;

        // Ancaman makin besar saat player makin dekat.
        float threat =
            1f - Mathf.Clamp01(PlayerDistance / safeDistance);

        return FleeUrgency() * threat * fleeWeight;
    }

    private float ScoreAttack()
    {
        if (!canSee || !IsInAttackRange())
            return 0f;

        float willingness =
            Mathf.Clamp01(
                attackWillingnessByHealth.Evaluate(HealthRatio)
            );

        return willingness * attackWeight;
    }

    private float ScoreChase()
    {
        if (!canSee || IsInAttackRange())
            return 0f;

        // Enemy yang ingin kabur tidak mengejar; mencegah osilasi chase <-> flee di sekitar titik silang skor.
        return chaseWeight * (1f - FleeUrgency());
    }

    private float ScoreSearch()
    {
        if (!hasLastKnownPosition || canSee)
            return 0f;

        // Skor meluruh seiring waktu; saat di bawah patrol, enemy menyerah.
        return searchWeight
               * (1f - LostFor())
               * (1f - FleeUrgency());
    }

    private float ScorePatrol()
    {
        return patrolWeight;
    }

    // ==================================================
    // EXECUTORS
    // ==================================================

    private void ExecutePatrol()
    {
        SetAction(EnemyAction.Patrol);

        if (patrolPoints == null ||
            patrolPoints.Length == 0)
        {
            return;
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
    }

    private void ExecuteChase()
    {
        if (player == null)
            return;

        SetAction(EnemyAction.Chase);

        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.stoppingDistance =
            attackRange * 0.8f;

        agent.SetDestination(player.position);
    }

    private void ExecuteAttack()
    {
        if (player == null)
            return;

        SetAction(EnemyAction.Attack);

        agent.isStopped = true;

        FacePlayer();

        // Keputusan hanya diperbarui tiap decisionInterval, jadi pukulan diperiksa lagi terhadap jangkauan saat ini.
        if (!canSee || !IsInAttackRange())
            return;

        if (Time.time < nextAttackTime)
            return;

        nextAttackTime = Time.time + attackCooldown;

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
    }

    private void ExecuteFlee()
    {
        if (safePoint == null)
            return;

        SetAction(EnemyAction.Flee);

        // Enemy yang kabur tidak boleh kembali mencari ke arah player.
        ClearLastKnownPosition();

        agent.isStopped = false;
        agent.speed = fleeSpeed;
        agent.stoppingDistance = 0.5f;

        agent.SetDestination(
            safePoint.position
        );

        float safePointDistance =
            Vector3.Distance(
                transform.position,
                safePoint.position
            );

        // Selesai jika sudah cukup jauh dari player atau sudah sampai di safe point (kondisi yang sama dengan BT dan FSM).
        if (PlayerDistance >= safeDistance ||
            safePointDistance <= 1f)
        {
            fleeCooldownUntil = Time.time + fleeCooldown;

            selector.RequestDecision();
        }
    }

    // Move (berjalan ke last known position) lalu Scan (berhenti dan menoleh kiri-kanan) sampai durasi habis.
    private void ExecuteSearch()
    {
        if (!hasLastKnownPosition)
        {
            selector.RequestDecision();

            return;
        }

        SetAction(EnemyAction.Search);

        if (searchPhase == SearchPhase.None)
        {
            // Posisi player bisa berada di luar NavMesh, jadi di-snap ke titik NavMesh terdekat.
            NavMeshHit hit;

            if (!NavMesh.SamplePosition(
                    lastKnownPosition,
                    out hit,
                    searchSampleDistance,
                    NavMesh.AllAreas))
            {
                FinishSearch();

                return;
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
                FinishSearch();

                return;
            }

            if (!arrived)
                return;

            searchPhase = SearchPhase.Scan;
            scanStartTime = Time.time;
            scanBaseYaw = transform.eulerAngles.y;

            agent.isStopped = true;
            agent.ResetPath();
        }

        // Scan: satu siklus ayunan kanan-tengah-kiri-tengah.
        float elapsed = Time.time - scanStartTime;

        if (elapsed >= searchScanDuration)
        {
            FinishSearch();

            return;
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
    }

    private void FinishSearch()
    {
        ClearLastKnownPosition();

        selector.RequestDecision();
    }

    private void ClearLastKnownPosition()
    {
        hasLastKnownPosition = false;
        searchPhase = SearchPhase.None;
    }

    private void Die()
    {
        SetAction(EnemyAction.Dead);

        if (!agent.isStopped)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
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

    private void UpdateDebugScores()
    {
        foreach (UtilityAction action in selector.Actions)
        {
            switch (action.Id)
            {
                case EnemyAction.Flee: scoreFlee = action.Score; break;
                case EnemyAction.Attack: scoreAttack = action.Score; break;
                case EnemyAction.Chase: scoreChase = action.Score; break;
                case EnemyAction.Search: scoreSearch = action.Score; break;
                case EnemyAction.Patrol: scorePatrol = action.Score; break;
            }
        }
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
