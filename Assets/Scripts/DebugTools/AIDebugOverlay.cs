using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// AI Debug Mode untuk Game view (juga di build), tanpa bergantung pada Gizmo editor.
// F1 menyalakan dan mematikan semua visualisasi (overlay dunia, label musuh, panel). F2 slow motion untuk presentasi.
// Overlay dunia digambar dengan GL setelah kamera selesai render:
//   Musuh   cone pandang (kuning, merah saat melihat MC), radius proximity, garis LOS (hijau / merah terhalang),
//           LKP (silang oranye, pudar mendekati lupa), jalur NavMesh,
//           Charger lingkaran lungeRange (merah saat windup) dan garis lunge,
//           Shooter ring jarak ideal di sekitar MC, titik reposition, arah kabur,
//           Guardian ring aggro dan leash di sekitar anchor, tali ke anchor, titik interpose, lingkaran marah.
//   Tactical slot per musuh (warna role) dan garis musuh ke slot, garis kuning pemegang token ke MC.
//   MC      Range target (cyan), garis ke target (hijau auto, magenta lock), radius Aura Burst.
public class AIDebugOverlay : MonoBehaviour
{
    private static readonly Color ConeIdle = new Color(1f, 0.9f, 0.2f, 0.6f);
    private static readonly Color ConeSeeing = new Color(1f, 0.25f, 0.25f, 0.9f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.25f);
    private static readonly Color Memory = new Color(1f, 0.55f, 0f, 1f);
    private static readonly Color TokenColor = new Color(1f, 0.93f, 0.3f, 1f);

    [Header("Input")]
    [SerializeField] private string toggleActionName = "Player/ToggleAIDebug";
    [SerializeField] private bool slowMotionHotkey = true;
    [SerializeField, Range(0.05f, 1f)] private float slowMotionScale = 0.3f;

    [Header("Start")]
    // Mati = permainan mulai bersih, tekan F1 untuk membuka debug.
    [SerializeField] private bool startEnabled = false;

    [Header("Layers")]
    [SerializeField] private bool drawPerception = true;
    [SerializeField] private bool drawMemory = true;
    [SerializeField] private bool drawPaths = true;
    [SerializeField] private bool drawRoleShapes = true;
    [SerializeField] private bool drawTactical = true;
    [SerializeField] private bool drawPlayer = true;
    [SerializeField] private bool showEnemyLabels = true;

    [Header("Style")]
    // Garis tetap terlihat walau tertutup dinding.
    [SerializeField] private bool drawThroughWalls = true;
    [SerializeField] private float groundOffset = 0.05f;

    private readonly Vector3[] pathBuffer = new Vector3[32];

    private Material material;
    private InputAction toggleAction;
    private bool slowMotion;
    private float normalTimeScale = 1f;

    public bool IsSlowMotion => slowMotion;

    private void Awake()
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");

