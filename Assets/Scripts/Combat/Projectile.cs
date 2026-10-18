using UnityEngine;

// Proyektil homing ringan yang dipakai ulang dari ProjectilePool.
// Gerak memakai SphereCast per frame (bukan trigger) supaya tidak tembus saat cepat dan tidak butuh Rigidbody.
// Berhenti di objek pertama yang ditabrak, termasuk IDamageable pertama. Ini yang membuat Guardian bisa menghalangi tembakan.
public class Projectile : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField] private float speed = 12f;
    [SerializeField] private float lifetime = 2f;
    // Derajat per detik. 0 berarti lurus tanpa homing.
    [SerializeField] private float homingTurnRate = 240f;
    [SerializeField] private float radius = 0.15f;

    [Header("Collision")]
    // Jika aktif, layer yang ditabrak mengikuti Layer Collision Matrix untuk layer objek ini.
    [SerializeField] private bool useLayerCollisionMatrix = true;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private ProjectilePool pool;
    private IDamageable target;
    private Component targetComponent;
    private GameObject source;
    private Vector3 direction;
    private float damage;
    private DamageKind kind;
    private float age;
    private bool flying;
    private int mask;
    private TrailRenderer trail;

    public bool IsFlying => flying;

    private void Awake()
    {
        trail = GetComponentInChildren<TrailRenderer>();
        mask = useLayerCollisionMatrix ? BuildMaskFromMatrix(gameObject.layer) : hitMask.value;
    }

    public void Launch(
        ProjectilePool owner,
        Vector3 position,
        Vector3 launchDirection,
        IDamageable homingTarget,
        float projectileDamage,
        GameObject projectileSource,
        DamageKind damageKind = DamageKind.Projectile)
    {
        pool = owner;
        target = homingTarget;
        targetComponent = homingTarget as Component;
        source = projectileSource;
        damage = projectileDamage;
        kind = damageKind;
        age = 0f;
        flying = true;

        direction = launchDirection.sqrMagnitude > 0.0001f
            ? launchDirection.normalized
            : transform.forward;

        transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));

        if (trail != null)
            trail.Clear();
    }

    private void Update()
    {
        if (!flying)
            return;

        float dt = Time.deltaTime;

        age += dt;

        if (age >= lifetime)
        {
            Despawn();
            return;
        }

        Steer(dt);

        Vector3 position = transform.position;
        float step = speed * dt;

        if (Physics.SphereCast(position, radius, direction, out RaycastHit hit, step, mask, QueryTriggerInteraction.Ignore))
        {
            Hit(hit);
            return;
        }

        transform.SetPositionAndRotation(position + direction * step, Quaternion.LookRotation(direction));
    }

    private void Steer(float dt)
    {
        if (homingTurnRate <= 0f || !HasLiveTarget())
            return;

        Vector3 desired = target.AimPoint - transform.position;

        if (desired.sqrMagnitude < 0.0001f)
            return;

        direction = Vector3.RotateTowards(
            direction,
            desired.normalized,
            homingTurnRate * Mathf.Deg2Rad * dt,
            0f
        );
    }

    private bool HasLiveTarget()
    {
        // Cek Unity null (objek sudah dihancurkan) dan objek nonaktif.
        if (targetComponent == null || !targetComponent.gameObject.activeInHierarchy)
            return false;

        return !target.IsDead;
    }

    private void Hit(RaycastHit hit)
    {
        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();

        if (damageable != null && !damageable.IsDead)
            damageable.TakeDamage(new DamageInfo(damage, source, hit.point, kind));

        Despawn();
    }

    private void Despawn()
    {
        flying = false;
        target = null;
        targetComponent = null;

        if (pool != null)
            pool.Release(this);
        else
            Destroy(gameObject);
    }

    private static int BuildMaskFromMatrix(int layer)
    {
        int result = 0;

        for (int other = 0; other < 32; other++)
        {
            if (!Physics.GetIgnoreLayerCollision(layer, other))
                result |= 1 << other;
        }

        return result;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !flying)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, radius);

        if (HasLiveTarget())
        {
            Gizmos.color = new Color(1f, 0f, 1f, 0.3f);
            Gizmos.DrawLine(transform.position, target.AimPoint);
        }
    }
}
