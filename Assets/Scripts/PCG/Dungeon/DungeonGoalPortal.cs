using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Portal keluar di room Goal dungeon. Padam selama masih ada musuh, menyala setelah ronde bersih.
// Seperti buff gate, MC berdiri dalam radius portal lalu menahan Interact (E) untuk kembali ke Safezone,
// jadi lewat dekat portal tidak langsung mengakhiri ronde (mis. saat masih ingin membuka vault).
// Dicek lewat jarak, tanpa collider.
// Pendarnya sama dengan gate Safezone (GatePortalGlow): bagian gate model Goal diberi emission emas yang berdenyut,
// Point Light di tengahnya, dan lebih terang saat MC mendekat. Terlihat karena Bloom di Global Volume.
// Material asli prefab tidak diubah: setiap renderer memakai salinan material sendiri.
public class DungeonGoalPortal : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color OpenColor = new Color(1f, 0.68f, 0.2f);

    // Radius tempat MC bisa menahan Interact.
    [SerializeField, Min(0.3f)] private float enterRadius = 1.4f;
    [SerializeField, Min(0.3f)] private float enterHeight = 2f;
    // Action yang sama dengan buff gate (interaction Hold), jadi lama tahannya ikut pengaturan Input Actions.
    [SerializeField] private string confirmActionName = "Player/Interact";

    [Header("Glow")]
    // Hanya child dengan awalan nama ini yang berpendar, mis. "gate" di wall-gate (temboknya "wall_gate" tidak ikut).
    // Tidak ada yang cocok = seluruh model berpendar.
    [SerializeField] private string gateMeshPrefix = "gate";
    // Di atas 1 supaya ikut Bloom.
    [SerializeField] private float emissionIntensity = 2.4f;
    // Pengali saat MC berada dalam Highlight Radius.
    [SerializeField] private float highlightBoost = 1.6f;
    [SerializeField] private float highlightRadius = 4f;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField, Range(0f, 1f)] private float pulseAmount = 0.25f;
    [SerializeField] private float fadeSpeed = 4f;
    [SerializeField] private float lightIntensity = 3f;
    [SerializeField] private float lightHeight = 1.2f;

    private readonly List<Material> instances = new List<Material>();

    private Light glow;
    private Color current = Color.black;
    private bool entered;
    private InputAction confirmAction;
    // Tahan baru dihitung jika tombol mulai ditekan di dalam radius. Menahan E sambil berjalan masuk tidak ikut.
    private bool armed;

    public bool IsOpen { get; private set; }

    public bool IsHighlighted { get; private set; }

    // MC berada dalam radius portal yang terbuka, jadi bisa menahan E. Dipakai prompt UI.
    public bool PlayerInRange { get; private set; }

    // Progress tahan Interact (0-1).
    public float ConfirmProgress =>
        PlayerInRange && armed && confirmAction != null && confirmAction.phase == InputActionPhase.Started
            ? Mathf.Clamp01(confirmAction.GetTimeoutCompletionPercentage())
            : 0f;

    // Dikirim sekali saat MC selesai menahan Interact di portal yang terbuka.
    public event Action<DungeonGoalPortal> Entered;

    // Dipanggil DungeonGenerator setelah portal dibuat. Prefab Goal apa pun bisa dipakai.
    public void Build(float tileSize)
    {
        enterRadius = Mathf.Max(enterRadius, tileSize * 0.75f);

        if (InputSystem.actions != null)
            confirmAction = InputSystem.actions.FindAction(confirmActionName);

        if (confirmAction == null)
            Debug.LogWarning(name + ": action '" + confirmActionName + "' tidak ditemukan, portal Goal tidak bisa dipakai.", this);
        else
            confirmAction.Enable();
        highlightRadius = Mathf.Max(highlightRadius, enterRadius * 2.5f);

        List<Renderer> targets = new List<Renderer>();

        foreach (Transform part in GatePortalGlow.FindGateParts(transform, gateMeshPrefix))
            targets.AddRange(part.GetComponentsInChildren<Renderer>(true));

        bool partsFound = targets.Count > 0;

        if (!partsFound)
            targets.AddRange(GetComponentsInChildren<Renderer>(true));

        foreach (Renderer target in targets)
        {
            Material[] shared = target.sharedMaterials;
            Material[] copies = new Material[shared.Length];

            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null || !shared[i].HasProperty(EmissionColorId))
                {
                    copies[i] = shared[i];
                    continue;
                }

                Material copy = new Material(shared[i]) { hideFlags = HideFlags.DontSave };
                copy.EnableKeyword("_EMISSION");
                copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                copy.SetColor(EmissionColorId, Color.black);
                copies[i] = copy;
                instances.Add(copy);
            }

            target.sharedMaterials = copies;
        }

        if (instances.Count == 0)
            Debug.LogWarning(name + ": material Goal tidak punya _EmissionColor, pendar dilewati (lampu tetap dipakai).", this);

        GameObject lightObject = new GameObject("Glow");
        lightObject.hideFlags = gameObject.hideFlags;
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = Vector3.up * lightHeight;

        // Lampu di tengah bagian gate supaya cahayanya keluar dari lengkung gate.
        if (partsFound)
        {
            Bounds bounds = targets[0].bounds;

            foreach (Renderer target in targets)
                bounds.Encapsulate(target.bounds);

            lightObject.transform.position = bounds.center;
        }

        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = tileSize * 4f;
        glow.shadows = LightShadows.None;
        glow.intensity = 0f;
        glow.enabled = false;

        SetOpen(false);
    }

    public void Open()
    {
        if (IsOpen)
            return;

        SetOpen(true);
        SfxManager.Play(SfxCue.GateChoose);
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
        entered = false;
        armed = false;
        IsHighlighted = false;
        PlayerInRange = false;
    }

    private void Update()
    {
        PlayerHealth player = PlayerHealth.Current;
        float distance = float.MaxValue;
        float height = 0f;

        if (player != null && !player.IsDead)
        {
            Vector3 offset = player.transform.position - transform.position;
            height = Mathf.Abs(offset.y);
            offset.y = 0f;
            distance = offset.magnitude;
        }

        IsHighlighted = IsOpen && distance <= highlightRadius;
        PlayerInRange = IsOpen && !entered && height <= enterHeight && distance <= enterRadius;

        UpdateGlow();

        if (!PlayerInRange || confirmAction == null)
        {
            armed = false;
            return;
        }

        if (confirmAction.WasPressedThisFrame())
            armed = true;

        if (!armed || !confirmAction.WasPerformedThisFrame())
            return;

        entered = true;
        armed = false;
        PlayerInRange = false;
        Entered?.Invoke(this);
    }

    private void UpdateGlow()
    {
        Color target = Color.black;

        if (IsOpen)
        {
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            target = OpenColor * emissionIntensity * pulse * (IsHighlighted ? highlightBoost : 1f);
        }

        current = Color.Lerp(current, target, 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime));

        foreach (Material material in instances)
        {
            if (material != null)
                material.SetColor(EmissionColorId, current);
        }

        if (glow == null)
            return;

        // Warna lampu tanpa HDR, kecerahan dipindah ke intensitas (dibatasi 2x supaya tidak menyilaukan).
        float brightness = Mathf.Max(current.r, Mathf.Max(current.g, current.b));
        glow.enabled = brightness > 0.01f;
        glow.color = brightness > 0.0001f ? current / brightness : Color.black;
        glow.intensity = lightIntensity * Mathf.Min(brightness / Mathf.Max(0.01f, emissionIntensity), 2f);
    }

    private void OnDestroy()
    {
        foreach (Material material in instances)
        {
            if (material == null)
                continue;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }

        instances.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOpen ? OpenColor : Color.gray;
        Gizmos.DrawWireSphere(transform.position, enterRadius);
        Gizmos.color = new Color(OpenColor.r, OpenColor.g, OpenColor.b, 0.35f);
        Gizmos.DrawWireSphere(transform.position, highlightRadius);
    }
}
