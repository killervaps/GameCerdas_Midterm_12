using UnityEngine;

public class PlayerAttackTest : MonoBehaviour
{
    [SerializeField] private float attackDistance = 3f;
    [SerializeField] private float damage = 20f;

    [SerializeField] private Animator animator;

    private static readonly int AttackHash = Animator.StringToHash("Attack");

    private PlayerHealth health;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (health != null && health.IsDead)
            return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (animator != null)
                animator.SetTrigger(AttackHash);

            TryAttackEnemy();
        }
    }

    private void TryAttackEnemy()
    {
        EnemyHealth target = FindNearestEnemy();

        if (target == null)
            return;

        target.TakeDamage(damage);

        Debug.Log(
            "Player attacks " + target.name + "!"
        );
    }

    // Menyerang satu enemy hidup yang paling dekat dalam attackDistance.
    private EnemyHealth FindNearestEnemy()
    {
        EnemyHealth nearest = null;
        float nearestDistance = attackDistance;

        foreach (EnemyHealth candidate in EnemyHealth.Active)
        {
            if (candidate.IsDead)
                continue;

            float distance =
                Vector3.Distance(
                    transform.position,
                    candidate.transform.position
                );

            if (distance <= nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
