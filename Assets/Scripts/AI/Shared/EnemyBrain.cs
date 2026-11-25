using UnityEngine;

// Dasar semua brain musuh (Charger, Shooter, Guardian, dummy).
// EnemyController memanggil Tick setiap 0,1-0,2 s setelah perception dan memori diperbarui.
// Brain tidak saling tahu, koordinasi kelompok lewat TacticalCoordinator (M6).
public abstract class EnemyBrain : MonoBehaviour, IEnemyBrain
{
    protected EnemyController Controller { get; private set; }

    protected EnemyBlackboard Blackboard => Controller.Blackboard;

    protected EnemyMotor Motor => Controller.Motor;

    protected EnemyRoleData Data => Controller.RoleData;

    // Damage satu serangan, sudah termasuk pengali stat wave (Endless).
    protected float AttackDamage => Data.Damage * Controller.DamageMultiplier;

    // Build MC menurut GroupBlackboard. Netral (semua 1) jika tidak ada TacticalCoordinator.
    protected BuildIndicators Build =>
        TacticalCoordinator.Current != null ? TacticalCoordinator.Current.Group.Indicators : BuildIndicators.Neutral;

    protected BuildReactions Reactions =>
        TacticalCoordinator.Current != null ? TacticalCoordinator.Current.Reactions : BuildReactions.Disabled;

    // Pengetahuan kelompok, termasuk titik objective. Null tanpa TacticalCoordinator.
    protected GroupBlackboard Group => TacticalCoordinator.Current != null ? TacticalCoordinator.Current.Group : null;

    protected bool HasObjective => Group != null && Group.HasObjectivePoint;

    // Titik di dalam zona untuk musuh ini. Tiap musuh mendapat sudut sendiri (dari namanya yang unik, mis. Charger_12)
    // supaya tidak bertumpuk.
    protected Vector3 ObjectiveSpot(float radiusFraction)
    {
        GroupBlackboard group = Group;
        float angle = (Mathf.Abs(name.GetHashCode()) % 360) * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * group.ObjectiveRadius * radiusFraction;
        Vector3 point = group.ObjectivePoint + offset;

        return UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas)
            ? hit.position
            : group.ObjectivePoint;
    }

    public abstract string BrainName { get; }

    public EnemyAction CurrentAction { get; private set; } = EnemyAction.Idle;

    public virtual bool IsScanning => false;

    // Baris tambahan untuk label debug, mis. skor utility atau sisa cooldown.
    public virtual string DebugDetail => string.Empty;

    // Dikirim setiap kali brain melakukan serangan sekali jalan (pukulan, tembakan). Dipakai animasi dan nanti SFX/VFX.
    public event System.Action AttackPerformed;

    protected void NotifyAttack()
    {
        AttackPerformed?.Invoke();
    }

    public virtual void Initialize(EnemyController controller)
    {
        Controller = controller;
    }

    public abstract void Tick(float deltaTime);

    public virtual void OnDied()
    {
        SetAction(EnemyAction.Dead);
    }

    protected void SetAction(EnemyAction action)
    {
        if (CurrentAction == action)
            return;

        EnemyAction previous = CurrentAction;
        CurrentAction = action;

        if (Controller != null && Controller.LogStateChanges)
            Debug.Log(name + ": " + previous + " -> " + action, this);
    }
}
