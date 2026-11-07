using UnityEngine;

// Chest di tengah vault. Terbuka saat MC mendekat (hanya mungkin setelah pintu vault dibuka), lalu memberi gate hadiah.
// Cahaya chest berwarna rarity minimum sebelum dibuka, berganti ke warna rarity hadiah setelah dibuka.
// Lid (opsional) diputar terbuka. Visual prefab tanpa lid cukup dianimasikan membesar sesaat.
public class VaultChest : MonoBehaviour
{
    [SerializeField, Min(0.3f)] private float openRadius = 1.3f;
    [SerializeField] private float popDuration = 0.45f;
    [SerializeField] private float lidAngle = -110f;

    private DungeonVault vault;
    private Transform visual;
    private Transform lid;
    private Light glow;
    private Vector3 visualScale = Vector3.one;
    private Quaternion lidClosed;
    private float openTime = -1f;
    private float pulsePhase;

    public bool IsOpened { get; private set; }

    public void Setup(DungeonVault owner, Transform visualRoot, Transform lidPivot, Light light, float radius)
    {
        vault = owner;
        visual = visualRoot;
        lid = lidPivot;
        glow = light;
        openRadius = Mathf.Max(openRadius, radius);

        if (visual != null)
            visualScale = visual.localScale;

        if (lid != null)
            lidClosed = lid.localRotation;

        if (glow != null)
            glow.color = GateRarityStyle.Color(owner.MinRarity);
    }

    private void Update()
    {
        if (openTime >= 0f)
            AnimateOpen();

        UpdateGlow();

        if (IsOpened || vault == null || vault.State != VaultState.Open)
            return;

        PlayerHealth player = PlayerHealth.Current;

        if (player == null || player.IsDead)
            return;

        Vector3 offset = player.transform.position - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude <= openRadius * openRadius)
            Open();
    }

    private void Open()
    {
        IsOpened = true;
        openTime = 0f;

        vault.Loot();
        SfxManager.Play(SfxCue.ItemPickup);
    }

    private void AnimateOpen()
    {
        openTime += Time.deltaTime;

        float t = Mathf.Clamp01(openTime / Mathf.Max(0.01f, popDuration));

        if (visual != null)
            visual.localScale = visualScale * (1f + Mathf.Sin(t * Mathf.PI) * 0.18f);

        if (lid != null)
            lid.localRotation = lidClosed * Quaternion.Euler(lidAngle * (1f - (1f - t) * (1f - t)), 0f, 0f);

        if (t >= 1f)
            openTime = -1f;
    }

    private void UpdateGlow()
    {
        if (glow == null)
            return;

        pulsePhase += Time.deltaTime;

        if (!IsOpened)
        {
            glow.intensity = 1.2f + Mathf.Sin(pulsePhase * 2.5f) * 0.4f;
            return;
        }

        MathGateData reward = vault != null ? vault.Reward : null;
        GateRarity rarity = reward != null ? reward.Rarity : vault != null ? vault.MinRarity : GateRarity.VeryRare;

        glow.color = GateRarityStyle.Color(rarity);
        glow.intensity = Mathf.Lerp(glow.intensity, 2.5f, 1f - Mathf.Exp(-3f * Time.deltaTime));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOpened ? Color.gray : new Color(1f, 0.68f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, openRadius);
    }
}
