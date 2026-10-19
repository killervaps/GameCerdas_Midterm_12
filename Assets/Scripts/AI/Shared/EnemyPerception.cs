using UnityEngine;

// Indra musuh. Dipanggil EnemyController setiap tick, hasilnya ditulis ke EnemyBlackboard.
// MC terlihat jika (dalam Vision Range dan dalam sudut pandang) atau (dalam Proximity Radius), dan tidak terhalang NavBlock.
[RequireComponent(typeof(EnemyBlackboard))]
public class EnemyPerception : MonoBehaviour
{
    [Header("Vision")]
    // Semua nilai ditimpa EnemyRoleData lewat EnemyController.
    [SerializeField] private float visionRange = 14f;
    [SerializeField, Range(0f, 360f)] private float visionAngle = 120f;
    [SerializeField] private float proximityRadius = 2f;
    [SerializeField] private float eyeHeight = 1.8f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private EnemyBlackboard blackboard;
    private PlayerHealth cachedPlayer;
    private PlayerMotor cachedMotor;
    private int obstacleMask;

    // Hasil sense terakhir, untuk gizmo dan label debug.
    public bool InRange { get; private set; }
    public bool InFieldOfView { get; private set; }
    public bool InProximity { get; private set; }
    public bool LineOfSightBlocked { get; private set; }

    public float VisionRange => visionRange;
    public float VisionAngle => visionAngle;
    public float ProximityRadius => proximityRadius;

    public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight * transform.lossyScale.y;

    private void Awake()
    {
        blackboard = GetComponent<EnemyBlackboard>();
        obstacleMask = GameLayers.ObstacleMask;
    }

    public void Configure(EnemyRoleData data)
    {
        visionRange = data.VisionRange;
        visionAngle = data.VisionAngle;
        proximityRadius = data.ProximityRadius;
        eyeHeight = data.EyeHeight;
    }

    public void Sense()
    {
        PlayerHealth player = PlayerHealth.Current;

        if (player != cachedPlayer)
        {
            cachedPlayer = player;
            cachedMotor = player != null ? player.GetComponent<PlayerMotor>() : null;
            blackboard.SetPlayer(player);
        }

        if (player == null || player.IsDead || !player.isActiveAndEnabled)
        {
            InRange = InFieldOfView = InProximity = false;
            LineOfSightBlocked = false;
            blackboard.SetVisibility(false, float.PositiveInfinity);
            return;
        }

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        InRange = distance <= visionRange;
        InProximity = distance <= proximityRadius;
        InFieldOfView = visionAngle >= 360f ||
                        (distance > 0.001f && Vector3.Angle(transform.forward, toPlayer) <= visionAngle * 0.5f);

        bool candidate = (InRange && InFieldOfView) || InProximity;

        // Raycast hanya jika lolos jarak dan sudut, supaya hemat.
        LineOfSightBlocked = candidate &&
                             Physics.Linecast(EyePosition, player.AimPoint, obstacleMask, QueryTriggerInteraction.Ignore);

        bool canSee = candidate && !LineOfSightBlocked;

        blackboard.SetVisibility(canSee, distance);

        if (canSee)
        {
            Vector3 velocity = cachedMotor != null ? cachedMotor.Velocity : Vector3.zero;
            velocity.y = 0f;
            blackboard.Observe(player.transform.position, velocity);
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Vector3 origin = transform.position + Vector3.up * 0.1f;

        bool seeing = Application.isPlaying && blackboard != null && blackboard.CanSeePlayer;

        // Cone pandang: merah saat melihat MC, kuning saat tidak.
        Gizmos.color = seeing ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(1f, 0.9f, 0.2f, 0.6f);
        DrawCone(origin, visionRange, visionAngle);

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
        DrawArc(origin, proximityRadius, 0f, 360f);

        if (!Application.isPlaying || cachedPlayer == null || cachedPlayer.IsDead)
            return;

        if (InRange && InFieldOfView || InProximity)
        {
            // Garis LOS: hijau jika terlihat, merah jika terhalang dinding.
            Gizmos.color = LineOfSightBlocked ? Color.red : Color.green;
            Gizmos.DrawLine(EyePosition, cachedPlayer.AimPoint);
        }
    }

    private void DrawCone(Vector3 origin, float range, float angle)
    {
        if (angle >= 360f)
        {
            DrawArc(origin, range, 0f, 360f);
            return;
        }

        float half = angle * 0.5f;
        float yaw = transform.eulerAngles.y;

        Vector3 left = Quaternion.Euler(0f, yaw - half, 0f) * Vector3.forward * range;
        Vector3 right = Quaternion.Euler(0f, yaw + half, 0f) * Vector3.forward * range;

        Gizmos.DrawLine(origin, origin + left);
        Gizmos.DrawLine(origin, origin + right);

        DrawArc(origin, range, yaw - half, angle);
    }

    private static void DrawArc(Vector3 origin, float radius, float startYaw, float sweep)
    {
        int segments = Mathf.Max(8, Mathf.CeilToInt(sweep / 10f));
        Vector3 previous = origin + Quaternion.Euler(0f, startYaw, 0f) * Vector3.forward * radius;

        for (int i = 1; i <= segments; i++)
        {
            float yaw = startYaw + sweep * i / segments;
            Vector3 point = origin + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * radius;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
