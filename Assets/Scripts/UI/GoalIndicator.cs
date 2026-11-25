using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Penanda arah portal Goal di Mode Dungeon. Hanya muncul setelah ronde bersih dan portal terbuka, jadi selama
// pertempuran pemain tetap menjelajah sendiri.
// Pandunya berurutan lewat DungeonGuide, sama dengan waypoint grup musuh: satu titik pada satu waktu, mulai dari
// pintu keluar room MC, belokan koridor, pintu masuk room berikutnya, dan terakhir portal. Titik berikutnya baru
// dipakai setelah MC sampai di titik sekarang, jadi penanda tidak pernah menunjuk menembus dinding.
// Titik terlihat di layar = belah ketupat di atasnya, di luar layar = panah di tepi layar. Jarak = jarak jalan ke portal.
// Saat MC berada di dekat portal, kartu "Tahan E" muncul di bawah layar dengan bar progress seperti kartu buff gate,
// plus pengingat jika vault ronde ini belum dibuka.
public class GoalIndicator : UIScreen
{
    [SerializeField] private float edgeMargin = 80f;
    [SerializeField] private float markerHeight = 2.2f;
    [SerializeField] private float arrowSize = 44f;
    [SerializeField] private float diamondSize = 30f;
    [SerializeField] private float bobAmount = 6f;

    [Header("Pandu Dungeon")]
    [SerializeField, Min(0.05f)] private float guideInterval = 0.5f;
    // Titik pandu dianggap tercapai jika jarak jalan (BFS) dari cell MC ke cell titik paling banyak segini.
    [SerializeField, Min(0)] private int reachCells = 1;
    // Rencana dibuat ulang jika MC menjauh dari titik sekarang lebih dari segini cell.
    [SerializeField, Min(1)] private int strayCells = 4;
    // Jarak aman garis pandang dari dinding, dalam cell.
    [SerializeField, Range(0f, 0.45f)] private float sightClearance = 0.3f;

    [Header("Debug (F1)")]
    [SerializeField] private bool drawDebugPath = true;

    private readonly DungeonGuide guide = new DungeonGuide { targetName = "portal" };
    private float nextGuideUpdate;
    private bool guideReady;
    private float guideMeters;
    private DungeonGoalPortal guidedPortal;

    public static GoalIndicator Current { get; private set; }

    // Sedang memandu ke portal (portal terbuka dan penanda dipakai).
    public bool IsTracking => IsVisible && guidedPortal != null && ShowMarker;

    // Satu baris untuk panel DDA.
    public string DebugStatus => guideReady
        ? "Goal " + Mathf.CeilToInt(guideMeters) + " m, pandu " + guide.DescribeStep()
        : "Goal tanpa jalur grid";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    private RectTransform marker;
    private RectTransform arrow;
    private RectTransform diamond;
    private TextMeshProUGUI label;
    private CanvasGroup content;
    private Camera cam;

    // False = penanda arah disembunyikan, kartu "Tahan E" tetap dipakai. Diatur GameUI (Show Goal Indicator).
    public bool ShowMarker { get; set; } = true;

    private RectTransform prompt;
    private CanvasGroup promptGroup;
    private TextMeshProUGUI promptText;
    private TextMeshProUGUI promptNote;
    private RectTransform promptFill;
    private Image promptKeyCap;
    private TextMeshProUGUI promptKeyLabel;

    protected override void Build()
    {
        Current = this;

        marker = UIKit.CreateRect("Marker", rect);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(arrowSize, arrowSize);
        content = marker.gameObject.AddComponent<CanvasGroup>();
        content.blocksRaycasts = false;

        // Panah dan belah ketupat diberi lapisan tinta di bawahnya (pola yang sama dengan LockOnReticle), dan
        // label berupa stiker emas bertinta. Emas polos di atas lantai pasir hanya 1.09:1, jadi tanpa tinta
        // penanda ini tidak terbaca di lantai terang.
        arrow = CreateInkedShape("Arrow", ReticleSprites.TriangleInk, ReticleSprites.Triangle, arrowSize);
        diamond = CreateInkedShape("Diamond", ReticleSprites.DiamondInk, ReticleSprites.Diamond, diamondSize);

        Image labelStamp = UIKit.CreateFreeStamp("Label", marker, "GOAL", 20f, UIKit.Accent, 0f, out label);
        RectTransform labelRect = labelStamp.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -(arrowSize * 0.5f + 6f));

