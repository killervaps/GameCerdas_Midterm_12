using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Kartu hadiah chest vault (Mode Dungeon). Bentuknya seperti kartu gate: pita warna rarity, nama gate, cap
// "Tanpa trade-off", baris buff dengan lingkaran operator dan nilai sebelum -> sesudah, serta daftar trade-off
// yang dibuang. Muncul di tengah layar, lalu mengecil dan meluncur ke baris Vault di HUD kiri atas supaya pemain
// melihat hadiah masuk ke build. Chest kosong memakai kartu yang sama dengan pita abu-abu.
public class VaultRewardCard : UIScreen
{
    private const float Width = 480f;

    [SerializeField] private float holdDuration = 3.5f;
    [SerializeField] private float flyDuration = 0.55f;

    private static readonly Color EmptyBand = new Color(0.55f, 0.58f, 0.64f);

    private RectTransform card;
    private CanvasGroup cardGroup;
    private Image band;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI rarityText;
    private GameObject capRow;
    private GameObject noTradeOffStamp;
    private TextMeshProUGUI noteText;
    private RectTransform rows;
    private TextMeshProUGUI droppedText;
    private SigmaRainbow rainbow;

    private float time;
    private bool flying;
    private Vector2 flyTarget;
    private readonly StringBuilder builder = new StringBuilder();

    private static readonly Vector2 HomePosition = new Vector2(0f, 40f);

    public bool IsPlaying { get; private set; }

