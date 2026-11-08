using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Penanda arah portal Goal di Mode Dungeon. Hanya muncul setelah ronde bersih dan portal terbuka, jadi selama
// pertempuran pemain tetap menjelajah sendiri. Portal terlihat di layar = belah ketupat di atas portal,
// portal di luar layar = panah di tepi layar yang menunjuk ke arahnya. Keduanya menampilkan jarak dalam meter.
// Saat MC berada di dekat portal, kartu "Tahan E" muncul di bawah layar dengan bar progress seperti kartu buff gate,
// plus pengingat jika vault ronde ini belum dibuka.
public class GoalIndicator : UIScreen
{
    [SerializeField] private float edgeMargin = 80f;
    [SerializeField] private float markerHeight = 2.2f;
    [SerializeField] private float arrowSize = 44f;
    [SerializeField] private float diamondSize = 30f;
    [SerializeField] private float bobAmount = 6f;

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

    private void UpdatePrompt(DungeonGoalPortal portal)
    {
        bool show = portal != null && portal.PlayerInRange;
        promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, show ? 1f : 0f, 8f * Time.unscaledDeltaTime);

        if (!show)
            return;

        float progress = portal.ConfirmProgress;
        UIKit.SetBarFill(promptFill, progress);
        ControlGlyphs.Apply(promptText);
        promptText.text = progress > 0.01f
            ? "Kembali ke Safezone... <b>" + Mathf.RoundToInt(progress * 100f) + "%</b>"
            : "Tahan <b>" + ControlHints.Glyph(PadAction.Interact) + "</b> untuk kembali ke Safezone";
        ControlHints.StyleCap(promptKeyCap, promptKeyLabel, PadAction.Interact);

        DungeonVault vault = DungeonVault.Active;
        bool vaultLeft = vault != null && vault.State != VaultState.Looted;
        promptNote.text = vaultLeft ? "Vault belum dibuka. Hadiahnya hilang jika ronde diakhiri." : string.Empty;

        if (promptNote.gameObject.activeSelf != vaultLeft)
        {
            promptNote.gameObject.SetActive(vaultLeft);
            UIKit.ForceRebuild(prompt);
        }
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        DungeonGoalPortal portal = ui.Run != null ? ui.Run.OpenPortal : null;
        PlayerHealth player = PlayerHealth.Current;

        UpdatePrompt(portal);

        if (cam == null || !cam.isActiveAndEnabled)
            cam = Camera.main;

        if (portal == null || cam == null || !ShowMarker)
        {
            content.alpha = 0f;
            return;
        }

        content.alpha = 1f;

        Vector3 world = portal.transform.position + Vector3.up * markerHeight;
        Vector3 screen = cam.WorldToScreenPoint(world);
        bool behind = screen.z < 0f;

        if (behind)
            screen = -screen;

        Vector2 canvasSize = rect.rect.size;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out Vector2 local);

        Vector2 half = canvasSize * 0.5f - new Vector2(edgeMargin, edgeMargin);
        bool onScreen = !behind && Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;

        float meters = player != null ? Vector3.Distance(player.transform.position, portal.transform.position) : 0f;
        label.text = "GOAL  " + Mathf.CeilToInt(meters) + " m";

        float bob = Mathf.Sin(Time.unscaledTime * 4f) * bobAmount;

        if (onScreen)
        {
            marker.anchoredPosition = local + Vector2.up * bob;
            arrow.gameObject.SetActive(false);
            diamond.gameObject.SetActive(true);
            return;
        }

        // Di luar layar: tempel di tepi pada arah portal dari tengah layar, panah menunjuk keluar.
        Vector2 direction = local.sqrMagnitude > 0.01f ? local.normalized : Vector2.up;
        float scale = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(direction.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(direction.y)));

        marker.anchoredPosition = direction * scale - direction * bob;
        arrow.gameObject.SetActive(true);
        diamond.gameObject.SetActive(false);
        // Ujung sprite segitiga menunjuk ke bawah (-90 derajat), jadi diputar +90 dari sudut arah.
        arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f);
    }
}
