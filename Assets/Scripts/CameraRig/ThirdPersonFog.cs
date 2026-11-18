using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

public enum ThirdPersonFogStyle
{
    // Kabut Unity biasa: benda memudar ke satu warna, latar kamera diganti warna yang sama.
    FlatColor,
    // Kabut warna langit: benda memudar ke langit di belakangnya (shader MathGate/SkyFog). Langit berbintang tetap tampil.
    Sky
}

// Kabut jarak untuk kamera orang ketiga di arena dan Safezone (di dungeon sudah ada DungeonAtmosphere).
// Benda mulai memudar di Fog Start dan tertutup penuh di Fog End. Setelah Fog End tidak digambar sama sekali
// (Far Clip lensa Cinemachine), jadi arena lain yang melayang jauh tidak terlihat.
//   Flat Color  RenderSettings fog Linear + latar kamera warna kabut.
//   Sky         efek layar penuh MathGate/SkyFog: tiap piksel dicampur dengan warna langit di arahnya.
//               Langit diambil sekali saat mulai dengan Reflection Probe tersembunyi yang hanya melihat skybox
//               (skybox project ini 6 sisi, jadi tidak bisa dibaca langsung oleh shader).
//               Butuh Full Screen Pass Renderer Feature dengan material SkyFog di renderer URP.
// Hanya aktif saat ThirdPersonCameraRig yang menggerakkan kamera (F8 ke Top Down = kabut memudar hilang).
// Nilai asli lighting dicatat saat kabut mulai dan dikembalikan saat kabut selesai.
// Selama DungeonAtmosphere terpasang, komponen ini tidak menulis apa pun.
[RequireComponent(typeof(ThirdPersonCameraRig))]
public class ThirdPersonFog : MonoBehaviour
{
    private static readonly int WeightId = Shader.PropertyToID("_SkyFogWeight");
    private static readonly int StartId = Shader.PropertyToID("_SkyFogStart");
    private static readonly int EndId = Shader.PropertyToID("_SkyFogEnd");
    private static readonly int CubeId = Shader.PropertyToID("_SkyFogCube");
    private static readonly int HasCubeId = Shader.PropertyToID("_SkyFogHasCube");
    private static readonly int FlipYId = Shader.PropertyToID("_SkyFogFlipY");

    [SerializeField] private bool fog = true;
    [SerializeField] private ThirdPersonFogStyle style = ThirdPersonFogStyle.Sky;
    // Jarak dari kamera.
    [SerializeField, Min(0f)] private float fogStart = 12f;
    [SerializeField, Min(0.1f)] private float fogEnd = 32f;
    // Benda di luar Fog End tidak digambar (lebih ringan, dan memang tidak terlihat).
    [SerializeField] private bool cullBeyondFog = true;
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.6f;

    [Header("Flat Color")]
    [SerializeField] private Color fogColor = new Color(0.16f, 0.18f, 0.26f);
    // Latar kamera diganti warna kabut, bukan skybox.
    [SerializeField] private bool fogColoredBackground = true;

    [Header("Sky")]
    // Resolusi tangkapan langit. Makin besar makin tajam bintangnya.
    [SerializeField] private int skyResolution = 1024;
    // Nyalakan jika kabut langit terlihat terbalik atas-bawah (tergantung platform grafis).
    [SerializeField] private bool flipY;

    private ThirdPersonCameraRig rig;
    private CinemachineCamera cmCamera;
    private DungeonAtmosphere[] dungeons;

    private float weight;
    private bool applied;
    private ThirdPersonFogStyle appliedStyle;

    private bool baseFog;
    private FogMode baseFogMode;
    private Color baseFogColor;
    private float baseFogStart;
    private float baseFogEnd;
    private float baseFarClip;
    private Camera cam;
    private CameraClearFlags baseClearFlags;
    private Color baseBackground;

    private ReflectionProbe skyProbe;
    private int skyRenderId = -1;
    private bool skyReady;

    private void Awake()
    {
        rig = GetComponent<ThirdPersonCameraRig>();
        cmCamera = GetComponent<CinemachineCamera>();
        dungeons = FindObjectsByType<DungeonAtmosphere>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Shader.SetGlobalFloat(WeightId, 0f);
    }

    private void OnDisable()
    {
        if (applied)
            Restore();

        weight = 0f;
        Shader.SetGlobalFloat(WeightId, 0f);
    }

    private void OnDestroy()
    {
        if (skyProbe != null)
            Destroy(skyProbe.gameObject);
    }

    private void LateUpdate()
    {
        bool wanted = fog && rig.IsDriving;
        weight = Mathf.MoveTowards(weight, wanted ? 1f : 0f, Time.unscaledDeltaTime / fadeDuration);

        // Gaya diganti di Inspector saat Play: kembalikan dulu, lalu pasang ulang dengan gaya baru.
        if (applied && appliedStyle != style)
            Restore();

        if (weight > 0f && !applied)
            Begin();

        if (!applied)
            return;

        if (DungeonActive())
        {
            // Dungeon punya kabutnya sendiri. Nilai dungeon tidak ditimpa, nilai asli tetap disimpan untuk Restore nanti.
            Shader.SetGlobalFloat(WeightId, 0f);
            return;
        }

        if (weight <= 0f)
        {
            Restore();
            return;
        }

        if (style == ThirdPersonFogStyle.Sky)
            ApplySky(weight);
        else
            ApplyFlat(weight);
    }