    protected override void Build()
    {
        Image panel = UIKit.CreatePanel("Card", rect, UIKit.Panel);
        card = panel.rectTransform;
        UIKit.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), HomePosition, new Vector2(Width, 0f));
        int ink = Mathf.RoundToInt(UIKit.InkWidth);
        UIKit.AddVertical(panel.gameObject, 0f, new RectOffset(ink, ink, ink, 20));
        UIKit.FitHeight(panel.gameObject);
        UIKit.SetShadowDistance(panel, new Vector2(7f, -8f));
        cardGroup = panel.gameObject.AddComponent<CanvasGroup>();
        cardGroup.blocksRaycasts = false;
        cardGroup.interactable = false;

        // Pita kepala selebar kartu, tingginya mengikuti nama (nama panjang turun ke baris kedua).
        band = UIKit.SetCorners(UIKit.CreateImage("Band", panel.transform, Color.white), UIKit.InkTopBand);
        UIKit.AddHorizontal(band.gameObject, 12f, new RectOffset(20, 20, 10, 14), TextAnchor.MiddleLeft);
        band.gameObject.AddComponent<LayoutElement>().minHeight = 70f;

        nameText = UIKit.UseDisplayFont(UIKit.CreateText("Name", band.transform, string.Empty, 30f, UIKit.Ink, TextAlignmentOptions.Left, true));
        nameText.textWrappingMode = TextWrappingModes.Normal;
        nameText.lineSpacing = -14f;
        UIKit.Layout(nameText, -1f, -1f, 1f);

        rarityText = UIKit.UseDisplayFont(UIKit.CreateText("Rarity", band.transform, string.Empty, 18f, UIKit.Ink, TextAlignmentOptions.Right, true));
        rarityText.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(rarityText, -1f, 150f, 0f);

        RectTransform body = UIKit.CreateRect("Body", panel.transform);
        UIKit.AddVertical(body.gameObject, 8f, new RectOffset(20, 20, 14, 0));

        // Catatan singkat + cap krem miring "Tanpa trade-off".
        RectTransform cap = UIKit.CreateRect("CapRow", body);
        capRow = cap.gameObject;
        UIKit.AddHorizontal(capRow, 10f, null, TextAnchor.MiddleLeft);

        noteText = UIKit.CreateText("Note", cap, string.Empty, 18f, UIKit.TextMuted);
        UIKit.Layout(noteText, -1f, -1f, 1f);

        Image noTradeOff = UIKit.CreateStamp("NoTradeOff", cap, 18f, 5f, out TextMeshProUGUI noTradeOffLabel);
        UIKit.SetStamp(noTradeOff, noTradeOffLabel, "Tanpa trade-off", UIKit.Cream);
        noTradeOffStamp = noTradeOff.gameObject;

        rows = UIKit.CreateRect("Buffs", body);
        UIKit.AddVertical(rows.gameObject, 8f);

        droppedText = UIKit.CreateText("Dropped", body, string.Empty, 17f, UIKit.TextMuted);
    }

    // Dipanggil GameUI saat chest dibuka. Stat MC sudah ditambah hadiah saat event ini dikirim, jadi nilai
    // "sebelum" diambil dari snapshot stat yang disimpan GameUI saat pintu vault terbuka.
    public void Play(DungeonVault vault, bool hasBaseline, StatSnapshot baseline)
    {
        if (vault == null)
            return;

        if (rainbow != null)
        {
            Destroy(rainbow);
            rainbow = null;
        }

        MathGateData reward = vault.Reward;

        if (reward == null)
            FillEmpty(vault);
        else
            FillReward(reward, hasBaseline, baseline);

        time = 0f;
        flying = false;
        IsPlaying = true;
        card.anchoredPosition = HomePosition;
        card.localScale = Vector3.one;
        cardGroup.alpha = 1f;

        Show();
        UIKit.ForceRebuild(card);
    }

    public void Stop()
    {
        if (!IsPlaying)
            return;

        IsPlaying = false;
        HideInstant();
    }

    private void FillEmpty(DungeonVault vault)
    {
        PaintBand(EmptyBand);
        nameText.text = "Chest kosong";
        rarityText.text = string.Empty;
        noteText.text = "Pool gate tidak punya gate " + GateRarityStyle.Label(vault.MinRarity) + " ke atas.";
        noTradeOffStamp.SetActive(false);
        UIKit.ClearChildren(rows);
        droppedText.gameObject.SetActive(false);
    }

    private void FillReward(MathGateData reward, bool hasBaseline, StatSnapshot baseline)
    {
        PaintBand(UIKit.RarityColor(reward.Rarity));
        nameText.text = reward.DisplayName;
        rarityText.text = UIKit.RarityLabel(reward.Rarity);

        if (GateRarityStyle.IsAnimated(reward.Rarity))
        {
            rainbow = band.gameObject.AddComponent<SigmaRainbow>();
            rainbow.Bind(band, nameText);
        }

        noteText.text = "Hadiah vault, langsung masuk build";
        noTradeOffStamp.SetActive(true);

        UIKit.ClearChildren(rows);
        PlayerStats stats = ui != null ? ui.Stats : null;
        builder.Clear();

        foreach (ModifierEffect effect in reward.Effects)
        {
            if (effect.IsBuff)
            {
                CreateBuffRow(effect, stats, hasBaseline, baseline);
                continue;
            }

            if (builder.Length > 0)
                builder.Append("   ");

            builder.Append("<s>").Append(UIKit.StatLabel(effect.stat)).Append(' ').Append(effect.Symbol)
                .Append(effect.value.ToString("0.##")).Append("</s>");
        }

        bool anyDropped = builder.Length > 0;
        droppedText.gameObject.SetActive(anyDropped);

        if (anyDropped)
            droppedText.text = builder.ToString() + "   dibuang dari hadiah vault";
    }

    private void PaintBand(Color color)
    {
        Color dark = Color.Lerp(color, UIKit.Ink, 0.82f);
        band.color = color;
        nameText.color = dark;
        rarityText.color = dark;
    }

    // Satu baris buff, sama dengan baris efek di kartu gate: lingkaran operator, nama stat, nilai sebelum -> sesudah,
    // dan garis bawah hijau.
    private void CreateBuffRow(ModifierEffect effect, PlayerStats stats, bool hasBaseline, StatSnapshot baseline)
    {
        Color color = UIKit.Buff;

        Image row = UIKit.SetCorners(UIKit.CreateImage("Buff", rows, UIKit.Well), UIKit.InkMedium);
        HorizontalLayoutGroup layout = UIKit.AddHorizontal(row.gameObject, 12f, new RectOffset(10, 16, 7, 11));
        layout.childAlignment = TextAnchor.MiddleLeft;
        UIKit.Layout(row, 54f);

        Image strip = UIKit.CreateImage("KindStrip", row.transform, color, false);
        strip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        RectTransform stripRect = strip.rectTransform;
        stripRect.anchorMin = new Vector2(0f, 0f);
        stripRect.anchorMax = new Vector2(1f, 0f);
        stripRect.pivot = new Vector2(0.5f, 0f);
        stripRect.offsetMin = new Vector2(8f, 3f);
        stripRect.offsetMax = new Vector2(-8f, 7f);

        UIKit.CreateToken("Operator", row.transform, 34f, color, OperatorGlyph(effect.op), out _);

        TextMeshProUGUI label = UIKit.CreateText("Text", row.transform,
            "<b>" + UIKit.StatLabel(effect.stat) + "</b>  " + effect.Symbol + effect.value.ToString("0.##"), 21f, UIKit.Text);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(label, -1f, -1f, 1f);

        if (stats == null || !hasBaseline)
            return;

        float before = baseline.Get(effect.stat);
        float after = stats.Get(effect.stat);

        TextMeshProUGUI change = UIKit.UseDisplayFont(UIKit.CreateText("Change", row.transform,
            before.ToString("0.##") + " <color=" + UIKit.MutedHex + ">→</color> <color=" + UIKit.BuffHex + ">" + after.ToString("0.##") + "</color>",
            21f, UIKit.Text, TextAlignmentOptions.Right, true));
        change.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(change, -1f, 140f, 0f);
    }

    // Tanda minus tipografis (U+2212) supaya lebarnya sama dengan + × ÷ di lingkaran operator.
    private static string OperatorGlyph(ModifierOp op)
    {
        switch (op)
        {
            case ModifierOp.Add: return "+";
            case ModifierOp.Subtract: return "−";
            case ModifierOp.Multiply: return "×";
            case ModifierOp.Divide: return "÷";
            default: return "?";
        }
    }

    protected override void Update()
    {
        base.Update();

        if (!IsPlaying)
            return;

        // Berhenti di tempat saat game dijeda.
        if (ui != null && ui.IsPaused)
            return;

        time += Time.unscaledDeltaTime;

        if (time < holdDuration)
        {
            // Muncul membesar sesaat, lalu diam di tengah.
            float pop = Mathf.Clamp01(time / 0.25f);
            float scale = Mathf.Lerp(1.2f, 1f, 1f - (1f - pop) * (1f - pop));
            card.localScale = new Vector3(scale, scale, 1f);
            return;
        }

        if (!flying)
        {
            flying = true;
            flyTarget = FindFlyTarget();
        }

        float t = Mathf.Clamp01((time - holdDuration) / flyDuration);
        float eased = t * t;
        card.anchoredPosition = Vector2.Lerp(HomePosition, flyTarget, eased);
        float shrink = Mathf.Lerp(1f, 0.2f, eased);
        card.localScale = new Vector3(shrink, shrink, 1f);
        cardGroup.alpha = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);

        if (t >= 1f)
            Stop();
    }

    // Posisi baris Vault di HUD dalam koordinat layar ini. Jika HUD tersembunyi, meluncur ke kiri atas layar.
    private Vector2 FindFlyTarget()
    {
        Vector2 fallback = new Vector2(-rect.rect.width * 0.5f + 220f, rect.rect.height * 0.5f - 260f);
        HudPanel hud = ui != null ? ui.Hud : null;

        if (hud == null || !hud.IsVisible || !hud.VaultChipsRect.gameObject.activeInHierarchy)
            return fallback;

        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, hud.VaultChipsRect.position);

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out Vector2 local) ? local : fallback;
    }
}
