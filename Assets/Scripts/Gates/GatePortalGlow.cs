using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Model gate berpendar dengan warna rarity buff yang sedang ditawarkan, plus Point Light kecil di dalam lengkungnya.
//   Biasa abu-abu, Langka hijau, Sangat Langka biru, Epik ungu, Legendaris emas, SIGMA berputar RGB.
//   Makin langka makin terang. Berdenyut pelan, lebih terang saat MC berdiri di depan gate, padam saat gate tertutup.
// Material asli tidak diubah. Saat Play tiap renderer gate memakai salinan material sendiri yang emission-nya dinyalakan,
// jadi tiga gate bisa berbeda warna walau memakai material yang sama. Pendar terlihat karena Bloom di Global Volume.
// Dipasang otomatis oleh GatePortal. Tambahkan manual di objek GatePortal jika ingin mengatur nilainya di Inspector.
[RequireComponent(typeof(GatePortal))]
public class GatePortalGlow : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Target")]
    // Kosongkan untuk mencari otomatis: MeshRenderer di objek induk yang namanya (atau nama induknya) diawali
    // Gate Mesh Prefix, mis. "gate-1". Tembok ("wall-gate") tidak ikut menyala.
    [SerializeField] private Renderer[] gateRenderers;
    [SerializeField] private string gateMeshPrefix = "gate";

    [Header("Emission")]
    // Di atas 1 supaya ikut Bloom (threshold Bloom di Volume = 1).
    [SerializeField] private float emissionIntensity = 1.6f;
    // Tambahan intensitas per tingkat rarity. Legendaris = intensitas × (1 + 4 × nilai ini).
    [SerializeField] private float rarityBoost = 0.2f;
    // Pengali saat MC berdiri di depan gate.
    [SerializeField] private float highlightBoost = 1.6f;

    [Header("Pulse")]
    [SerializeField] private float pulseSpeed = 2f;
    // 0 = tanpa denyut, 0,3 = terang-redup ±30%.
    [SerializeField, Range(0f, 1f)] private float pulseAmount = 0.25f;
    // Kecepatan transisi warna (berganti rarity, menyala, padam).
    [SerializeField] private float fadeSpeed = 4f;

    [Header("Point Light")]
    [SerializeField] private bool useLight = true;
    [SerializeField] private float lightIntensity = 2.5f;
    [SerializeField] private float lightRange = 4f;
    // Posisi lampu relatif terhadap pusat model gate (dunia).
    [SerializeField] private Vector3 lightOffset = new Vector3(0f, 0f, 0f);

    private readonly List<Material> instances = new List<Material>();

    private GatePortal portal;
    private Light glowLight;
    private Color current = Color.black;
    private float phase;
    private bool ready;

    private void Awake()
    {
        portal = GetComponent<GatePortal>();
        phase = Random.value * 10f;
    }

    private void Start()
    {
        if (gateRenderers == null || gateRenderers.Length == 0)
            gateRenderers = FindGateRenderers();

        if (gateRenderers.Length == 0)
        {
            Debug.LogWarning(name + ": GatePortalGlow tidak menemukan MeshRenderer model gate. Isi Gate Renderers manual.", this);
            return;
        }

        bool anyEmission = false;

        foreach (Renderer target in gateRenderers)
        {
            if (target == null)
                continue;

            // renderer.materials membuat salinan khusus renderer ini. Dihapus lagi di OnDestroy.
            foreach (Material material in target.materials)
            {
                if (!material.HasProperty(EmissionColorId))
                    continue;

                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor(EmissionColorId, Color.black);
                instances.Add(material);
                anyEmission = true;
            }
        }

        if (!anyEmission)
            Debug.LogWarning(name + ": shader material gate tidak punya _EmissionColor, pendar dilewati (lampu tetap dipakai).", this);

        if (useLight)
            CreateLight();

        ready = true;
    }

    private void OnDestroy()
    {
        foreach (Material material in instances)
        {
            if (material != null)
                Destroy(material);
        }

        instances.Clear();
    }

    private void Update()
    {
        if (!ready)
            return;

        Color target = TargetColor();
        current = Color.Lerp(current, target, 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime));

        foreach (Material material in instances)
            material.SetColor(EmissionColorId, current);

        if (glowLight != null)
        {
            // Warna lampu tanpa HDR, kecerahan dipindah ke intensitas.
            float brightness = Mathf.Max(current.r, Mathf.Max(current.g, current.b));
            glowLight.enabled = brightness > 0.01f;
            glowLight.color = brightness > 0.0001f ? current / brightness : Color.black;
            // Rarity tinggi dan highlight ikut membuat lampu lebih terang, dibatasi 2× supaya tidak menyilaukan.
            glowLight.intensity = lightIntensity * Mathf.Min(brightness / Mathf.Max(0.01f, emissionIntensity), 2f);
            glowLight.range = lightRange;
        }
    }

    // Warna emission HDR yang dituju frame ini. Hitam jika gate tertutup.
    private Color TargetColor()
    {
        MathGateData gate = portal.Gate;

        if (gate == null)
            return Color.black;

        GateRarity rarity = gate.Rarity;
        Color baseColor = GateRarityStyle.Color(rarity);

        float pulse = 1f + Mathf.Sin((Time.time + phase) * pulseSpeed) * pulseAmount;
        float intensity = emissionIntensity * (1f + (int)rarity * rarityBoost) * pulse;

        if (portal.IsHighlighted)
            intensity *= highlightBoost;

        return baseColor * intensity;
    }

    // Inti gate (mis. child "gate-1" di model wall-gate), tanpa tembok, teks TMP, dan objek di bawah portal ini.
    // Jika tidak ada yang cocok dengan prefix, seluruh model dipakai supaya gate tetap berpendar.
    private Renderer[] FindGateRenderers()
    {
        Transform root = transform.parent != null ? transform.parent : transform;
        List<Renderer> all = new List<Renderer>();
        List<Renderer> matching = new List<Renderer>();

        foreach (MeshRenderer candidate in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (candidate.transform.IsChildOf(transform))
                continue;

            if (candidate.GetComponent<TMP_Text>() != null)
                continue;

            all.Add(candidate);

            if (HasPrefixedAncestor(candidate.transform, root))
                matching.Add(candidate);
        }

        if (matching.Count == 0 && all.Count > 0)
        {
            Debug.LogWarning(name + ": tidak ada objek berawalan '" + gateMeshPrefix + "', seluruh model gate dipakai untuk pendar.", this);
            return all.ToArray();
        }

        return matching.ToArray();
    }

    // Objek ini atau salah satu induknya (sampai root, tidak termasuk root) diawali prefix, tanpa beda huruf besar/kecil.
    private bool HasPrefixedAncestor(Transform node, Transform root)
    {
        if (string.IsNullOrEmpty(gateMeshPrefix))
            return true;

        for (Transform t = node; t != null && t != root; t = t.parent)
        {
            if (t.name.StartsWith(gateMeshPrefix, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void CreateLight()
    {
        Bounds bounds = gateRenderers[0].bounds;

        foreach (Renderer target in gateRenderers)
        {
            if (target != null)
                bounds.Encapsulate(target.bounds);
        }

        GameObject lightObject = new GameObject("RarityLight");
        lightObject.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
        lightObject.transform.position = bounds.center + lightOffset;

        glowLight = lightObject.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.shadows = LightShadows.None;
        glowLight.range = lightRange;
        glowLight.intensity = 0f;
        glowLight.enabled = false;
    }
}
