using UnityEngine;

// Efek visual di tubuh korban saat terkena damage, dipilih per jenis damage.
// Dipasang di Player atau musuh. Proyektil punya efek impact sendiri di prefab-nya,
// jadi slot Projectile di sini biasanya dikosongkan supaya efek tidak dobel.
public class DamageEffectSpawner : MonoBehaviour
{
    [Header("Effect per Damage Kind")]
    // Pukulan Guardian dan serangan jarak dekat lain.
    [SerializeField] private GameObject meleeEffect;
    // Terjangan Charger.
    [SerializeField] private GameObject lungeEffect;
    // Aura Burst MC (dipasang di musuh).
    [SerializeField] private GameObject burstEffect;
    // Opsional. Kosongkan jika prefab proyektil sudah punya Character Impact Effect.
    [SerializeField] private GameObject projectileEffect;
    // Damage tanpa jenis khusus, mis. dari script uji.
    [SerializeField] private GameObject genericEffect;

    [Header("Placement")]
    [SerializeField] private float effectScale = 1f;
    // Efek diarahkan menjauhi penyerang, seperti percikan yang terlempar ke belakang korban.
    [SerializeField] private bool faceAwayFromAttacker = true;

    private PlayerHealth playerHealth;
    private EnemyHealth enemyHealth;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        enemyHealth = GetComponent<EnemyHealth>();

        if (playerHealth == null && enemyHealth == null)
            Debug.LogWarning(name + ": DamageEffectSpawner butuh PlayerHealth atau EnemyHealth di objek yang sama.", this);
    }

    private void OnEnable()
    {
        if (playerHealth != null)
            playerHealth.Damaged += HandleDamaged;

        if (enemyHealth != null)
            enemyHealth.Damaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.Damaged -= HandleDamaged;

        if (enemyHealth != null)
            enemyHealth.Damaged -= HandleDamaged;
    }

    private void HandleDamaged(float amount, DamageInfo info)
    {
        GameObject prefab = PickEffect(info.kind);

        if (prefab == null)
            return;

        Vector3 aim = playerHealth != null ? playerHealth.AimPoint : enemyHealth.AimPoint;
        Vector3 point = info.point.sqrMagnitude > 0.0001f ? info.point : aim;

        Quaternion rotation = Quaternion.identity;

        if (faceAwayFromAttacker && info.source != null)
        {
            Vector3 away = point - info.source.transform.position;
            away.y = 0f;

            if (away.sqrMagnitude > 0.0001f)
                rotation = Quaternion.LookRotation(away.normalized);
        }

        EffectPool.Spawn(prefab, point, rotation, effectScale);
    }

    private GameObject PickEffect(DamageKind kind)
    {
        switch (kind)
        {
            case DamageKind.Melee: return meleeEffect;
            case DamageKind.Lunge: return lungeEffect != null ? lungeEffect : meleeEffect;
            case DamageKind.Burst: return burstEffect;
            case DamageKind.Projectile: return projectileEffect;
            default: return genericEffect;
        }
    }
}
