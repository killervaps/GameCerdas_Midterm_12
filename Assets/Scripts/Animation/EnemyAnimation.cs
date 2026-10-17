using UnityEngine;

public class EnemyAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    private static readonly int StateHash = Animator.StringToHash("State");

    private IEnemyBrain brain;
    private int lastState;
    private bool initialized;

    private void Awake()
    {
        // Bisa EnemyBTController maupun EnemyUtilityController.
        brain = GetComponentInParent<IEnemyBrain>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (brain == null || animator == null)
            return;

        int state = ToAnimatorState(brain.CurrentAction);

        // Kirim nilai hanya saat berubah
        if (initialized && state == lastState)
            return;

        initialized = true;
        lastState = state;

        // Patrol = 0, Chase = 1, Attack = 2, Flee = 3, Dead = 4, Search = 5
        animator.SetInteger(StateHash, state);
    }

    private int ToAnimatorState(EnemyAction action)
    {
        // Search punya dua tampilan: saat berjalan ke last known position memakai animasi jalan (Patrol = 0), saat menoleh memakai Idle (5).
        if (action == EnemyAction.Search && !brain.IsScanning)
            return (int)EnemyAction.Patrol;

        return (int)action;
    }
}
