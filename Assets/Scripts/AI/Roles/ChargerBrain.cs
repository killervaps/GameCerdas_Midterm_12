using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Charger (Tung Tung Tung Sahur). Behavior Tree, selector prioritas yang dicek dari atas setiap tick:
//   1. Mati                                   -> Die
//   2. Aksi terkunci sedang jalan             -> lanjutkan Windup / Lunge / Recover
//   3. Jarak <= lungeRange, LOS, dapat token  -> mulai Windup
//   4. MC terlihat                            -> Chase
//   5. Punya LKP                              -> Search
//   6. Lainnya                                -> Idle
// Windup, Lunge, dan Recover adalah aksi terkunci. Penjaga di prioritas 2 mencegah cabang lain menyela,
// karena framework BT ini tidak memberi tahu node Running yang tersela.
public class ChargerBrain : EnemyBrain
{
    private enum LockedPhase
    {
        None,
        Windup,
        Lunge,
        Recover
    }

    [Header("Windup Signal")]
    // Warna kedip saat isyarat "tung tung tung". Nanti diganti SFX dan animasi lewat event WindupBeat.
    [SerializeField] private Color windupFlashColor = Color.white;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private readonly Collider[] hitBuffer = new Collider[4];

    private BTNode root;
    private SearchRoutine search;
    private RendererTint tint;
    private int playerMask;

    private LockedPhase phase = LockedPhase.None;
    private float phaseStartTime;
    private int beatsFired;
    private Vector3 lungeDirection;
    private Vector3 lungeStart;
    private bool lungeHit;

    public override string BrainName => "BT/Charger";

    public override bool IsScanning => search != null && search.CurrentPhase == SearchRoutine.Phase.Scan;

    public bool IsLocked => phase != LockedPhase.None;

    // Nomor ketukan (1, 2, 3) selama windup. Hook untuk SFX "tung" dan animasi.
    public event Action<int> WindupBeat;

    public override string DebugDetail
    {
        get
        {
            if (phase != LockedPhase.None)
                return (Time.time - phaseStartTime).ToString("0.00") + "s" + (Controller.HasAttackToken ? " [token]" : "");

            if (search != null && search.IsActive)
                return search.CurrentPhase == SearchRoutine.Phase.Scan ? "scan" : "ke LKP";

            return string.Empty;
        }
    }

