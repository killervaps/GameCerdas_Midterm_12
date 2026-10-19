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
    private Vector3 interposePoint;
    private bool hasInterposePoint;

    public override string BrainName => "BT/Guardian";

    public Transform Anchor => anchor;

    // True jika anchor adalah musuh lain (mode bodyguard), bukan titik diam.
    public bool IsBodyguard => anchorHealth != null;

    public bool IsEnraged => enraged;

    public override string DebugDetail
    {
        get
        {
            if (enraged)
                return "<color=#ff5555>MARAH</color>";

            if (anchor == null)
                return string.Empty;

            string anchorName = IsBodyguard ? anchor.name : "titik";
            string text = "anchor " + anchorName + "  leash " + DistanceToAnchor().ToString("0.0") + "/" + Data.LeashRadius.ToString("0.#");

            if (returning)
                text += " pulang";

            if (Controller.HasAttackToken)
                text += " [token]";

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

        if (distance > Data.LeashRadius)
            returning = true;

        return returning;
    }

    private bool PlayerInAggro()
    {
        PlayerHealth player = Blackboard.Player;

        if (player == null || player.IsDead)
            return false;

        float playerToAnchor = FlatDistance(player.transform.position, AnchorPosition);

        if (Blackboard.CanSeePlayer && playerToAnchor <= Data.AggroRadius)
            return true;

        // Diserang MC dari luar aggro radius tetapi masih dalam leash, Guardian boleh mengejar sebentar.
        return Blackboard.WasDamagedWithin(Data.DamageAggroDuration) &&
               Blackboard.HasLastKnownPosition &&
               FlatDistance(Blackboard.LastKnownPosition, AnchorPosition) <= Data.LeashRadius;
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

        Vector3 point = anchorPosition + direction.normalized * Data.GuardDistance;

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

        if (Blackboard.DistanceToPlayer <= Data.AttackRange)
        {
            Motor.Stop();
            Motor.FacePosition(player.transform.position);

            if (Time.time >= nextAttackTime && Controller.HasAttackToken)
            {
                player.TakeDamage(new DamageInfo(Data.Damage, gameObject, player.AimPoint, DamageKind.Melee));
                nextAttackTime = Time.time + Data.AttackCooldown;
            }

            return;
        }

        Motor.ClearFacing();
        Motor.MoveTo(player.transform.position, speedMultiplier, Data.AttackRange * 0.8f);
    }

    private Vector3 AnchorPosition => anchor != null ? anchor.position : transform.position;

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
        GizmoShapes.Circle(center, data.AggroRadius);

        Gizmos.color = returning ? Color.red : new Color(0.3f, 0.6f, 1f, 0.8f);
        GizmoShapes.Circle(center, data.LeashRadius);

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
