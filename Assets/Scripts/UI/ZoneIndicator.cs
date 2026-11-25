using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Penanda zona Hold Zone, selalu tampil selama ronde Hold Zone. Pandunya berurutan lewat DungeonGuide, sama dengan
// penanda Goal: satu titik pada satu waktu (pintu keluar room, belokan koridor, pintu masuk room), lalu pusat zona.
// Di dalam zona penanda tetap di pusat zona. Warna mengikuti status zona: biru = dikuasai MC, kuning = contested,
// abu = kosong. Label berisi progres zona dan jarak jalan.
public class ZoneIndicator : UIScreen
{
    private static readonly Color PlayerColor = new Color(0.3f, 0.65f, 1f);
    private static readonly Color ContestedColor = new Color(1f, 0.8f, 0.2f);
    private static readonly Color EmptyColor = new Color(0.75f, 0.75f, 0.8f);

    [SerializeField] private float edgeMargin = 80f;
    [SerializeField] private float markerHeight = 2.2f;
    [SerializeField] private float arrowSize = 40f;
    [SerializeField] private float diamondSize = 28f;
    [SerializeField] private float bobAmount = 5f;

    [Header("Pandu Dungeon")]
    [SerializeField, Min(0.05f)] private float guideInterval = 0.5f;
    [SerializeField, Min(0)] private int reachCells = 1;
    [SerializeField, Min(1)] private int strayCells = 4;
    [SerializeField, Range(0f, 0.45f)] private float sightClearance = 0.3f;

    [Header("Debug (F1)")]
    [SerializeField] private bool drawDebugPath = true;

    private readonly DungeonGuide guide = new DungeonGuide { targetName = "zona" };
    private float nextGuideUpdate;
    private bool guideReady;
    private float guideMeters;
    private HoldZone guidedZone;

    private RectTransform marker;
    private CanvasGroup content;
    private RectTransform arrow;
    private RectTransform diamond;
    private Image arrowFill;
    private Image diamondFill;
    private Image labelStamp;
    private TextMeshProUGUI label;
    private Camera cam;

    protected override void Build()
    {
        marker = UIKit.CreateRect("Marker", rect);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(arrowSize, arrowSize);
        content = marker.gameObject.AddComponent<CanvasGroup>();
        content.blocksRaycasts = false;
        content.alpha = 0f;

        arrow = CreateInkedShape("Arrow", ReticleSprites.TriangleInk, ReticleSprites.Triangle, arrowSize, out arrowFill);
        diamond = CreateInkedShape("Diamond", ReticleSprites.DiamondInk, ReticleSprites.Diamond, diamondSize, out diamondFill);

        labelStamp = UIKit.CreateFreeStamp("Label", marker, "ZONA", 18f, EmptyColor, 0f, out label);
        RectTransform labelRect = labelStamp.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -(arrowSize * 0.5f + 6f));
    }

    private RectTransform CreateInkedShape(string shapeName, Sprite inkSprite, Sprite fillSprite, float size, out Image fill)
    {
        RectTransform root = UIKit.CreateRect(shapeName, marker);
        root.sizeDelta = new Vector2(size, size);

        Image ink = UIKit.CreateImage("Ink", root, UIKit.Ink, false);
        ink.sprite = inkSprite;
        UIKit.Stretch(ink.rectTransform);

        fill = UIKit.CreateImage("Fill", root, EmptyColor, false);
        fill.sprite = fillSprite;
        UIKit.Stretch(fill.rectTransform);

        return root;
    }

    protected override void OnShow()
    {
        guide.Reset();
        guideReady = false;
        guidedZone = null;
        nextGuideUpdate = 0f;
        content.alpha = 0f;
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
    }

    private static HoldZoneObjective ActiveObjective =>
        ObjectiveRunner.Current != null ? ObjectiveRunner.Current.Active as HoldZoneObjective : null;

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        HoldZoneObjective objective = ActiveObjective;
        HoldZone zone = objective != null ? objective.Zone : null;
        PlayerHealth player = PlayerHealth.Current;

        if (cam == null || !cam.isActiveAndEnabled)
            cam = Camera.main;

        if (zone == null || cam == null)
        {
            content.alpha = 0f;
            return;
        }

        UpdateGuide(zone, player);

        // MC di dalam zona: penanda tetap di pusat zona.
        Vector3 point = guideReady && !zone.PlayerInside ? guide.CurrentPoint : zone.transform.position;
        float meters = guideReady ? guideMeters : player != null ? Vector3.Distance(player.transform.position, zone.transform.position) : 0f;

        Color color = zone.State == ZoneState.Player ? PlayerColor : zone.State == ZoneState.Contested ? ContestedColor : EmptyColor;
        arrowFill.color = color;
        diamondFill.color = color;
        UIKit.SetStamp(labelStamp, label, "ZONA " + Mathf.FloorToInt(zone.Progress01 * 100f) + "%" +
                                          (zone.PlayerInside ? string.Empty : "  " + Mathf.CeilToInt(meters) + " m"), color);

        content.alpha = Mathf.MoveTowards(content.alpha, 1f, 8f * Time.unscaledDeltaTime);

        bool onScreen = WaypointProjection.Project(cam, rect, edgeMargin, point + Vector3.up * markerHeight,
            out Vector2 local, out Vector2 direction, out float scale);
        float bob = Mathf.Sin(Time.unscaledTime * 4f) * bobAmount;

        if (onScreen)
        {
            marker.anchoredPosition = local + Vector2.up * bob;
            arrow.gameObject.SetActive(false);
            diamond.gameObject.SetActive(true);
            return;
        }

        marker.anchoredPosition = direction * scale - direction * bob;
        arrow.gameObject.SetActive(true);
        diamond.gameObject.SetActive(false);
        arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f);
    }

    private void UpdateGuide(HoldZone zone, PlayerHealth player)
    {
        if (zone != guidedZone)
        {
            guide.Reset();
            guidedZone = zone;
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

        Vector3 target = zone.transform.position;

        if (!guide.Search(dungeon, player.transform.position) || !DungeonGuide.TryCellOf(dungeon, target, out Vector2Int targetCell))
            return;

        guideReady = guide.Follow(dungeon, targetCell, target);
        guideMeters = guide.TargetDistance * dungeon.TileSize;
    }

    // F1: rute ke zona (biru), titik pandu, dan titik yang sedang ditunjuk.
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
        guide.DrawDebugLines(new Color(PlayerColor.r, PlayerColor.g, PlayerColor.b, 0.9f), markerHeight);
        GL.End();
        GL.PopMatrix();
    }
}
