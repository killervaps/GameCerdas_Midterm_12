using UnityEngine;

// Bola kecil bercahaya yang memulihkan HP MC saat disentuh.
// Cahaya berasal dari material emisif (ditangkap Bloom) ditambah point light yang berdenyut.
public class HealthOrb : PickupItem
{
    [Header("Heal")]
    // Fraksi MaxHP MC. 0,2 = pulih 20% MaxHP.
    [SerializeField, Range(0f, 1f)] private float healFraction = 0.2f;
    // Tambahan heal tetap di luar fraksi.
    [SerializeField, Min(0f)] private float flatHeal = 0f;
    // Mati = orb tidak terambil saat HP penuh, jadi bisa disimpan untuk nanti.
    [SerializeField] private bool consumeAtFullHealth = false;

    [Header("Glow")]
    [SerializeField] private Light glowLight;
    [SerializeField] private float pulseSpeed = 3f;
    // Intensitas light berdenyut di antara (1 - amplitude) dan (1 + amplitude) kali intensitas awal.
    [SerializeField, Range(0f, 1f)] private float pulseAmplitude = 0.35f;

    private float baseIntensity;

    protected override void Awake()
    {
        base.Awake();

        if (glowLight == null)
            glowLight = GetComponentInChildren<Light>();

        if (glowLight != null)
            baseIntensity = glowLight.intensity;
    }

    protected override void Update()
    {
        base.Update();

        if (glowLight != null)
            glowLight.intensity = baseIntensity * (1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude);
    }

    protected override bool TryApply(PlayerHealth player)
    {
        if (!consumeAtFullHealth && player.CurrentHealth >= player.MaxHealth - 0.01f)
            return false;

        player.Heal(player.MaxHealth * healFraction + flatHeal);
        return true;
    }
}
