using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Penanda prop yang boleh ditembus kamera orang ketiga (pilar, patung, pohon, bata, barel, dll).
// Dipasang di root prop: menu MathGate > Camera > Mark See-Through Props (grup obstacle arena, menara, dekorasi Safezone),
// Mark Selected As See-Through untuk objek pilihan, dan otomatis untuk props dungeon saat dungeon dibuat.
// Deteksi memakai bentuk yang terlihat (bounds renderer), jadi prop tanpa collider juga bisa memudar.
//   - CameraWallClamp mengabaikan collider prop ini, jadi kamera tidak lagi maju-mundur setiap melewati prop.
//   - CameraOccluderFade memudarkan prop ini selama berada di antara kamera dan MC.
// Layer tidak diubah (tetap NavBlock), jadi NavMesh, pandangan musuh, proyektil, dan PCG tidak terpengaruh.
// Bayangan tetap ada saat memudar.
[DisallowMultipleComponent]
public class CameraSeeThroughProp : MonoBehaviour
{
    private static readonly Dictionary<Collider, CameraSeeThroughProp> lookup = new Dictionary<Collider, CameraSeeThroughProp>();
    private static readonly List<CameraSeeThroughProp> all = new List<CameraSeeThroughProp>();

    // Semua prop aktif. Dibaca CameraOccluderFade setiap frame.
    public static IReadOnlyList<CameraSeeThroughProp> All => all;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");

    // Opasitas terendah prop ini saat memudar. Negatif = memakai nilai CameraOccluderFade (Min Opacity).
    // Untuk prop besar beri nilai lebih tinggi supaya tetap terbaca, mis. 0,45.
    [SerializeField, Range(-1f, 1f)] private float minOpacityOverride = -1f;

    private class Slot
    {
        public Renderer renderer;
        public Material[] original;
        public Material[] faded;
        public bool canFade;
        public ShadowCastingMode originalShadows;
    }

    private readonly List<Slot> slots = new List<Slot>();
    private bool prepared;
    private bool swapped;
    private float alpha = 1f;
    private float targetAlpha = 1f;
    private float fadeSpeed = 6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lookup.Clear();
        all.Clear();
    }

    private Bounds bounds;
    private bool hasBounds;
    private bool boundsReady;

    // Gabungan bounds renderer prop. Dihitung sekali; prop dianggap diam (arena, Safezone, dungeon).
    public bool TryGetBounds(out Bounds result)
    {
        if (!boundsReady || transform.hasChanged)
        {
            boundsReady = true;
            transform.hasChanged = false;
            hasBounds = false;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
        }

        result = bounds;
        return hasBounds;
    }

    // Apakah prop ini menghalangi garis kamera -> titik fokus (diperlebar Radius), atau kamera berada di dalamnya.
    public bool Blocks(Vector3 camera, Vector3 focus, float radius, float cameraRadius)
    {
        if (!TryGetBounds(out Bounds box))
            return false;

        Bounds around = box;
        around.Expand(cameraRadius * 2f);

        if (around.Contains(camera))
            return true;

        Vector3 delta = focus - camera;
        float length = delta.magnitude;

        if (length < 0.001f)
            return false;

        box.Expand(radius * 2f);

        // Titik fokus sendiri di dalam prop (mis. MC berdiri di balik pilar tipis): tetap dipudarkan.
        return box.IntersectRay(new Ray(camera, delta / length), out float distance) && distance <= length;
    }

    private void OnEnable()
    {
        if (!all.Contains(this))
            all.Add(this);
    }

    // Apakah collider ini milik prop yang boleh ditembus kamera. Hasil dicache per collider.
    public static bool IsSeeThrough(Collider collider)
    {
        return Find(collider) != null;
    }

    public static CameraSeeThroughProp Find(Collider collider)
    {
        if (collider == null)
            return null;

        if (!lookup.TryGetValue(collider, out CameraSeeThroughProp prop))
        {
            prop = collider.GetComponentInParent<CameraSeeThroughProp>();
            lookup[collider] = prop;
        }

        return prop != null && prop.isActiveAndEnabled ? prop : null;
    }

    // False = tidak dicek CameraOccluderFade (dipakai MC sendiri, yang dipudarkan ThirdPersonCameraRig saat kamera dekat).
    public bool ScanByCamera { get; set; } = true;

    public float MinOpacity(float fallback) => minOpacityOverride >= 0f ? minOpacityOverride : fallback;

    // Dipanggil CameraOccluderFade setiap frame selama prop menghalangi.
    public void RequestFade(float opacity, float speed)
    {
        targetAlpha = Mathf.Clamp01(opacity);
        fadeSpeed = speed;
        enabled = true;
    }

    public void ReleaseFade(float speed)
    {
        targetAlpha = 1f;
        fadeSpeed = speed;
    }

    private void Update()
    {
        if (Mathf.Approximately(alpha, targetAlpha))
            return;

        alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
        Apply();
    }

    private void OnDisable()
    {
        all.Remove(this);
        alpha = 1f;
        targetAlpha = 1f;

        if (prepared)
            Apply();
    }

    private void OnDestroy()
    {
        foreach (Slot slot in slots)
        {
            if (slot.faded == null)
                continue;

            foreach (Material material in slot.faded)
            {
                if (material != null)
                    Destroy(material);
            }
        }
    }

    private void Apply()
    {
        if (!prepared)
            Prepare();

        bool faded = alpha < 0.999f;

        foreach (Slot slot in slots)
        {
            if (slot.renderer == null)
                continue;

            if (!slot.canFade)
            {
                // Shader bukan URP Lit/Simple Lit/Unlit: tidak bisa transparan, jadi disembunyikan dengan bayangan tetap ada.
                slot.renderer.shadowCastingMode = faded && alpha < 0.5f ? ShadowCastingMode.ShadowsOnly : slot.originalShadows;
                continue;
            }

            if (faded != swapped)
                slot.renderer.sharedMaterials = faded ? slot.faded : slot.original;

            if (!faded)
                continue;

            foreach (Material material in slot.faded)
                SetAlpha(material, alpha);
        }

        swapped = faded;
    }

    private void Prepare()
    {
        prepared = true;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                continue;

            Material[] original = renderer.sharedMaterials;
            Material[] faded = new Material[original.Length];
            bool canFade = original.Length > 0;

            for (int i = 0; i < original.Length; i++)
            {
                if (original[i] == null || !original[i].HasProperty(SurfaceId))
                {
                    canFade = false;
                    break;
                }

                faded[i] = MakeTransparent(original[i]);
            }

            if (!canFade)
            {
                foreach (Material material in faded)
                {
                    if (material != null)
                        Destroy(material);
                }

                faded = null;
            }

            slots.Add(new Slot
            {
                renderer = renderer,
                original = original,
                faded = faded,
                canFade = canFade,
                originalShadows = renderer.shadowCastingMode
            });
        }
    }

    // Salinan material URP yang diubah ke Surface Type Transparent (Alpha blend). Material asli tidak disentuh.
    private static Material MakeTransparent(Material source)
    {
        Material material = new Material(source) { name = source.name + " (See-Through)" };

        material.SetFloat(SurfaceId, 1f);
        material.SetFloat("_Blend", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Transparent;

        return material;
    }

    private static void SetAlpha(Material material, float alpha)
    {
        int id = material.HasProperty(BaseColorId) ? BaseColorId : material.HasProperty(ColorId) ? ColorId : -1;

        if (id < 0)
            return;

        Color color = material.GetColor(id);
        color.a = alpha;
        material.SetColor(id, color);
    }
}
