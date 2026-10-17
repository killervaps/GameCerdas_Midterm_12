using UnityEngine;

// Kumpulan sifat enemy yang tidak terikat pada otak tertentu. 
// EnemyBTController dan EnemyUtilityController membaca aset yang sama, lalu menerjemahkannya dengan caranya sendiri. 
// Dengan begitu "Aggressive" di BT dan "Aggressive" di Utility AI berasal dari satu definisi yang sama.
[CreateAssetMenu(fileName = "EnemyPersonality", menuName = "Enemy AI/Personality")]
public class EnemyPersonality : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string displayName;

    [Header("Perception")]
    [SerializeField] private float visionRange = 10f;
    [SerializeField, Range(0f, 360f)] private float visionAngle = 90f;

    [Header("Combat")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackExitRange = 2.75f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float attackDamage = 10f;

    [Header("Flee")]
    // Enemy mulai kabur saat rasio HP-nya turun sampai nilai ini.
    [SerializeField, Range(0.05f, 0.8f)] private float fleeStartHealthRatio = 0.3f;
    [SerializeField] private float safeDistance = 12f;

    [Header("Search")]
    [SerializeField] private float searchScanDuration = 4f;

    [Header("Movement")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float chaseSpeed = 4f;
    [SerializeField] private float fleeSpeed = 5f;

    // Nama untuk label di UI; memakai nama aset jika dikosongkan.
    public string DisplayName =>
        string.IsNullOrEmpty(displayName)
            ? name
            : displayName;

    public float VisionRange => visionRange;
    public float VisionAngle => visionAngle;

    public float AttackRange => attackRange;
    public float AttackExitRange => attackExitRange;
    public float AttackCooldown => attackCooldown;
    public float AttackDamage => attackDamage;

    public float FleeStartHealthRatio => fleeStartHealthRatio;
    public float SafeDistance => safeDistance;

    public float SearchScanDuration => searchScanDuration;

    public float PatrolSpeed => patrolSpeed;
    public float ChaseSpeed => chaseSpeed;
    public float FleeSpeed => fleeSpeed;
}
