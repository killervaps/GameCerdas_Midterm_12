using UnityEngine;

// Stat dan parameter satu role musuh. Penerus pola EnemyPersonality.
// EnemyController membaca asset ini saat Awake lalu membagikannya ke Health, Motor, Perception, dan Blackboard.
[CreateAssetMenu(fileName = "Role_New", menuName = "MathGate/Enemy Role Data")]
public class EnemyRoleData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private EnemyRole role = EnemyRole.Charger;
    [SerializeField] private string displayName;
    // Pembeda visual selama semua role masih memakai model TripleT.
    [SerializeField] private Color tint = Color.white;

    [Header("Health")]
    [SerializeField] private float maxHealth = 60f;
    [SerializeField] private float defense = 0f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f;
    // Derajat per detik, dipakai NavMeshAgent dan saat menghadap target.
    [SerializeField] private float turnSpeed = 540f;
    [SerializeField] private float acceleration = 20f;

    [Header("Perception")]
    [SerializeField] private float visionRange = 14f;
    [SerializeField, Range(0f, 360f)] private float visionAngle = 120f;
    // Di dalam radius ini MC tetap terdeteksi walaupun di belakang (asal tidak terhalang dinding).
    [SerializeField] private float proximityRadius = 2f;
    // Tinggi mata relatif pivot, sebelum dikali skala objek.
    [SerializeField] private float eyeHeight = 1.8f;
    // Lama musuh mengingat posisi terakhir MC setelah kehilangan pandangan.
    [SerializeField] private float memoryDuration = 8f;

    [Header("Combat")]
    [SerializeField] private float damage = 15f;
    [SerializeField] private float attackCooldown = 2.5f;
    // Jarak serang dasar. Brain per role boleh memakai jarak kerjanya sendiri.
    [SerializeField] private float attackRange = 2f;

    [Header("Search")]
    [SerializeField, Range(0.1f, 1.5f)] private float searchSpeedMultiplier = 0.7f;
    [SerializeField] private float searchScanDuration = 3f;
    [SerializeField] private float searchScanAngle = 70f;
    // Pengaman jika posisi terakhir tidak bisa dicapai.
    [SerializeField] private float searchTimeout = 10f;
    [SerializeField] private float navMeshSampleDistance = 2f;

    // Bagian di bawah hanya dibaca brain role yang bersangkutan.

    [Header("Charger (lunge)")]
    [SerializeField] private float lungeRange = 4f;
    [SerializeField] private float windupDuration = 0.6f;
    // Jumlah isyarat "tung tung tung" selama windup.
    [SerializeField] private int windupBeats = 3;
    [SerializeField] private float lungeDuration = 0.35f;
    [SerializeField] private float lungeSpeed = 12f;
    [SerializeField] private float lungeHitRadius = 1f;
    [SerializeField] private float recoverDuration = 1f;

    [Header("Shooter (utility)")]
    // Jarak ideal = Range MC + nilai ini, tepat di luar jangkauan tembak MC.
    [SerializeField] private float idealDistanceOffset = 1.5f;
    // Skor Flee terhadap rasio HP (0-1). Tinggi di bawah 30%.
    [SerializeField] private AnimationCurve fleeByHealth = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.3f, 0.8f),
        new Keyframe(0.45f, 0f),
        new Keyframe(1f, 0f));
    [SerializeField] private float fleeDistance = 6f;
    [SerializeField, Range(0f, 1f)] private float holdScore = 0.2f;
    // Bonus skor Reposition jika titik tujuan dekat Guardian kawan.
    [SerializeField, Range(0f, 1f)] private float guardianProximityBonus = 0.2f;
    [SerializeField] private float guardianBonusRadius = 3f;
    [SerializeField] private float utilityDecisionInterval = 0.25f;
    [SerializeField] private float utilityInertia = 0.1f;

    [Header("Guardian (anchor)")]
    // MC masuk radius ini (diukur dari anchor), Guardian bereaksi.
    [SerializeField] private float aggroRadius = 5f;
    // Jarak maksimal Guardian dari anchor.
    [SerializeField] private float leashRadius = 8f;
    // Hysteresis: setelah keluar leash, baru boleh mengejar lagi jika sudah sedekat ini ke anchor.
    [SerializeField] private float returnThreshold = 1f;
    // Jarak titik jaga dari Shooter ke arah MC (mode bodyguard).
    [SerializeField] private float guardDistance = 2f;
    [SerializeField] private float enrageSpeedMultiplier = 1.4f;
    // Diserang MC di luar aggro radius tetapi masih dalam leash, Guardian boleh mengejar selama sekian detik.
    [SerializeField] private float damageAggroDuration = 3f;

    public EnemyRole Role => role;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? role.ToString() : displayName;
    public Color Tint => tint;

    public float MaxHealth => maxHealth;
    public float Defense => defense;

    public float MoveSpeed => moveSpeed;
    public float TurnSpeed => turnSpeed;
    public float Acceleration => acceleration;

    public float VisionRange => visionRange;
    public float VisionAngle => visionAngle;
    public float ProximityRadius => proximityRadius;
    public float EyeHeight => eyeHeight;
    public float MemoryDuration => memoryDuration;

    public float Damage => damage;
    public float AttackCooldown => attackCooldown;
    public float AttackRange => attackRange;

    public float SearchSpeedMultiplier => searchSpeedMultiplier;
    public float SearchScanDuration => searchScanDuration;
    public float SearchScanAngle => searchScanAngle;
    public float SearchTimeout => searchTimeout;
    public float NavMeshSampleDistance => navMeshSampleDistance;

    public float LungeRange => lungeRange;
    public float WindupDuration => windupDuration;
    public int WindupBeats => Mathf.Max(1, windupBeats);
    public float LungeDuration => lungeDuration;
    public float LungeSpeed => lungeSpeed;
    public float LungeHitRadius => lungeHitRadius;
    public float RecoverDuration => recoverDuration;

    public float IdealDistanceOffset => idealDistanceOffset;
    public AnimationCurve FleeByHealth => fleeByHealth;
    public float FleeDistance => fleeDistance;
    public float HoldScore => holdScore;
    public float GuardianProximityBonus => guardianProximityBonus;
    public float GuardianBonusRadius => guardianBonusRadius;
    public float UtilityDecisionInterval => utilityDecisionInterval;
    public float UtilityInertia => utilityInertia;

    public float AggroRadius => aggroRadius;
    public float LeashRadius => leashRadius;
    public float ReturnThreshold => returnThreshold;
    public float GuardDistance => guardDistance;
    public float EnrageSpeedMultiplier => enrageSpeedMultiplier;
    public float DamageAggroDuration => damageAggroDuration;

    // Dipakai threat estimation: DPS = damage / cooldown.
    public float DPS => damage / Mathf.Max(0.01f, attackCooldown);

    // EHP = MaxHP × (100 + Defense) / 100.
    public float EffectiveHealth => DamageCalculator.EffectiveHealth(maxHealth, defense);

#if UNITY_EDITOR
    // Dipakai menu Editor untuk membuat asset role contoh.
    public void EditorSetup(
        EnemyRole newRole,
        Color newTint,
        float newMaxHealth,
        float newMoveSpeed,
        float newVisionRange,
        float newVisionAngle,
        float newDamage,
        float newCooldown,
        float newAttackRange)
    {
        role = newRole;
        displayName = newRole.ToString();
        tint = newTint;
        maxHealth = newMaxHealth;
        moveSpeed = newMoveSpeed;
        visionRange = newVisionRange;
        visionAngle = newVisionAngle;
        damage = newDamage;
        attackCooldown = newCooldown;
        attackRange = newAttackRange;
    }
#endif
}
