using UnityEngine;

// Kedip api obor dungeon: intensitas lampu dan ukuran api naik-turun mengikuti Perlin noise, jadi geraknya halus
// dan tiap obor tidak berkedip bersamaan. Dipasang DungeonGenerator. Hanya bergerak saat Play.
public class TorchFlicker : MonoBehaviour
{
    private Light torchLight;
    private Transform flame;
    private float baseIntensity;
    private float amount;
    private float speed;
    private float phase;
    private Vector3 flameScale;

    public void Setup(Light target, Transform flameVisual, float flickerAmount, float flickerSpeed, float noisePhase)
    {
        torchLight = target;
        flame = flameVisual;
        baseIntensity = target != null ? target.intensity : 0f;
        amount = flickerAmount;
        speed = flickerSpeed;
        phase = noisePhase;
        flameScale = flame != null ? flame.localScale : Vector3.one;
    }

    private void Update()
    {
        if (torchLight == null || amount <= 0f)
            return;

        // Dua lapis noise, pelan dan cepat, supaya kedipnya tidak terlihat berpola.
        float slow = Mathf.PerlinNoise(phase, Time.time * speed * 0.35f) - 0.5f;
        float fast = Mathf.PerlinNoise(phase + 17.3f, Time.time * speed) - 0.5f;
        float flicker = 1f + (slow * 1.2f + fast * 0.8f) * amount;

        torchLight.intensity = baseIntensity * flicker;

        if (flame != null)
            flame.localScale = new Vector3(flameScale.x, flameScale.y * (0.85f + flicker * 0.15f), flameScale.z);
    }
}
