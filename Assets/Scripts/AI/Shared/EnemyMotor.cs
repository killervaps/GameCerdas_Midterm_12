using System;
using System.Collections;
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

    [Header("Jump Links")]
    // Nama area NavMesh untuk link lompat (Navigation > Areas). Hanya role di bawah yang boleh melewatinya,
    // role lain otomatis dikeluarkan dari Area Mask agent-nya.
    [SerializeField] private string jumpAreaName = "archer_jump";
    [SerializeField] private EnemyRole[] rolesAllowedToJump = { EnemyRole.Shooter };
    // Biaya area lompat saat jalan biasa. Tinggi supaya lompatan hanya dipakai jika diminta (mis. kabur dari menara).
    [SerializeField] private float jumpAreaCost = 20f;
    [SerializeField] private float jumpHeight = 1.2f;
    // Kecepatan sepanjang lintasan lompat. Durasi = panjang link / kecepatan, minimal 0,35 s.
    [SerializeField] private float jumpSpeed = 7f;
    // Jeda setelah mendarat, musuh diam dan tidak menyerang. Membuat lompatan bukan kabur gratis.
    [SerializeField] private float landingRecovery = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private NavMeshAgent agent;
    private bool hasFacing;
    private Vector3 facingDirection;
    private Vector3 lastDestination;
    private bool hasDestination;

    private int jumpArea = -1;
    private bool canJump;
    private Coroutine jumpRoutine;
    private Vector3 jumpEnd;
    private float recoverUntil;
    private bool stoppedForRecovery;

    // Perintah gerak yang datang saat sedang melompat, dijalankan setelah mendarat.
    private bool hasPendingMove;
    private bool hasPendingStop;
    private Vector3 pendingDestination;
    private float pendingSpeedMultiplier;
    private float pendingStoppingDistance;
    private bool pendingPreferJump;

    public NavMeshAgent Agent => agent;

    public float BaseSpeed => baseSpeed;

    public float SpeedMultiplier { get; private set; } = 1f;

    public bool IsMoving => agent != null && agent.enabled && agent.velocity.sqrMagnitude > 0.01f;

    public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;

    public bool IsReady => agent != null && agent.enabled && agent.isOnNavMesh;

    // Musuh ini boleh memakai link lompat dan area-nya sudah dibuat.
    public bool CanUseJumpLinks => canJump;

    public int JumpArea => jumpArea;

    public bool IsJumping => jumpRoutine != null;

    public bool IsRecoveringFromJump => Time.time < recoverUntil;

    // Sedang melompat atau baru mendarat. Brain sebaiknya tidak menyerang selama ini.
    public bool IsBusyJumping => IsJumping || IsRecoveringFromJump;

    public event Action JumpStarted;

    public event Action Landed;

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

    // Saat Start agent sudah menempel ke NavMesh, cost area lompat aman diatur.
    private void Start()
    {
        if (canJump && IsReady)
            agent.SetAreaCost(jumpArea, jumpAreaCost);
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

        ConfigureJumpArea(data.Role);
    }

    private void ConfigureJumpArea(EnemyRole role)
    {
        jumpArea = NavMesh.GetAreaFromName(jumpAreaName);
        canJump = false;

        if (jumpArea < 0)
            return;

        if (Array.IndexOf(rolesAllowedToJump, role) >= 0)
        {
            canJump = true;
            // Lompatan digerakkan sendiri (lintasan melengkung), bukan meluncur bawaan agent.
            agent.autoTraverseOffMeshLink = false;

            // SetAreaCost hanya boleh saat agent aktif dan sudah di NavMesh. Configure dipanggil dari Awake,
            // saat agent belum menempel, jadi cost diatur di Start atau langsung oleh MoveTo sebelum mencari path.
            if (IsReady)
                agent.SetAreaCost(jumpArea, jumpAreaCost);
        }
        else
        {
            agent.areaMask &= ~(1 << jumpArea);
        }
    }

    // speedMultiplier relatif ke Move Speed role, mis. 0,7 saat Search.
    // preferJumpLinks: link lompat dibuat murah untuk path ini, dipakai saat kabur dari tempat tinggi.
    public bool MoveTo(Vector3 destination, float speedMultiplier = 1f, float stoppingDistance = 0.1f, bool preferJumpLinks = false)
    {
        if (IsJumping)
        {
            hasPendingMove = true;
            hasPendingStop = false;
            pendingDestination = destination;
            pendingSpeedMultiplier = speedMultiplier;
            pendingStoppingDistance = stoppingDistance;
            pendingPreferJump = preferJumpLinks;
            return true;
        }

        if (!IsReady)
            return false;

        if (canJump)
            agent.SetAreaCost(jumpArea, preferJumpLinks ? 1f : jumpAreaCost);

        SpeedMultiplier = speedMultiplier;
        agent.speed = baseSpeed * speedMultiplier;
        agent.stoppingDistance = stoppingDistance;
        agent.isStopped = IsRecoveringFromJump;

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

        if (IsJumping)
        {
            hasPendingMove = false;
            hasPendingStop = true;
            return;
        }

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
            CancelJump();
            Stop();
            agent.enabled = false;
        }
    }

    private void Update()
    {
        if (canJump && jumpRoutine == null && agent.enabled && agent.isOnOffMeshLink)
            jumpRoutine = StartCoroutine(TraverseLink());

        if (stoppedForRecovery && !IsRecoveringFromJump)
        {
            stoppedForRecovery = false;

            if (hasDestination && IsReady)
                agent.isStopped = false;
        }

        if (!hasFacing)
            return;

        Quaternion target = Quaternion.LookRotation(facingDirection);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            target,
            turnSpeed * Time.deltaTime
        );
    }

    // Lintasan parabola dari ujung awal ke ujung akhir link, lalu agent dikembalikan ke NavMesh.
    private IEnumerator TraverseLink()
    {
        OffMeshLinkData data = agent.currentOffMeshLinkData;

        Vector3 start = transform.position;
        jumpEnd = data.endPos + Vector3.up * agent.baseOffset;

        Vector3 flat = jumpEnd - start;
        flat.y = 0f;

        if (!hasFacing && flat.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(flat);

        float duration = Mathf.Max(0.35f, Vector3.Distance(start, jumpEnd) / Mathf.Max(0.1f, jumpSpeed));
        float t = 0f;

        JumpStarted?.Invoke();

        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / duration);

            Vector3 position = Vector3.Lerp(start, jumpEnd, t);
            position.y += jumpHeight * 4f * t * (1f - t);
            transform.position = position;

            yield return null;
        }

        FinishJump();
    }

    private void FinishJump()
    {
        jumpRoutine = null;

        if (agent.enabled && agent.isOnOffMeshLink)
            agent.CompleteOffMeshLink();

        recoverUntil = Time.time + landingRecovery;

        if (landingRecovery > 0f && IsReady)
        {
            agent.isStopped = true;
            stoppedForRecovery = true;
        }

        if (hasPendingMove)
            MoveTo(pendingDestination, pendingSpeedMultiplier, pendingStoppingDistance, pendingPreferJump);
        else if (hasPendingStop)
            Stop();

        hasPendingMove = false;
        hasPendingStop = false;

        Landed?.Invoke();
    }

    // Lompatan dibatalkan (mis. mati di udara). Musuh langsung ditaruh di titik mendarat.
    private void CancelJump()
    {
        if (jumpRoutine == null)
            return;

        StopCoroutine(jumpRoutine);
        jumpRoutine = null;

        transform.position = jumpEnd;

        if (agent.enabled && agent.isOnOffMeshLink)
            agent.CompleteOffMeshLink();

        hasPendingMove = false;
        hasPendingStop = false;
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