        if (shader == null)
        {
            Debug.LogError("AIDebugOverlay: shader Hidden/Internal-Colored tidak ditemukan, overlay dunia dimatikan.", this);
            return;
        }

        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)(drawThroughWalls ? CompareFunction.Always : CompareFunction.LessEqual));
    }

    private void OnEnable()
    {
        AIDebugSettings.Enabled = startEnabled;
        AIDebugSettings.LabelsAllowed = showEnemyLabels;

        toggleAction = InputSystem.actions != null ? InputSystem.actions.FindAction(toggleActionName) : null;
        toggleAction?.Enable();

        if (toggleAction == null)
            Debug.LogWarning("AIDebugOverlay: action '" + toggleActionName + "' tidak ditemukan, F1 tidak aktif.", this);

        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;

        if (slowMotion)
            SetSlowMotion(false);
    }

    private void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            AIDebugSettings.LabelsAllowed = showEnemyLabels;
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.WasPressedThisFrame())
        {
            AIDebugSettings.Enabled = !AIDebugSettings.Enabled;
            Debug.Log("AI Debug Mode " + (AIDebugSettings.Enabled ? "ON" : "OFF"), this);
        }

        if (slowMotionHotkey && Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            SetSlowMotion(!slowMotion);
    }

    private void SetSlowMotion(bool value)
    {
        if (value)
            normalTimeScale = Time.timeScale;

        slowMotion = value;
        Time.timeScale = value ? slowMotionScale : normalTimeScale;
    }

    // ---------- Render ----------

    private void HandleEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (!AIDebugSettings.Enabled || material == null || cam.cameraType != CameraType.Game)
            return;

        GL.PushMatrix();
        material.SetPass(0);
        GL.LoadProjectionMatrix(cam.projectionMatrix);
        GL.modelview = cam.worldToCameraMatrix;
        GL.Begin(GL.LINES);

        if (drawPlayer)
            DrawPlayer();

        DrawEnemies();

        if (drawTactical)
            DrawTactical();

        GL.End();
        GL.PopMatrix();
    }

    private void DrawPlayer()
    {
        PlayerHealth player = PlayerHealth.Current;

        if (player == null || player.IsDead)
            return;

        Vector3 feet = Ground(player.transform.position);

        TargetSelector selector = player.GetComponent<TargetSelector>();

        if (selector != null)
        {
            Circle(feet, selector.Range, new Color(0.3f, 0.8f, 1f, 0.6f));

            EnemyHealth target = selector.CurrentTarget;

            if (target != null && !target.IsDead)
            {
                Color color = selector.IsLocked ? Color.magenta : Color.green;
                Line(player.AimPoint, target.AimPoint, color);
                Circle(Ground(target.transform.position), selector.IsLocked ? 0.5f : 0.35f, color, 20);
            }
        }

        AuraBurst burst = player.GetComponent<AuraBurst>();

        if (burst != null)
            Circle(feet, burst.Radius, burst.IsReady ? new Color(1f, 0.9f, 0.2f, 0.5f) : new Color(0.5f, 0.5f, 0.5f, 0.3f));
    }

    private void DrawEnemies()
    {
        PlayerHealth player = PlayerHealth.Current;

        foreach (EnemyController enemy in EnemyController.Active)
        {
            if (enemy == null || enemy.Health.IsDead || enemy.RoleData == null)
                continue;

            if (drawPerception)
                DrawPerception(enemy, player);

            if (drawMemory)
                DrawMemory(enemy);

            if (drawPaths)
                DrawPath(enemy);

            if (drawRoleShapes)
                DrawRoleShapes(enemy, player);
        }
    }

    private void DrawPerception(EnemyController enemy, PlayerHealth player)
    {
        EnemyPerception perception = enemy.Perception;
        Vector3 origin = Ground(enemy.transform.position);

        Color cone = enemy.Blackboard.CanSeePlayer ? ConeSeeing : ConeIdle;

        Cone(origin, enemy.transform.eulerAngles.y, perception.VisionAngle, perception.VisionRange, cone);
        Circle(origin, perception.ProximityRadius, new Color(cone.r, cone.g, cone.b, 0.3f), 20);

        if (player == null || player.IsDead)
            return;

        bool candidate = (perception.InRange && perception.InFieldOfView) || perception.InProximity;

        if (candidate)
            Line(perception.EyePosition, player.AimPoint, perception.LineOfSightBlocked ? Color.red : Color.green);
    }

    private void DrawMemory(EnemyController enemy)
    {
        EnemyBlackboard bb = enemy.Blackboard;

        if (!bb.HasLastKnownPosition || bb.CanSeePlayer)
            return;

        float fade = bb.MemoryDuration > 0f ? bb.MemoryRemaining / bb.MemoryDuration : 1f;
        Color color = new Color(Memory.r, Memory.g, Memory.b, 0.25f + 0.75f * fade);

        Vector3 lkp = Ground(bb.LastKnownPosition);

        Cross(lkp, 0.3f, color);
        Circle(lkp, 0.3f * Mathf.Max(0.2f, fade), color, 16);
        Line(Ground(enemy.transform.position), lkp, color);
    }

    private void DrawPath(EnemyController enemy)
    {
        UnityEngine.AI.NavMeshAgent agent = enemy.Motor.Agent;

        if (agent == null || !agent.enabled || !agent.hasPath)
            return;

        int count = agent.path.GetCornersNonAlloc(pathBuffer);

        for (int i = 1; i < count; i++)
            Line(Ground(pathBuffer[i - 1]), Ground(pathBuffer[i]), Faint);
    }

    private void DrawRoleShapes(EnemyController enemy, PlayerHealth player)
    {
        Vector3 origin = Ground(enemy.transform.position);
        EnemyRoleData data = enemy.RoleData;

        switch (enemy.Brain)
        {
            case ChargerBrain charger:
                Circle(origin, data.LungeRange, charger.IsWindingUp ? Color.red : new Color(1f, 0.45f, 0.2f, 0.5f));

                if (charger.IsLunging)
                {
                    Vector3 start = Ground(charger.LungeStart);
                    Line(start, start + charger.LungeDirection * data.LungeSpeed * data.LungeDuration, charger.LungeHit ? Color.green : Color.red);
                }
                break;

            case ShooterBrain shooter:
                if (player != null && enemy.Blackboard.IsAware)
                    Circle(Ground(player.transform.position), shooter.IdealDistance, new Color(0.3f, 0.6f, 1f, 0.45f));

                if (shooter.HasRepositionPoint)
                    Cross(Ground(shooter.RepositionPoint), 0.25f, Color.cyan);

                if (shooter.CurrentAction == EnemyAction.Flee)
                    Line(origin, Ground(shooter.FleePoint), Color.magenta);
                break;

            case GuardianBrain guardian:
                Vector3 anchor = Ground(guardian.AnchorPosition);

                if (guardian.IsEnraged)
                {
                    Circle(origin, 0.6f, Color.red, 20);
                    break;
                }

                Circle(anchor, guardian.AggroRadius, new Color(1f, 0.6f, 0.2f, 0.8f));
                Circle(anchor, guardian.LeashRadius, guardian.IsReturning ? Color.red : new Color(0.3f, 0.6f, 1f, 0.8f));
                Line(origin, anchor, guardian.IsBodyguard ? Color.green : Color.white);

                if (guardian.HasInterposePoint)
                    Cross(Ground(guardian.InterposePoint), 0.25f, Color.yellow);
                break;
        }
    }

    private void DrawTactical()
    {
        TacticalCoordinator coordinator = TacticalCoordinator.Current;

        if (coordinator == null)
            return;

        foreach (SlotManager.Assignment assignment in coordinator.Slots.Assignments)
        {
            if (assignment.enemy == null || assignment.enemy.Health.IsDead)
                continue;

            Color color = RoleColors.Get(assignment.enemy.Role);
            Vector3 slot = Ground(assignment.position);

            Circle(slot, 0.15f, color, 12);
            Cross(slot, 0.15f, color);
            Line(Ground(assignment.enemy.transform.position), slot, color);
        }

        PlayerHealth player = coordinator.Group.Player;

        foreach (AttackTokenManager.Holder holder in coordinator.Tokens.Holders)
        {
            if (holder.enemy == null || holder.enemy.Health.IsDead)
                continue;

            Vector3 head = holder.enemy.Perception.EyePosition + Vector3.up * 0.15f;

            Circle(head, 0.15f, TokenColor, 12);

            if (player != null)
                Line(head, player.AimPoint, TokenColor);
        }
    }

    // ---------- Primitif ----------

    private Vector3 Ground(Vector3 position)
    {
        return position + Vector3.up * groundOffset;
    }

    private static void Line(Vector3 a, Vector3 b, Color color)
    {
        GL.Color(color);
        GL.Vertex(a);
        GL.Vertex(b);
    }

    private static void Circle(Vector3 center, float radius, Color color, int segments = 40)
    {
        Arc(center, radius, 0f, 360f, color, segments);
    }

    private static void Arc(Vector3 center, float radius, float startYaw, float sweep, Color color, int segments = 40)
    {
        GL.Color(color);

        Vector3 previous = center + Quaternion.Euler(0f, startYaw, 0f) * Vector3.forward * radius;

        for (int i = 1; i <= segments; i++)
        {
            Vector3 point = center + Quaternion.Euler(0f, startYaw + sweep * i / segments, 0f) * Vector3.forward * radius;
            GL.Vertex(previous);
            GL.Vertex(point);
            previous = point;
        }
    }

    private static void Cone(Vector3 origin, float yaw, float angle, float range, Color color)
    {
        if (angle >= 360f)
        {
            Circle(origin, range, color);
            return;
        }

        float half = angle * 0.5f;

        Line(origin, origin + Quaternion.Euler(0f, yaw - half, 0f) * Vector3.forward * range, color);
        Line(origin, origin + Quaternion.Euler(0f, yaw + half, 0f) * Vector3.forward * range, color);
        Arc(origin, range, yaw - half, angle, color, Mathf.Max(8, Mathf.CeilToInt(angle / 10f)));
    }

    private static void Cross(Vector3 center, float size, Color color)
    {
        Line(center + new Vector3(-size, 0f, -size), center + new Vector3(size, 0f, size), color);
        Line(center + new Vector3(-size, 0f, size), center + new Vector3(size, 0f, -size), color);
    }
}
