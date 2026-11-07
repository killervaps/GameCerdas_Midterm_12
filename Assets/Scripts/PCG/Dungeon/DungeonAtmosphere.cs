using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Suasana bawah tanah Mode Dungeon. Dungeon satu scene dengan Safezone, jadi lighting scene tidak diubah permanen:
// selama MC berada di dalam area dungeon, nilai di bawah di-fade masuk, dan dikembalikan ke nilai asli saat MC keluar.
//   - Directional Light diredupkan dan dibuat dingin, ambient gelap, pantulan skybox dikurangi
//   - fog Linear gelap: sekitar MC tetap jelas, bagian layar yang jauh dari kamera memudar
//   - latar kamera warna gelap, bukan skybox, supaya celah di luar dinding tidak terlihat seperti langit
//   - Volume global prioritas tinggi (Vignette, Color Adjustments, Bloom) dengan bobot mengikuti fade
//   - lentera: Point Light hangat yang mengikuti MC
// Obor di dinding dibuat DungeonGenerator. Hanya aktif saat Play. Nilai bisa diubah langsung saat Play untuk mencoba,
// tapi salin lewat Copy Component sebelum keluar Play supaya tidak hilang.
[RequireComponent(typeof(DungeonGenerator))]
public class DungeonAtmosphere : MonoBehaviour
{
    [SerializeField] private bool atmosphere = true;
    [SerializeField, Min(0.05f)] private float fadeDuration = 1f;
    // Area dungeon diperluas sebesar ini (meter) saat mengecek apakah MC di dalam.
    [SerializeField] private float boundsMargin = 2f;

    [Header("Sun & Ambient")]
    [SerializeField, Min(0f)] private float sunIntensity = 0.25f;
    [SerializeField] private Color sunColor = new Color(0.55f, 0.65f, 1f);
    // Jangan terlalu gelap: musuh di luar jangkauan obor harus tetap terbaca.
    [SerializeField] private Color ambientColor = new Color(0.12f, 0.13f, 0.18f);
    [SerializeField, Range(0f, 1f)] private float reflectionIntensity = 0.2f;

    [Header("Fog (Linear)")]
    [SerializeField] private bool fog = true;
    [SerializeField] private Color fogColor = new Color(0.025f, 0.028f, 0.04f);
    // Jarak dari kamera. Kamera top-down di scene berjarak sekitar 6 m dari MC, jadi kabut mulai tepat di belakang MC.
    [SerializeField, Min(0f)] private float fogStart = 7f;
    [SerializeField, Min(0.1f)] private float fogEnd = 20f;
    [SerializeField] private bool darkBackground = true;

    [Header("Post Processing")]
    [SerializeField] private bool postProcessing = true;
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.42f;
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.45f;
    [SerializeField] private float postExposure = -0.2f;
    [SerializeField, Range(-100f, 100f)] private float saturation = -18f;
    [SerializeField, Range(-100f, 100f)] private float contrast = 12f;
    [SerializeField] private Color colorFilter = new Color(0.93f, 0.96f, 1f);
    // Pendar kunci, chest, pintu vault, dan portal lebih menonjol di tempat gelap.
    [SerializeField, Min(0f)] private float bloomIntensity = 0.6f;

    [Header("Lentera MC")]
    [SerializeField] private bool lantern = true;
    [SerializeField] private Color lanternColor = new Color(1f, 0.78f, 0.52f);
    [SerializeField, Min(0f)] private float lanternIntensity = 1.6f;
    [SerializeField, Min(0.5f)] private float lanternRange = 6f;
    [SerializeField] private float lanternHeight = 2.2f;

    private DungeonGenerator generator;
    private float weight;
    private bool applied;

    private Light sun;
    private float baseSunIntensity;
    private Color baseSunColor;
    private bool baseFog;
    private FogMode baseFogMode;
    private Color baseFogColor;
    private float baseFogDensity;
    private float baseFogStart;
    private float baseFogEnd;
    private AmbientMode baseAmbientMode;
    private Color baseAmbient;
    private float baseReflection;

    private Camera cam;
    private CameraClearFlags baseClearFlags;
    private Color baseBackground;

    private Volume volume;
    private VolumeProfile profile;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;
    private Bloom bloom;

    private Light lanternLight;

    // 0 = suasana Safezone, 1 = suasana dungeon penuh.
    public float Weight => weight;

    public bool IsActive => applied;

    private void Awake()
    {
        generator = GetComponent<DungeonGenerator>();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        float target = atmosphere && PlayerInside() ? 1f : 0f;
        weight = Mathf.MoveTowards(weight, target, Time.deltaTime / Mathf.Max(0.05f, fadeDuration));

        if (weight <= 0f)
        {
            if (applied)
                Restore();

            return;
        }

        if (!applied)
            Begin();

        Apply(weight);
    }

    private void OnDisable()
    {
        if (applied)
            Restore();

        weight = 0f;
    }

    private void OnDestroy()
    {
        if (profile != null)
            Destroy(profile);

        // Lentera menempel di MC, bukan di objek ini.
        if (lanternLight != null)
            Destroy(lanternLight.gameObject);
    }

    private bool PlayerInside()
    {
        PlayerHealth player = PlayerHealth.Current;

        if (player == null || generator == null || !generator.HasLayout)
            return false;

        Vector3 local = player.transform.position - generator.transform.position;
        float width = generator.MapWidth * generator.TileSize;
        float height = generator.MapHeight * generator.TileSize;

        return local.x >= -boundsMargin && local.x <= width + boundsMargin &&
               local.z >= -boundsMargin && local.z <= height + boundsMargin;
    }

