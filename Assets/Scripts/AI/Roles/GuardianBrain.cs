using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Guardian. Tank lambat yang diikat ke anchor dengan leash. Behavior Tree, selector prioritas:
//   1. Mati                                  -> Die
//   2. Anchor (Shooter) mati                 -> Enraged, lepas anchor dan kejar MC lebih cepat
//   3. Sedang pulang atau keluar leash       -> Return ke anchor sampai jarak <= returnThreshold (hysteresis)
//   4. MC di aggro radius dan dapat token    -> dekati lalu serang
//   5. MC di aggro radius                    -> hadang di titik interpose
//   6. Lainnya                               -> Guard, jaga anchor atau berdiri di titik interpose
// Anchor default adalah titik diam di posisi spawn. TacticalCoordinator (M6) bisa memasang Shooter sebagai anchor (mode bodyguard).
public class GuardianBrain : EnemyBrain
{
    [Header("Anchor")]
    // Opsional. Kosongkan untuk memakai posisi spawn sebagai anchor diam.
    [SerializeField] private Transform initialAnchor;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private BTNode root;
    private Transform staticAnchor;
    private Transform anchor;
    private EnemyHealth anchorHealth;
    private bool returning;
    private bool enraged;
    private bool engaging;
    private float nextAttackTime;
    // Ayunan yang sudah dimulai tapi belum mendarat. Damage diberikan saat swingHitTime, bukan saat animasi dimulai.
    private bool swingPending;
    private float swingHitTime;
    private Vector3 interposePoint;
    private bool hasInterposePoint;

    public override string BrainName => "BT/Guardian";

    public Transform Anchor => anchor;

    // True jika anchor adalah musuh lain (mode bodyguard), bukan titik diam.
    public bool IsBodyguard => anchorHealth != null;

    public bool IsEnraged => enraged;

    public bool IsSwinging => swingPending;

    public override string DebugDetail
    {
        get
        {
            if (swingPending)
                return "<color=#ffaa33>ayun " + Mathf.Max(0f, swingHitTime - Time.time).ToString("0.00") + "s</color>" +
                       (enraged ? " <color=#ff5555>MARAH</color>" : string.Empty);

            if (enraged)
                return "<color=#ff5555>MARAH</color>";

            if (anchor == null)
                return string.Empty;

            string anchorName = IsBodyguard ? anchor.name : "titik";
            string text = "anchor " + anchorName + "  leash " + DistanceToAnchor().ToString("0.0") + "/" + LeashRadius.ToString("0.#");

            if (Reactions.IsDurable(Build))
                text += " longgar";

            if (Reactions.IsHighOffense(Build))
                text += " maju";

            if (returning)
                text += " pulang";

            return text;
        }
    }

