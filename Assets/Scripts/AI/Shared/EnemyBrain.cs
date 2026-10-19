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

    // Build MC menurut GroupBlackboard. Netral (semua 1) jika tidak ada TacticalCoordinator.
    protected BuildIndicators Build =>
        TacticalCoordinator.Current != null ? TacticalCoordinator.Current.Group.Indicators : BuildIndicators.Neutral;

    protected BuildReactions Reactions =>
        TacticalCoordinator.Current != null ? TacticalCoordinator.Current.Reactions : BuildReactions.Disabled;

    public abstract string BrainName { get; }

    public EnemyAction CurrentAction { get; private set; } = EnemyAction.Idle;

    public virtual bool IsScanning => false;

    // Baris tambahan untuk label debug, mis. skor utility atau sisa cooldown.
    public virtual string DebugDetail => string.Empty;

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