    // Nilai asli dicatat tepat sebelum suasana dungeon dipasang, jadi perubahan lighting lain di Safezone tetap dihormati.
    private void Begin()
    {
        applied = true;

        sun = RenderSettings.sun != null ? RenderSettings.sun : FindDirectionalLight();

        if (sun != null)
        {
            baseSunIntensity = sun.intensity;
            baseSunColor = sun.color;
        }

        baseFog = RenderSettings.fog;
        baseFogMode = RenderSettings.fogMode;
        baseFogColor = RenderSettings.fogColor;
        baseFogDensity = RenderSettings.fogDensity;
        baseFogStart = RenderSettings.fogStartDistance;
        baseFogEnd = RenderSettings.fogEndDistance;
        baseAmbientMode = RenderSettings.ambientMode;
        baseAmbient = RenderSettings.ambientLight;
        baseReflection = RenderSettings.reflectionIntensity;

        cam = Camera.main;

        if (cam != null)
        {
            baseClearFlags = cam.clearFlags;
            baseBackground = cam.backgroundColor;
        }

        if (postProcessing)
            EnsureVolume();

        if (lantern)
            EnsureLantern();
    }

    private void Apply(float t)
    {
        if (sun != null)
        {
            sun.intensity = Mathf.Lerp(baseSunIntensity, sunIntensity, t);
            sun.color = Color.Lerp(baseSunColor, sunColor, t);
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.Lerp(baseAmbient, ambientColor, t);
        RenderSettings.reflectionIntensity = Mathf.Lerp(baseReflection, reflectionIntensity, t);

        if (fog)
        {
            // Mulai dari kabut yang sangat jauh (praktis tanpa kabut), lalu mendekat ke Fog Start/End.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(baseFog ? baseFogColor : fogColor, fogColor, t);
            RenderSettings.fogStartDistance = Mathf.Lerp(fogStart + 80f, fogStart, t);
            RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd + 160f, fogEnd, t);
        }

        if (cam != null && darkBackground)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = fogColor;
        }

        if (volume != null)
        {
            volume.weight = t;
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(vignetteSmoothness);
            colorAdjustments.postExposure.Override(postExposure);
            colorAdjustments.saturation.Override(saturation);
            colorAdjustments.contrast.Override(contrast);
            colorAdjustments.colorFilter.Override(colorFilter);
            bloom.intensity.Override(bloomIntensity);
        }

        if (lanternLight != null)
        {
            PlayerHealth player = PlayerHealth.Current;

            if (player != null && lanternLight.transform.parent != player.transform)
                AttachLantern(player.transform);

            lanternLight.enabled = lantern && t > 0.01f;
            lanternLight.color = lanternColor;
            lanternLight.range = lanternRange;
            lanternLight.intensity = lanternIntensity * t;
            lanternLight.transform.localPosition = Vector3.up * lanternHeight;
        }
    }

    private void Restore()
    {
        applied = false;

        if (sun != null)
        {
            sun.intensity = baseSunIntensity;
            sun.color = baseSunColor;
        }

        RenderSettings.fog = baseFog;
        RenderSettings.fogMode = baseFogMode;
        RenderSettings.fogColor = baseFogColor;
        RenderSettings.fogDensity = baseFogDensity;
        RenderSettings.fogStartDistance = baseFogStart;
        RenderSettings.fogEndDistance = baseFogEnd;
        RenderSettings.ambientMode = baseAmbientMode;
        RenderSettings.ambientLight = baseAmbient;
        RenderSettings.reflectionIntensity = baseReflection;

        if (cam != null)
        {
            cam.clearFlags = baseClearFlags;
            cam.backgroundColor = baseBackground;
        }

        if (volume != null)
            volume.weight = 0f;

        if (lanternLight != null)
            lanternLight.enabled = false;
    }

    private static Light FindDirectionalLight()
    {
        foreach (Light candidate in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (candidate.type == LightType.Directional)
                return candidate;
        }

        return null;
    }

    // Volume global dengan prioritas di atas Global Volume scene. Profil dibuat saat Play, tidak disimpan sebagai asset.
    private void EnsureVolume()
    {
        if (volume != null)
            return;

        GameObject volumeObject = new GameObject("DungeonVolume");
        volumeObject.transform.SetParent(transform, false);

        volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.weight = 0f;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.DontSave;
        vignette = profile.Add<Vignette>();
        vignette.color.Override(Color.black);
        colorAdjustments = profile.Add<ColorAdjustments>();
        bloom = profile.Add<Bloom>();
        volume.sharedProfile = profile;
    }

    private void EnsureLantern()
    {
        if (lanternLight != null)
            return;

        GameObject lightObject = new GameObject("DungeonLantern");
        lanternLight = lightObject.AddComponent<Light>();
        lanternLight.type = LightType.Point;
        lanternLight.shadows = LightShadows.None;
        lanternLight.enabled = false;

        if (PlayerHealth.Current != null)
            AttachLantern(PlayerHealth.Current.transform);
        else
            lightObject.transform.SetParent(transform, false);
    }

    private void AttachLantern(Transform player)
    {
        lanternLight.transform.SetParent(player, false);
        lanternLight.transform.localPosition = Vector3.up * lanternHeight;
    }
}
