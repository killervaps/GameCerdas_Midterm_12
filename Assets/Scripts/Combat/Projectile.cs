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

    [Header("Launch VFX (opsional)")]
    // Efek di titik keluar saat proyektil dilepas (muzzle flash), menghadap arah tembakan.
    [SerializeField] private GameObject launchEffect;
    [SerializeField] private float launchEffectScale = 1f;

    [Header("Impact VFX (opsional)")]
    // Efek saat mengenai karakter (IDamageable), mis. CFXR Hit A dari JMO Assets.
    [SerializeField] private GameObject characterImpactEffect;
    // Efek saat menabrak dinding, obstacle, atau lantai.
    [SerializeField] private GameObject wallImpactEffect;
    // Pengali ukuran efek. Arena yang diperkecil biasanya butuh nilai di bawah 1.
    [SerializeField] private float impactEffectScale = 1f;
    // Efek wall impact digeser sedikit keluar dari permukaan supaya tidak terbenam di dinding.
    [SerializeField] private float surfaceOffset = 0.02f;

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
    // Collider di bawah objek ini dilewati, mis. lantai menara tempat penembak berdiri.
    private Transform ignoreRoot;
    // Bola Aura MC (layer PlayerProjectile) atau peluru Shooter. Menentukan SFX tembak dan kena.
    private bool isPlayerProjectile;

    public bool IsFlying => flying;

    private void Awake()
    {
        trail = GetComponentInChildren<TrailRenderer>();
        mask = useLayerCollisionMatrix ? BuildMaskFromMatrix(gameObject.layer) : hitMask.value;
        isPlayerProjectile = gameObject.layer == LayerMask.NameToLayer(GameLayers.PlayerProjectile);
    }

    public void Launch(
        ProjectilePool owner,
        Vector3 position,
        Vector3 launchDirection,
        IDamageable homingTarget,
        float projectileDamage,
        GameObject projectileSource,
        DamageKind damageKind = DamageKind.Projectile,
        Transform ignoreCollidersUnder = null)
    {
        pool = owner;
        ignoreRoot = ignoreCollidersUnder;
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

        if (launchEffect != null)
            EffectPool.Spawn(launchEffect, position, Quaternion.LookRotation(direction), launchEffectScale);

        SfxManager.Play(isPlayerProjectile ? SfxCue.MagicCast : SfxCue.ShooterShot);
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

        if (Physics.SphereCast(position, radius, direction, out RaycastHit hit, step, mask, QueryTriggerInteraction.Ignore) &&
            (ignoreRoot == null || !hit.collider.transform.IsChildOf(ignoreRoot)))
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
        bool hitCharacter = damageable != null && !damageable.IsDead;

        if (hitCharacter)
            damageable.TakeDamage(new DamageInfo(damage, source, hit.point, kind));

        SpawnImpact(hit, hitCharacter);

        SfxManager.Play(!hitCharacter
            ? SfxCue.ProjectileHitWall
            : isPlayerProjectile ? SfxCue.MagicHit : SfxCue.ShooterHit);

        Despawn();
    }

    private void SpawnImpact(RaycastHit hit, bool hitCharacter)
    {
        GameObject prefab = hitCharacter ? characterImpactEffect : wallImpactEffect;

        if (prefab == null)
            return;

        // Normal permukaan menghadap balik ke arah datang proyektil. Jika tidak valid, pakai arah balik terbang.
        Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : -direction;

        EffectPool.Spawn(
            prefab,
            hit.point + normal * surfaceOffset,
            Quaternion.LookRotation(normal),
            impactEffectScale
        );
    }

    private void Despawn()
    {
        flying = false;
        target = null;
        targetComponent = null;
        ignoreRoot = null;

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
