using UnityEngine;
using UnityEngine.AI;

// Pembungkus NavMeshAgent untuk semua brain. Brain cukup memanggil MoveTo, Stop, dan Face,
// tanpa mengurus detail agent. Rotasi menghadap target dihaluskan setiap frame walaupun brain hanya tick 0,1-0,2 s.
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyMotor : MonoBehaviour
{
    [Header("Movement")]
    // Ditimpa EnemyRoleData lewat EnemyController.
    [SerializeField] private float baseSpeed = 4.5f;
    [SerializeField] private float turnSpeed = 540f;
    // Toleransi tambahan di atas stopping distance untuk dianggap sudah sampai.
    [SerializeField] private float arriveTolerance = 0.3f;
    // Tujuan baru diabaikan jika bedanya kurang dari ini, supaya path tidak dihitung ulang setiap tick.
    [SerializeField] private float repathThreshold = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private NavMeshAgent agent;
    private bool hasFacing;
    private Vector3 facingDirection;
    private Vector3 lastDestination;
    private bool hasDestination;

    public NavMeshAgent Agent => agent;

    public float BaseSpeed => baseSpeed;

    public float SpeedMultiplier { get; private set; } = 1f;

    public bool IsMoving => agent != null && agent.enabled && agent.velocity.sqrMagnitude > 0.01f;

    public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;

    public bool IsReady => agent != null && agent.enabled && agent.isOnNavMesh;

    public bool HasArrived
    {
        get
        {
            if (!IsReady)
                return true;

            if (agent.pathPending)
                return false;

            return !hasDestination || agent.remainingDistance <= agent.stoppingDistance + arriveTolerance;
        }
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public void Configure(EnemyRoleData data)
    {
        // Bisa dipanggil EnemyController sebelum Awake motor berjalan.
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        baseSpeed = data.MoveSpeed;
        turnSpeed = data.TurnSpeed;

        agent.speed = baseSpeed;
        agent.angularSpeed = turnSpeed;
        agent.acceleration = data.Acceleration;
    }

    // speedMultiplier relatif ke Move Speed role, mis. 0,7 saat Search.
    public bool MoveTo(Vector3 destination, float speedMultiplier = 1f, float stoppingDistance = 0.1f)
    {
        if (!IsReady)
            return false;

        SpeedMultiplier = speedMultiplier;
        agent.speed = baseSpeed * speedMultiplier;
        agent.stoppingDistance = stoppingDistance;
        agent.isStopped = false;

        if (!hasFacing)
            agent.updateRotation = true;

        if (hasDestination && (destination - lastDestination).sqrMagnitude < repathThreshold * repathThreshold)
            return true;

        hasDestination = agent.SetDestination(destination);
        lastDestination = destination;

        return hasDestination;
    }

    public void Stop()
    {
        hasDestination = false;

        if (!IsReady)
            return;

        agent.isStopped = true;
        agent.ResetPath();
    }

    // Menghadap posisi tertentu. Selama aktif, agent tidak memutar badan sendiri (berguna untuk mundur sambil menembak).
    public void FacePosition(Vector3 position)
    {
        FaceDirection(position - transform.position);
    }

    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        hasFacing = true;
        facingDirection = direction.normalized;

        if (agent != null)
            agent.updateRotation = false;
    }

    public void ClearFacing()
    {
        hasFacing = false;

        if (agent != null)
            agent.updateRotation = true;
    }

    // Untuk gerakan khusus di luar NavMesh, mis. lunge Charger. Posisi disinkronkan saat agent hidup lagi.
    public void SetAgentEnabled(bool value)
    {
        if (agent == null || agent.enabled == value)
            return;

        if (value)
        {
            agent.enabled = true;

            if (agent.isOnNavMesh)
                agent.Warp(transform.position);
        }
        else
        {
            Stop();
            agent.enabled = false;
        }
    }

    private void Update()
    {
        if (!hasFacing)
            return;

        Quaternion target = Quaternion.LookRotation(facingDirection);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            target,
            turnSpeed * Time.deltaTime
        );
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying || !IsReady || !agent.hasPath)
            return;

        // Jalur NavMesh: garis putih tipis sampai tujuan.
        Vector3[] corners = agent.path.corners;

        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);

        for (int i = 1; i < corners.Length; i++)
            Gizmos.DrawLine(corners[i - 1] + Vector3.up * 0.05f, corners[i] + Vector3.up * 0.05f);

        Gizmos.DrawWireCube(agent.destination + Vector3.up * 0.05f, Vector3.one * 0.25f);
    }
}