    public override void Initialize(EnemyController controller)
    {
        base.Initialize(controller);

        search = new SearchRoutine(Motor, transform);
        tint = GetComponent<RendererTint>();
        playerMask = GameLayers.PlayerMask;

        root = new SelectorNode(new List<BTNode>
        {
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => Controller.Health.IsDead),
                new ActionNode(Die)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => IsLocked),
                new ActionNode(ContinueLocked)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(CanStartLunge),
                new ActionNode(StartWindup)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => Blackboard.CanSeePlayer),
                new ActionNode(Chase)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => Blackboard.HasLastKnownPosition),
                new ActionNode(Search)
            }),
            new ActionNode(Idle)
        });
    }

    public override void Tick(float deltaTime)
    {
        root.Tick();
    }

    public override void OnDied()
    {
        if (phase == LockedPhase.Windup)
            RestoreTint();

        phase = LockedPhase.None;
        search.Cancel();

        base.OnDied();
    }

    // ---------- Kondisi ----------

    private bool CanStartLunge()
    {
        if (!Blackboard.CanSeePlayer || Blackboard.DistanceToPlayer > Data.LungeRange)
            return false;

        // Token diminta paling akhir. Tanpa token, Charger jatuh ke cabang Chase (M6: menunggu di slot).
        return Controller.RequestAttackToken();
    }

    // ---------- Aksi BT ----------

    private NodeState Die()
    {
        SetAction(EnemyAction.Dead);
        return NodeState.Success;
    }

    private NodeState ContinueLocked()
    {
        // Gerak dan pergantian fase berjalan per frame di Update. Di sini hanya menahan selector.
        return NodeState.Running;
    }

    private NodeState StartWindup()
    {
        search.Cancel();

        Motor.Stop();
        Motor.FacePosition(Blackboard.Player.transform.position);

        EnterPhase(LockedPhase.Windup);
        beatsFired = 0;

        return NodeState.Running;
    }

    private NodeState Chase()
    {
        search.Cancel();
        SetAction(EnemyAction.Chase);

        Motor.ClearFacing();
        // Berhenti sedikit di dalam lungeRange supaya cabang lunge bisa terpicu.
        Motor.MoveTo(Blackboard.Player.transform.position, 1f, Data.LungeRange * 0.7f);

        return NodeState.Running;
    }

    private NodeState Search()
    {
        if (!search.IsActive || (search.Target - Blackboard.LastKnownPosition).sqrMagnitude > 4f)
        {
            if (!search.Begin(Blackboard.LastKnownPosition, Data))
            {
                Blackboard.ClearLastKnownPosition();
                return NodeState.Failure;
            }
        }

        SetAction(EnemyAction.Search);

        NodeState state = search.Tick();

        if (state != NodeState.Running)
            Blackboard.ClearLastKnownPosition();

        return state;
    }

    private NodeState Idle()
    {
        SetAction(EnemyAction.Idle);

        Motor.Stop();
        Motor.ClearFacing();

        return NodeState.Success;
    }

    // ---------- Fase terkunci, per frame ----------

    private void Update()
    {
        if (phase == LockedPhase.None || Controller == null || Controller.Health.IsDead)
            return;

        float elapsed = Time.time - phaseStartTime;

        switch (phase)
        {
            case LockedPhase.Windup:
                UpdateWindup(elapsed);
                break;
            case LockedPhase.Lunge:
                UpdateLunge(elapsed);
                break;
            case LockedPhase.Recover:
                if (elapsed >= Data.RecoverDuration)
                    FinishRecover();
                break;
        }
    }

    private void UpdateWindup(float elapsed)
    {
        PlayerHealth player = Blackboard.Player;

        // Selama windup masih menoleh mengikuti MC, arah baru dikunci saat windup selesai.
        if (player != null)
            Motor.FacePosition(player.transform.position);

        float beatLength = Data.WindupDuration / Data.WindupBeats;
        int beat = Mathf.Min(Data.WindupBeats, Mathf.FloorToInt(elapsed / beatLength) + 1);

        if (beat > beatsFired)
        {
            beatsFired = beat;
            WindupBeat?.Invoke(beat);
        }

        // Kedip di paruh pertama tiap ketukan.
        bool flashOn = elapsed % beatLength < beatLength * 0.5f;

        if (tint != null)
            tint.SetColor(flashOn ? windupFlashColor : Data.Tint);

        if (elapsed >= Data.WindupDuration)
            StartLunge();
    }

    private void StartLunge()
    {
        RestoreTint();

        PlayerHealth player = Blackboard.Player;
        Vector3 direction = player != null ? player.transform.position - transform.position : transform.forward;
        direction.y = 0f;

        lungeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        lungeStart = transform.position;
        lungeHit = false;

        transform.rotation = Quaternion.LookRotation(lungeDirection);

        // Lunge lurus tanpa pathfinding, agent dimatikan sementara.
        Motor.SetAgentEnabled(false);

        EnterPhase(LockedPhase.Lunge);
    }

    private void UpdateLunge(float elapsed)
    {
        Vector3 position = transform.position;
        Vector3 next = position + lungeDirection * Data.LungeSpeed * Time.deltaTime;

        bool blocked = NavMesh.Raycast(position, next, out NavMeshHit navHit, NavMesh.AllAreas);

        // NavMesh tidak menutupi dinding dan obstacle, jadi tepi NavMesh = menabrak NavBlock.
        transform.position = blocked ? navHit.position : next;

        if (!lungeHit)
            CheckLungeHit();

        if (blocked || elapsed >= Data.LungeDuration)
            EndLunge();
    }

    private void CheckLungeHit()
    {
        Vector3 center = transform.position + Vector3.up * Data.LungeHitRadius * 0.5f;

        int count = Physics.OverlapSphereNonAlloc(center, Data.LungeHitRadius, hitBuffer, playerMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            PlayerHealth player = hitBuffer[i].GetComponentInParent<PlayerHealth>();

            if (player == null || player.IsDead)
                continue;

            // Hanya sekali per lunge.
            lungeHit = true;
            player.TakeDamage(new DamageInfo(Data.Damage, gameObject, player.AimPoint, DamageKind.Lunge));
            return;
        }
    }

    private void EndLunge()
    {
        Motor.SetAgentEnabled(true);
        Motor.Stop();
        Motor.ClearFacing();

        EnterPhase(LockedPhase.Recover);
    }

    private void FinishRecover()
    {
        phase = LockedPhase.None;

        // Token dilepas di akhir Recover, memberi giliran ke musuh lain.
        Controller.ReleaseAttackToken();
    }

    private void EnterPhase(LockedPhase next)
    {
        phase = next;
        phaseStartTime = Time.time;

        switch (next)
        {
            case LockedPhase.Windup: SetAction(EnemyAction.Windup); break;
            case LockedPhase.Lunge: SetAction(EnemyAction.Lunge); break;
            case LockedPhase.Recover: SetAction(EnemyAction.Recover); break;
        }
    }

    private void RestoreTint()
    {
        if (tint != null && Data != null)
            tint.SetColor(Data.Tint);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        EnemyController controller = Controller != null ? Controller : GetComponent<EnemyController>();

        if (controller == null || controller.RoleData == null)
            return;

        EnemyRoleData data = controller.RoleData;
        Vector3 origin = transform.position + Vector3.up * 0.1f;

        // Lingkaran lungeRange, merah saat windup.
        Gizmos.color = phase == LockedPhase.Windup ? Color.red : new Color(1f, 0.45f, 0.2f, 0.5f);
        GizmoShapes.Circle(origin, data.LungeRange);

        if (phase == LockedPhase.Lunge || phase == LockedPhase.Recover)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(lungeStart + Vector3.up * 0.1f, lungeStart + Vector3.up * 0.1f + lungeDirection * data.LungeSpeed * data.LungeDuration);
        }

        if (phase == LockedPhase.Lunge)
        {
            Gizmos.color = lungeHit ? Color.green : new Color(1f, 0f, 0f, 0.6f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * data.LungeHitRadius * 0.5f, data.LungeHitRadius);
        }
    }
}