        BuildPrompt();
    }

    // Wadah berisi siluet tinta (lebih besar) di bawah dan bentuk emas di atasnya, ukurannya sama.
    private RectTransform CreateInkedShape(string shapeName, Sprite inkSprite, Sprite fillSprite, float size)
    {
        RectTransform root = UIKit.CreateRect(shapeName, marker);
        root.sizeDelta = new Vector2(size, size);

        Image ink = UIKit.CreateImage("Ink", root, UIKit.Ink, false);
        ink.sprite = inkSprite;
        UIKit.Stretch(ink.rectTransform);

        Image fill = UIKit.CreateImage("Fill", root, UIKit.Accent, false);
        fill.sprite = fillSprite;
        UIKit.Stretch(fill.rectTransform);

        return root;
    }

    // Kaki kartu sama dengan GateInfoModal: tutup tombol [E] emas + bar progress.
    private void BuildPrompt()
    {
        Image panel = UIKit.CreatePanel("Prompt", rect, UIKit.Panel);
        prompt = panel.rectTransform;
        UIKit.Place(prompt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(480f, 0f));
        UIKit.AddVertical(panel.gameObject, 6f, new RectOffset(20, 20, 14, 16));
        UIKit.FitHeight(panel.gameObject);
        promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        promptGroup.blocksRaycasts = false;
        promptGroup.alpha = 0f;

        promptText = UIKit.CreateText("Text", prompt, string.Empty, 20f, UIKit.Text, TextAlignmentOptions.Left);
        // Peringatan memakai kuning "Sedang" (tanda hati-hati), bukan emas aksen yang khusus untuk aksi.
        promptNote = UIKit.CreateText("Note", prompt, string.Empty, 18f, UIKit.ThreatColor(ThreatLevel.Medium), TextAlignmentOptions.Left);

        RectTransform row = UIKit.CreateRect("ConfirmRow", prompt);
        UIKit.AddHorizontal(row.gameObject, 10f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(row, 40f);

        Image keyCap = UIKit.SetCorners(UIKit.CreateImage("KeyCap", row, UIKit.Accent), UIKit.InkSmall);
        UIKit.Layout(keyCap, 38f, 40f, 0f);
        TextMeshProUGUI keyLabel = UIKit.UseDisplayFont(UIKit.CreateText("Key", keyCap.transform, "E", 24f,
            new Color(0.23f, 0.15f, 0.02f), TextAlignmentOptions.Center, true));
        UIKit.Stretch(keyLabel.rectTransform);
        promptKeyCap = keyCap;
        promptKeyLabel = keyLabel;

        RectTransform bar = UIKit.CreateBar("ConfirmBar", row, UIKit.Ink, UIKit.Accent, out Image fill);
        UIKit.Layout(bar, 14f, -1f, 1f);
        promptFill = fill.rectTransform;
        UIKit.SetBarFill(promptFill, 0f);
    }

    // extraction = portal Ekstraksi di Safezone (Endless), teks dan catatannya berbeda dengan portal Goal.
    private void UpdatePrompt(DungeonGoalPortal portal, bool extraction)
    {
        bool show = portal != null && portal.PlayerInRange;
        promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, show ? 1f : 0f, 8f * Time.unscaledDeltaTime);

        if (!show)
            return;

        float progress = portal.ConfirmProgress;
        string action = extraction ? "ekstraksi" : "kembali ke Safezone";
        UIKit.SetBarFill(promptFill, progress);
        ControlGlyphs.Apply(promptText);
        promptText.text = progress > 0.01f
            ? (extraction ? "Ekstraksi... <b>" : "Kembali ke Safezone... <b>") + Mathf.RoundToInt(progress * 100f) + "%</b>"
            : "Tahan <b>" + ControlHints.Glyph(PadAction.Interact) + "</b> untuk " + action;
        ControlHints.StyleCap(promptKeyCap, promptKeyLabel, PadAction.Interact);

        string note = string.Empty;

        if (extraction)
        {
            AuraScore aura = AuraScore.Current;
            note = "Run berakhir di sini." + (aura != null ? " Skor sementara " + aura.Unbanked.ToString("N0") + " diamankan." : string.Empty);
        }
        else
        {
            DungeonVault vault = DungeonVault.Active;

            if (vault != null && vault.State != VaultState.Looted)
                note = "Vault belum dibuka. Hadiahnya hilang jika ronde diakhiri.";
        }

        promptNote.text = note;
        bool hasNote = note.Length > 0;

        if (promptNote.gameObject.activeSelf != hasNote)
        {
            promptNote.gameObject.SetActive(hasNote);
            UIKit.ForceRebuild(prompt);
        }
    }

    protected override void OnShow()
    {
        ResetGuide();
    }

    protected override void OnHide()
    {
        ResetGuide();
    }

    private void ResetGuide()
    {
        guide.Reset();
        guideReady = false;
        guidedPortal = null;
        nextGuideUpdate = 0f;
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }

    // Rute grid dari MC ke portal, diperbarui tiap Guide Interval. Portal baru = rencana baru dari posisi MC sekarang.
    private void UpdateGuide(DungeonGoalPortal portal, PlayerHealth player)
    {
        if (portal != guidedPortal)
        {
            guide.Reset();
            guidedPortal = portal;
            nextGuideUpdate = 0f;
        }

        if (Time.time < nextGuideUpdate)
            return;

        nextGuideUpdate = Time.time + guideInterval;
        guideReady = false;

        RunManager run = ui.Run;
        DungeonGenerator dungeon = run != null ? run.Dungeon : null;

        if (dungeon == null || !dungeon.HasLayout || player == null)
            return;

        guide.reachCells = reachCells;
        guide.strayCells = strayCells;
        guide.sightClearance = sightClearance;

        Vector3 target = portal.transform.position;

        if (!guide.Search(dungeon, player.transform.position) || !DungeonGuide.TryCellOf(dungeon, target, out Vector2Int targetCell))
            return;

        guideReady = guide.Follow(dungeon, targetCell, target);
        guideMeters = guide.TargetDistance * dungeon.TileSize;
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        DungeonGoalPortal portal = ui.Run != null ? ui.Run.OpenPortal : null;
        PlayerHealth player = PlayerHealth.Current;

        // Di Safezone portal Ekstraksi hanya memakai kartu "Tahan E". Letaknya di samping gate, jadi tanpa penanda arah.
        DungeonGoalPortal extraction = portal == null && ui.State == RunState.Preparing && ui.GateRoom != null
            ? ui.GateRoom.ExtractionPortal
            : null;

        UpdatePrompt(portal != null ? portal : extraction, extraction != null);

        if (cam == null || !cam.isActiveAndEnabled)
            cam = Camera.main;

        if (portal == null || cam == null || !ShowMarker)
        {
            content.alpha = 0f;
            return;
        }

        UpdateGuide(portal, player);

        // Tanpa rute grid (mis. layout belum ada), penanda jatuh kembali ke portal langsung.
        Vector3 point = guideReady ? guide.CurrentPoint : portal.transform.position;
        float meters = guideReady
            ? guideMeters
            : player != null ? Vector3.Distance(player.transform.position, portal.transform.position) : 0f;

        content.alpha = 1f;

        bool onScreen = WaypointProjection.Project(cam, rect, edgeMargin, point + Vector3.up * markerHeight,
            out Vector2 local, out Vector2 direction, out float scale);

        label.text = "GOAL  " + Mathf.CeilToInt(meters) + " m";

        float bob = Mathf.Sin(Time.unscaledTime * 4f) * bobAmount;

        if (onScreen)
        {
            marker.anchoredPosition = local + Vector2.up * bob;
            arrow.gameObject.SetActive(false);
            diamond.gameObject.SetActive(true);
            return;
        }

        // Di luar layar: tempel di tepi pada arah titik pandu dari tengah layar, panah menunjuk keluar.
        marker.anchoredPosition = direction * scale - direction * bob;
        arrow.gameObject.SetActive(true);
        diamond.gameObject.SetActive(false);
        // Ujung sprite segitiga menunjuk ke bawah (-90 derajat), jadi diputar +90 dari sudut arah.
        arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f);
    }

    // F1: garis emas = rute ke portal, tiang abu = titik yang sudah lewat, biru = berikutnya, putih = yang ditunjuk.
    private void HandleEndCameraRendering(ScriptableRenderContext context, Camera renderCamera)
    {
        if (!drawDebugPath || !AIDebugSettings.Enabled || !IsVisible || !guideReady || renderCamera.cameraType != CameraType.Game)
            return;

        Material material = DungeonGuide.DebugMaterial;

        if (material == null)
            return;

        GL.PushMatrix();
        material.SetPass(0);
        GL.LoadProjectionMatrix(renderCamera.projectionMatrix);
        GL.modelview = renderCamera.worldToCameraMatrix;
        GL.Begin(GL.LINES);
        guide.DrawDebugLines(new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.9f), markerHeight);
        GL.End();
        GL.PopMatrix();
    }
}
