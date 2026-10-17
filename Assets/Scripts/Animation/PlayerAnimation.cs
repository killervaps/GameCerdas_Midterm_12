using UnityEngine;

public class PlayerAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerHealth health;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsDeadHash = Animator.StringToHash("IsDead");

    private Vector3 lastPosition;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<PlayerHealth>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;

        float speed = Time.deltaTime > 0f
            ? delta.magnitude / Time.deltaTime
            : 0f;

        lastPosition = transform.position;

        animator.SetFloat(SpeedHash, speed);

        animator.SetBool(IsDeadHash, health.IsDead);
    }
}
