using System.Collections.Generic;
using UnityEngine;

// Pusat satu musuh. Memegang referensi komponen, menerapkan EnemyRoleData, dan menjalankan siklus AI:
// Perception -> Blackboard -> Brain, setiap 0,1-0,2 s dengan offset acak supaya musuh tidak berpikir di frame yang sama.
// Gerak NavMeshAgent tetap halus setiap frame.
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyPerception))]
[RequireComponent(typeof(EnemyBlackboard))]
[RequireComponent(typeof(EnemyMotor))]
public class EnemyController : MonoBehaviour
{
    private static readonly List<EnemyController> active = new List<EnemyController>();

    // Semua musuh aktif, untuk TacticalCoordinator dan overlay debug.
    public static IReadOnlyList<EnemyController> Active => active;

    [Header("Role")]
    [SerializeField] private EnemyRoleData roleData;

    [Header("Brain Tick")]
    [SerializeField] private float minTickInterval = 0.1f;
    [SerializeField] private float maxTickInterval = 0.2f;

    [Header("Reaction")]
    // Musuh yang terkena serangan langsung tahu posisi MC walaupun tidak melihatnya.
    [SerializeField] private bool alertOnDamage = true;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = true;

    private float nextTickTime;
    private float lastTickTime;
    private bool localToken;

    public EnemyRoleData RoleData => roleData;

    public EnemyRole Role => roleData != null ? roleData.Role : EnemyRole.Charger;

    public EnemyHealth Health { get; private set; }

    public EnemyPerception Perception { get; private set; }

    public EnemyBlackboard Blackboard { get; private set; }

    public EnemyMotor Motor { get; private set; }

    public EnemyBrain Brain { get; private set; }

    public bool LogStateChanges => logStateChanges;

    // Token menyerang. Dengan TacticalCoordinator, token bisa dicabut dari luar (timeout), jadi selalu ditanya ke penyedia.
    public bool HasAttackToken => AttackTokens.Provider != null ? AttackTokens.Provider.Holds(this) : localToken;

    public bool RequestAttackToken()
    {
        if (HasAttackToken)
            return true;

        if (AttackTokens.Provider == null)
        {
            localToken = true;
            return true;
        }

        return AttackTokens.Provider.Request(this);
    }

    public void ReleaseAttackToken()
    {
        localToken = false;
        AttackTokens.Provider?.Release(this);
    }

    private void Awake()
    {
        Health = GetComponent<EnemyHealth>();
        Perception = GetComponent<EnemyPerception>();
        Blackboard = GetComponent<EnemyBlackboard>();
        Motor = GetComponent<EnemyMotor>();
        Brain = GetComponent<EnemyBrain>();

        if (roleData == null)
        {
            Debug.LogError(name + ": EnemyController belum punya Role Data.", this);
            enabled = false;
            return;
        }

        ApplyRoleData();

        if (Brain != null)
            Brain.Initialize(this);
        else
            Debug.LogWarning(name + ": belum ada komponen brain (turunan EnemyBrain).", this);
    }

    private void OnEnable()
    {
        active.Add(this);

        if (Health != null)
        {
            Health.Died += HandleDied;
            Health.Damaged += HandleDamaged;
        }

        // Offset acak awal supaya musuh yang muncul bersamaan tidak tick di frame yang sama.
        lastTickTime = Time.time;
        nextTickTime = Time.time + Random.Range(0f, maxTickInterval);
    }

    private void OnDisable()
    {
        active.Remove(this);
        ReleaseAttackToken();

        if (Health != null)
        {
            Health.Died -= HandleDied;
            Health.Damaged -= HandleDamaged;
        }
    }

    private void ApplyRoleData()
    {
        Health.SetMaxHealth(roleData.MaxHealth);
        Health.SetDefense(roleData.Defense);

        Motor.Configure(roleData);
        Perception.Configure(roleData);
        Blackboard.SetMemoryDuration(roleData.MemoryDuration);

        RendererTint tint = GetComponent<RendererTint>();

        if (tint != null)
            tint.SetColor(roleData.Tint);
    }

    private void Update()
    {
        if (Health.IsDead || Time.time < nextTickTime)
            return;

        float deltaTime = Time.time - lastTickTime;

        lastTickTime = Time.time;
        nextTickTime = Time.time + Random.Range(minTickInterval, maxTickInterval);

        Perception.Sense();
        Blackboard.TickMemory();

        if (Brain != null)
            Brain.Tick(deltaTime);
    }

    private void HandleDamaged(float amount, DamageInfo info)
    {
        if (!alertOnDamage || Health.IsDead || Blackboard.CanSeePlayer)
            return;

        PlayerHealth player = PlayerHealth.Current;

        if (player != null && info.source == player.gameObject)
            Blackboard.Alert(player.transform.position, info.source);
    }

    private void HandleDied(EnemyHealth health)
    {
        ReleaseAttackToken();

        Motor.Stop();
        Motor.SetAgentEnabled(false);

        if (Brain != null)
            Brain.OnDied();
    }
}