    private bool DungeonActive()
    {
        foreach (DungeonAtmosphere dungeon in dungeons)
        {
            if (dungeon != null && dungeon.IsApplied)
                return true;
        }

        return false;
    }

    private void Begin()
    {
        applied = true;
        appliedStyle = style;

        baseFog = RenderSettings.fog;
        baseFogMode = RenderSettings.fogMode;
        baseFogColor = RenderSettings.fogColor;
        baseFogStart = RenderSettings.fogStartDistance;
        baseFogEnd = RenderSettings.fogEndDistance;
        baseFarClip = cmCamera.Lens.FarClipPlane;

        Transform output = rig.OutputCamera;
        cam = output != null ? output.GetComponent<Camera>() : Camera.main;

        if (cam != null)
        {
            baseClearFlags = cam.clearFlags;
            baseBackground = cam.backgroundColor;
        }

        if (style == ThirdPersonFogStyle.Sky && !skyReady)
            CaptureSky();
    }

    // Seperti DungeonAtmosphere: kabut mulai sangat jauh (praktis tidak ada), lalu mendekat ke Fog Start/End.
    private void ApplyFlat(float t)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(baseFog ? baseFogColor : fogColor, fogColor, t);
        RenderSettings.fogStartDistance = Mathf.Lerp(fogStart + 80f, fogStart, t);
        RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd + 160f, fogEnd, t);

        ApplyCull(t);

        if (cam != null && fogColoredBackground)
        {
            cam.clearFlags = t >= 0.5f ? CameraClearFlags.SolidColor : baseClearFlags;
            cam.backgroundColor = fogColor;
        }
    }

    private void ApplySky(float t)
    {
        // Kabut abu-abu bawaan scene dimatikan supaya benda jauh tidak keabu-abuan sebelum melebur ke langit.
        RenderSettings.fog = false;

        if (!skyReady && skyProbe != null && skyRenderId >= 0 && skyProbe.IsFinishedRendering(skyRenderId))
        {
            Shader.SetGlobalTexture(CubeId, skyProbe.realtimeTexture);
            skyReady = true;
        }

        Shader.SetGlobalFloat(HasCubeId, skyReady ? 1f : 0f);
        Shader.SetGlobalFloat(FlipYId, flipY ? 1f : 0f);
        Shader.SetGlobalFloat(StartId, Mathf.Lerp(fogStart + 80f, fogStart, t));
        Shader.SetGlobalFloat(EndId, Mathf.Lerp(fogEnd + 160f, fogEnd, t));
        Shader.SetGlobalFloat(WeightId, t);

        ApplyCull(t);
    }

    private void ApplyCull(float t)
    {
        if (cullBeyondFog)
            cmCamera.Lens.FarClipPlane = Mathf.Lerp(baseFarClip, fogEnd + 1f, t);
    }

    // Reflection Probe tersembunyi yang hanya menangkap skybox (Culling Mask = Nothing), sekali saja.
    // Ukurannya sangat kecil dan jauh di bawah peta, jadi tidak memengaruhi pantulan benda apa pun.
    private void CaptureSky()
    {
        if (skyProbe == null)
        {
            GameObject probeObject = new GameObject("SkyFog Capture") { hideFlags = HideFlags.HideAndDontSave };
            probeObject.transform.position = new Vector3(0f, -5000f, 0f);

            skyProbe = probeObject.AddComponent<ReflectionProbe>();
            skyProbe.mode = ReflectionProbeMode.Realtime;
            skyProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            skyProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            skyProbe.clearFlags = ReflectionProbeClearFlags.Skybox;
            skyProbe.cullingMask = 0;
            skyProbe.hdr = true;
            skyProbe.size = Vector3.one * 0.01f;
            skyProbe.blendDistance = 0f;
            skyProbe.importance = 0;
        }

        skyProbe.resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(skyResolution, 64, 2048));

        // Probe realtime dimatikan di Quality Settings project ini. Tidak ada probe realtime lain di scene,
        // jadi menyalakannya hanya berarti probe ini yang dirender.
        QualitySettings.realtimeReflectionProbes = true;
        skyRenderId = skyProbe.RenderProbe();
    }

    private void Restore()
    {
        applied = false;

        RenderSettings.fog = baseFog;
        RenderSettings.fogMode = baseFogMode;
        RenderSettings.fogColor = baseFogColor;
        RenderSettings.fogStartDistance = baseFogStart;
        RenderSettings.fogEndDistance = baseFogEnd;
        cmCamera.Lens.FarClipPlane = baseFarClip;
        Shader.SetGlobalFloat(WeightId, 0f);

        if (cam != null)
        {
            cam.clearFlags = baseClearFlags;
            cam.backgroundColor = baseBackground;
        }
    }
}
