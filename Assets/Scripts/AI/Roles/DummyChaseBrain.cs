using System.Collections.Generic;
using UnityEngine;

// Brain uji untuk komponen AI bersama, sebelum brain per role dibuat.
// Behavior Tree (selector prioritas, dievaluasi dari atas setiap tick):
//   1. Mati            -> Die
//   2. MC terlihat     -> Chase, atau Attack jika sudah dalam jangkauan
//   3. Punya LKP       -> Search (ke LKP lalu menoleh)
//   4. Lainnya         -> Idle
public class DummyChaseBrain : EnemyBrain
{
    [Header("Attack")]
    // Pukulan langsung tanpa hitbox, cukup untuk menguji alur kalah. Diganti serangan per role di M5.
    [SerializeField] private bool enableMeleeAttack = true;

    private BTNode root;
    private SearchRoutine search;
    private float nextAttackTime;

    public override string BrainName => "BT/Dummy";

    public override bool IsScanning => search != null && search.CurrentPhase == SearchRoutine.Phase.Scan;

    public override string DebugDetail
    {
        get
        {
            if (search == null || !search.IsActive)
                return string.Empty;

            return search.CurrentPhase == SearchRoutine.Phase.Scan
                ? "scan " + (search.ScanProgress * 100f).ToString("0") + "%"
                : "ke LKP";
        }
    }

    public override void Initialize(EnemyController controller)
    {
        base.Initialize(controller);

        search = new SearchRoutine(Motor, transform);

        root = new SelectorNode(new List<BTNode>
        {
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => Controller.Health.IsDead),
                new ActionNode(Die)
            }),
            new SequenceNode(new List<BTNode>
            {
                new ConditionNode(() => Blackboard.CanSeePlayer),
                new ActionNode(Engage)
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

    private NodeState Die()
    {
        search.Cancel();
        SetAction(EnemyAction.Dead);
        return NodeState.Success;
    }

    private NodeState Engage()
    {
        // MC terlihat lagi, pencarian lama tidak berlaku.
        search.Cancel();

        PlayerHealth player = Blackboard.Player;

        if (Blackboard.DistanceToPlayer <= Data.AttackRange)
        {
            SetAction(EnemyAction.Attack);

            Motor.Stop();
            Motor.FacePosition(player.transform.position);

            if (enableMeleeAttack && Time.time >= nextAttackTime)
            {
                player.TakeDamage(new DamageInfo(Data.Damage, gameObject, player.AimPoint, DamageKind.Melee));
                nextAttackTime = Time.time + Data.AttackCooldown;
            }

            return NodeState.Running;
        }

        SetAction(EnemyAction.Chase);

        Motor.ClearFacing();
        Motor.MoveTo(player.transform.position, 1f, Data.AttackRange * 0.8f);

        return NodeState.Running;
    }

    private NodeState Search()
    {
        // Mulai baru jika belum mencari, atau LKP sudah bergeser jauh (mis. diserang dari arah lain).
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

        // Pencarian selesai tanpa menemukan MC, LKP dibuang.
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
}