    public override void Initialize(EnemyController controller)
    {
        base.Initialize(controller);

        staticAnchor = new GameObject(name + "_Anchor").transform;
        staticAnchor.position = transform.position;

        if (initialAnchor != null)
            SetAnchor(initialAnchor);
        else
            ClearAnchor();

        root = new SelectorNode(new List<BTNode>
        {
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => Controller.Health.IsDead),
                new ActionNode(Die)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(ShouldEnrage),
                new ActionNode(Enraged)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(ShouldReturn),
                new ActionNode(ReturnToAnchor)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => PlayerInAggro() && Controller.RequestAttackToken()),
                new ActionNode(Engage)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(PlayerInAggro),
                new ActionNode(Interpose)
            }),
            new ActionNode(Guard)
        });
    }

    // Dipanggil TacticalCoordinator untuk mode bodyguard. Anchor berupa musuh lain akan memicu mode marah jika mati.
    public void SetAnchor(Transform newAnchor)
    {
        if (newAnchor == null)
        {
            ClearAnchor();
            return;
        }

        anchor = newAnchor;
        anchorHealth = newAnchor.GetComponentInParent<EnemyHealth>();
        enraged = false;
        returning = false;
    }

    public void ClearAnchor()
    {
        anchor = staticAnchor;
        anchorHealth = null;
    }

    public override void Tick(float deltaTime)
    {
        // Keluar dari cabang serang, token dilepas untuk musuh lain.
        bool wasEngaging = engaging;
        engaging = false;

        root.Tick();

        if (wasEngaging && !engaging && !enraged && Controller.HasAttackToken)
            Controller.ReleaseAttackToken();
    }

    private void OnDestroy()
    {
        if (staticAnchor != null)
            Destroy(staticAnchor.gameObject);
    }

    public override void OnDied()
    {
        swingPending = false;
        base.OnDied();
    }

    // Pukulan diselesaikan per frame supaya tepat waktu, walau brain hanya tick 0,1-0,2 s.
    private void Update()
    {
        if (swingPending && Time.time >= swingHitTime)
            ResolveSwing();
    }

    // ---------- Kondisi ----------

    private bool ShouldEnrage()
    {
        if (enraged)
            return true;

        if (!IsBodyguard)
            return false;

        bool anchorGone = anchor == null || !anchor.gameObject.activeInHierarchy || anchorHealth == null || anchorHealth.IsDead;

        if (anchorGone)
        {
            // TODO(desain): fallback saat Shooter mati masih usulan (alternatif: kembali ke anchor diam).
            enraged = true;
            anchor = staticAnchor;
            anchorHealth = null;
            returning = false;

            if (Controller.LogStateChanges)
                Debug.Log(name + ": anchor mati, mode marah", this);

            SfxManager.Play(SfxCue.GuardianEnrage);
        }

        return enraged;
    }

    private bool ShouldReturn()
    {
        float distance = DistanceToAnchor();

        if (returning)
        {
            if (distance <= Data.ReturnThreshold)
                returning = false;

            return returning;
        }

        if (distance > LeashRadius)
            returning = true;

        return returning;
    }

    private bool PlayerInAggro()
    {
        PlayerHealth player = Blackboard.Player;

        if (player == null || player.IsDead)
            return false;

        float playerToAnchor = FlatDistance(player.transform.position, AnchorPosition);

        if (Blackboard.CanSeePlayer && playerToAnchor <= AggroRadius)
            return true;

        // Diserang MC dari luar aggro radius tetapi masih dalam leash, Guardian boleh mengejar sebentar.
        return Blackboard.WasDamagedWithin(Data.DamageAggroDuration) &&
               Blackboard.HasLastKnownPosition &&
               FlatDistance(Blackboard.LastKnownPosition, AnchorPosition) <= LeashRadius;
    }

    // ---------- Aksi ----------

    private NodeState Die()
    {
        SetAction(EnemyAction.Dead);
        return NodeState.Success;
    }

    private NodeState Enraged()
    {
        SetAction(EnemyAction.Enraged);

        // Token dipakai sebisanya, tapi mode marah tetap mengejar walaupun tidak dapat token.
        Controller.RequestAttackToken();

        if (Blackboard.CanSeePlayer)
        {
            PlayerHealth player = Blackboard.Player;
            MeleeOrApproach(player, Data.EnrageSpeedMultiplier);
        }
        else if (Blackboard.HasLastKnownPosition)
        {
            Motor.ClearFacing();
            Motor.MoveTo(Blackboard.LastKnownPosition, Data.EnrageSpeedMultiplier, 0.3f);
        }
        else
        {
            Motor.Stop();
        }

        return NodeState.Running;
    }

    private NodeState ReturnToAnchor()
    {
        SetAction(EnemyAction.Return);

        Motor.ClearFacing();
        Motor.MoveTo(AnchorPosition, 1f, Data.ReturnThreshold * 0.5f);

        return NodeState.Running;
    }

    private NodeState Engage()
    {
        engaging = true;
        SetAction(EnemyAction.Attack);

        MeleeOrApproach(Blackboard.Player, 1f);

        return NodeState.Running;
    }

    private NodeState Interpose()
    {
        SetAction(EnemyAction.Guard);

        HoldInterposePoint(Blackboard.Player.transform.position);

        return NodeState.Running;
    }

    private NodeState Guard()
    {
        SetAction(EnemyAction.Guard);

        // Bodyguard yang tahu posisi MC tetap berdiri di antara MC dan Shooter, bukan menempel Shooter.
        if (IsBodyguard && Blackboard.IsAware)
        {
            Vector3 threat = Blackboard.CanSeePlayer ? Blackboard.Player.transform.position : Blackboard.LastKnownPosition;
            HoldInterposePoint(threat);
            return NodeState.Running;
        }

        hasInterposePoint = false;

        if (DistanceToAnchor() > Data.ReturnThreshold)
        {
            Motor.ClearFacing();
            Motor.MoveTo(AnchorPosition, 1f, Data.ReturnThreshold * 0.5f);
        }
        else
        {
            Motor.Stop();

            if (Blackboard.CanSeePlayer)
                Motor.FacePosition(Blackboard.Player.transform.position);
            else
                Motor.ClearFacing();
        }

        return NodeState.Running;
    }

    // ---------- Helper ----------

    private void HoldInterposePoint(Vector3 threat)
    {
        Vector3 anchorPosition = AnchorPosition;
        Vector3 direction = threat - anchorPosition;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;

        // DayaSerang MC tinggi, Guardian berdiri lebih maju sebagai tameng Shooter.
        float guardDistance = Data.GuardDistance * Reactions.GuardianGuardDistanceMultiplier(Build);
        Vector3 point = anchorPosition + direction.normalized * guardDistance;

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, Data.NavMeshSampleDistance, NavMesh.AllAreas))
        {
            interposePoint = hit.position;
            hasInterposePoint = true;

            Motor.MoveTo(interposePoint, 1f, 0.2f);
        }

        Motor.FacePosition(threat);
    }

    private void MeleeOrApproach(PlayerHealth player, float speedMultiplier)
    {
        if (player == null)
            return;

        // Selama ayunan, Guardian berdiri dan tetap menghadap MC sampai pukulan mendarat.
        if (swingPending)
        {
            Motor.Stop();
            Motor.FacePosition(player.transform.position);
            return;
        }

        if (Blackboard.DistanceToPlayer <= Data.AttackRange)
        {
            Motor.Stop();
            Motor.FacePosition(player.transform.position);

            if (Time.time >= nextAttackTime && Controller.HasAttackToken)
                StartSwing();

            return;
        }

        Motor.ClearFacing();
        Motor.MoveTo(player.transform.position, speedMultiplier, Data.AttackRange * 0.8f);
    }

    // Animasi dimulai sekarang, damage menyusul setelah Melee Hit Delay. MC punya kesempatan menghindar.
    private void StartSwing()
    {
        NotifyAttack();
        SfxManager.Play(SfxCue.GuardianSwing);

        swingPending = true;
        swingHitTime = Time.time + Data.MeleeHitDelay;
        nextAttackTime = Time.time + Data.AttackCooldown;

        if (Data.MeleeHitDelay <= 0f)
            ResolveSwing();
    }

    private void ResolveSwing()
    {
        swingPending = false;

        if (Controller.Health.IsDead)
            return;

        PlayerHealth player = Blackboard.Player;

        if (player == null || player.IsDead)
            return;

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;

        bool inRange = toPlayer.magnitude <= Data.AttackRange * Data.MeleeHitRangeMultiplier;
        bool inFront = Data.MeleeHitAngle >= 360f || Vector3.Angle(transform.forward, toPlayer) <= Data.MeleeHitAngle * 0.5f;

        if (inRange && inFront)
        {
            player.TakeDamage(new DamageInfo(Data.Damage, gameObject, player.AimPoint, DamageKind.Melee));
            SfxManager.Play(SfxCue.GuardianHit);
        }
        else if (Controller.LogStateChanges)
        {
            Debug.Log(name + ": pukulan meleset (" + (inRange ? "di luar sudut" : "di luar jangkauan") + ")", this);
        }
    }

    public Vector3 AnchorPosition => anchor != null ? anchor.position : transform.position;

    // Ketahanan MC tinggi, Guardian jadi garis depan dengan leash dan aggro lebih longgar.
    public float LeashRadius => Data.LeashRadius * Reactions.GuardianLeashMultiplier(Build);

    public float AggroRadius => Data.AggroRadius * Reactions.GuardianAggroMultiplier(Build);

    // Dibaca AI Debug overlay.
    public bool IsReturning => returning;

    public bool HasInterposePoint => hasInterposePoint;

    public Vector3 InterposePoint => interposePoint;

    private float DistanceToAnchor()
    {
        return FlatDistance(transform.position, AnchorPosition);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        EnemyController controller = Controller != null ? Controller : GetComponent<EnemyController>();

        if (controller == null || controller.RoleData == null)
            return;

        EnemyRoleData data = controller.RoleData;
        Vector3 center = (Application.isPlaying && anchor != null ? anchor.position : transform.position) + Vector3.up * 0.05f;

        // Aggro (oranye) dan leash (biru, merah saat pulang) diukur dari anchor.
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        GizmoShapes.Circle(center, Application.isPlaying && Controller != null ? AggroRadius : data.AggroRadius);

        Gizmos.color = returning ? Color.red : new Color(0.3f, 0.6f, 1f, 0.8f);
        GizmoShapes.Circle(center, Application.isPlaying && Controller != null ? LeashRadius : data.LeashRadius);

        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        GizmoShapes.Circle(center, data.ReturnThreshold, 16);

        if (!Application.isPlaying)
            return;

        // Tali dari Guardian ke anchor.
        Gizmos.color = IsBodyguard ? Color.green : Color.white;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, center);
        Gizmos.DrawWireCube(center, Vector3.one * 0.3f);

        if (hasInterposePoint)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(interposePoint + Vector3.up * 0.05f, 0.25f);
        }
    }
}
