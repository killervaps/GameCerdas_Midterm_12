using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Kartu detail gate. Muncul saat MC masuk collider trigger GatePortal (lewat event
// GateSelectionRoom.PortalFocused) dan hilang saat keluar. Berisi pita nama + rarity, deskripsi,
// efek per baris (operator asli dan perubahan stat), ancaman ronde berikutnya, serta progress "Tahan E".
// Tidak menjeda game: MC tetap bisa berjalan ke gate sebelah untuk membandingkan.
public class GateInfoModal : UIScreen
{
    private const float Width = 560f;
    private const float SlideDistance = 60f;
    // Di atas jumlah efek ini, baris efek dibuat ringkas (satu baris, lebih pendek).
    private const int CompactAbove = 4;

    private RectTransform card;
    private Image headerBand;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI rarityText;
    private TextMeshProUGUI tagsText;
    private TextMeshProUGUI descriptionText;
    private RectTransform effectsList;
    private TextMeshProUGUI threatText;
    private GameObject threatSection;
    private TextMeshProUGUI confirmText;
    private RectTransform confirmFill;

    private float slide;
    private bool animatedRarity;
    private readonly StringBuilder builder = new StringBuilder();

    protected override void Build()
    {
        Image panel = UIKit.CreatePanel("Card", rect, UIKit.Panel);
        card = panel.rectTransform;
        UIKit.Place(card, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, -20f), new Vector2(Width, 0f));
        int ink = Mathf.RoundToInt(UIKit.InkWidth);
        UIKit.AddVertical(panel.gameObject, 0f, new RectOffset(ink, ink, ink, 22));
        UIKit.FitHeight(panel.gameObject);

        // Pita kepala selebar kartu, warnanya mengikuti rarity. Menggantikan garis atas + pill rarity
        // sebelumnya, jadi rarity cukup ditandai satu kali.
        headerBand = UIKit.SetCorners(UIKit.CreateImage("HeaderBand", panel.transform, Color.white), UIKit.InkTopBand);
        UIKit.AddHorizontal(headerBand.gameObject, 12f, new RectOffset(20, 20, 10, 14), TextAnchor.MiddleLeft);

        // Tinggi pita mengikuti nama: minimal satu baris, bertambah jika nama gate panjang turun ke baris kedua.
        LayoutElement bandLayout = headerBand.gameObject.AddComponent<LayoutElement>();
        bandLayout.minHeight = 74f;

        // Nama panjang dibungkus ke baris berikutnya, tidak dipotong. Jangan pakai overflow Ellipsis di sini:
        // mode itu menyembunyikan seluruh baris jika tinggi baris Fredoka tidak muat.
        nameText = UIKit.UseDisplayFont(UIKit.CreateText("Name", headerBand.transform, string.Empty, 32f, UIKit.Ink, TextAlignmentOptions.Left, true));
        nameText.textWrappingMode = TextWrappingModes.Normal;
        nameText.overflowMode = TextOverflowModes.Overflow;
        nameText.lineSpacing = -14f;
        UIKit.Layout(nameText, -1f, -1f, 1f);

        rarityText = UIKit.UseDisplayFont(UIKit.CreateText("Rarity", headerBand.transform, string.Empty, 20f, UIKit.Ink, TextAlignmentOptions.Right, true));
        rarityText.textWrappingMode = TextWrappingModes.NoWrap;
        // Cukup untuk label terpanjang, "Sangat Langka".
        UIKit.Layout(rarityText, -1f, 160f, 0f);

        RectTransform body = UIKit.CreateRect("Body", panel.transform);
        UIKit.AddVertical(body.gameObject, 10f, new RectOffset(20, 20, 14, 0));

        tagsText = UIKit.CreateText("Tags", body, string.Empty, 19f, UIKit.TextMuted);

        descriptionText = UIKit.CreateText("Description", body, string.Empty, 22f, new Color(0.82f, 0.85f, 0.9f));

        effectsList = UIKit.CreateRect("Effects", body);
        UIKit.AddVertical(effectsList.gameObject, 8f);

        threatSection = UIKit.CreateRect("ThreatSection", body).gameObject;
        UIKit.AddVertical(threatSection, 4f);
        UIKit.CreateText("SectionTitle", threatSection.transform, "Ancaman ronde berikutnya", 19f, UIKit.TextMuted, TextAlignmentOptions.Left, true);
        threatText = UIKit.CreateText("Threat", threatSection.transform, string.Empty, 24f, UIKit.Text);

        // Kaki kartu: tutup tombol [E] emas + bar progress di sumur tinta.
        confirmText = UIKit.CreateText("Confirm", body, string.Empty, 20f, UIKit.Text, TextAlignmentOptions.Left);

        RectTransform confirmRow = UIKit.CreateRect("ConfirmRow", body);
        UIKit.AddHorizontal(confirmRow.gameObject, 10f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(confirmRow, 40f);

        Image keyCap = UIKit.SetCorners(UIKit.CreateImage("KeyCap", confirmRow, UIKit.Accent), UIKit.InkSmall);
        UIKit.Layout(keyCap, 38f, 40f, 0f);
        TextMeshProUGUI keyLabel = UIKit.UseDisplayFont(UIKit.CreateText("Key", keyCap.transform, "E", 24f,
            new Color(0.23f, 0.15f, 0.02f), TextAlignmentOptions.Center, true));
        UIKit.Stretch(keyLabel.rectTransform);

        RectTransform bar = UIKit.CreateBar("ConfirmBar", confirmRow, UIKit.Ink, UIKit.Accent, out Image fill);
        UIKit.Layout(bar, 14f, -1f, 1f);
        confirmFill = fill.rectTransform;
        UIKit.SetBarFill(confirmFill, 0f);
    }

    public void Open(MathGateData gate)
    {
        if (gate == null)
        {
            Hide();
            return;
        }

        Fill(gate);

        if (!IsVisible)
            slide = 1f;

        Show();
        UIKit.ForceRebuild(card);
    }

    private void Fill(MathGateData gate)
    {
        animatedRarity = GateRarityStyle.IsAnimated(gate.Rarity);

        nameText.text = gate.DisplayName;
        rarityText.text = UIKit.RarityLabel(gate.Rarity);
        PaintHeader(UIKit.RarityColor(gate.Rarity));

        builder.Clear();

        foreach (string tag in gate.PreviewTags)
        {
            if (string.IsNullOrEmpty(tag))
                continue;

            if (builder.Length > 0)
                builder.Append("   ");

            builder.Append('#').Append(tag);
        }

        tagsText.text = builder.ToString();
        tagsText.gameObject.SetActive(builder.Length > 0);

        descriptionText.text = gate.Description;
        descriptionText.gameObject.SetActive(!string.IsNullOrEmpty(gate.Description));

        BuildEffects(gate);
        BuildThreat(gate);
    }

    // Teks di pita memakai versi gelap dari warna rarity, bukan hitam polos.
    private void PaintHeader(Color rarityColor)
    {
        Color bandText = Color.Lerp(rarityColor, UIKit.Ink, 0.82f);

        headerBand.color = rarityColor;
        nameText.color = bandText;
        rarityText.color = bandText;
    }

    // Satu baris per efek: lingkaran operator asli (+ − × ÷), nama stat dan jenisnya, lalu nilai
    // sekarang -> sesudah gate. Garis bawah berwarna menandai buff (hijau) atau trade-off (merah).
    // Gate dengan banyak efek memakai baris ringkas satu baris supaya kartu tidak melewati tinggi layar.
    private void BuildEffects(MathGateData gate)
    {
        UIKit.ClearChildren(effectsList);

        bool compact = gate.Effects.Count > CompactAbove;
        float rowHeight = compact ? 48f : 60f;
        float iconSize = compact ? 32f : 38f;

        PlayerStats stats = ui.Stats;
        bool hasPreview = stats != null;
        StatSnapshot preview = hasPreview ? stats.Preview(gate) : default;

        foreach (ModifierEffect effect in gate.Effects)
        {
            bool buff = effect.IsBuff;
            bool tradeOff = effect.IsTradeOff;
            Color color = buff ? UIKit.Buff : tradeOff ? UIKit.TradeOff : UIKit.Neutral;
            string hex = buff ? UIKit.BuffHex : tradeOff ? UIKit.TradeOffHex : UIKit.MutedHex;
            Color dark = Color.Lerp(color, UIKit.Ink, 0.8f);

            Image row = UIKit.SetCorners(UIKit.CreateImage("Effect", effectsList, UIKit.Well), UIKit.InkMedium);
            HorizontalLayoutGroup layout = UIKit.AddHorizontal(row.gameObject, 12f, new RectOffset(10, 16, compact ? 6 : 8, compact ? 10 : 12));
            layout.childAlignment = TextAnchor.MiddleLeft;
            UIKit.Layout(row, rowHeight);

            Image strip = UIKit.CreateImage("KindStrip", row.transform, color, false);
            strip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform stripRect = strip.rectTransform;
            stripRect.anchorMin = new Vector2(0f, 0f);
            stripRect.anchorMax = new Vector2(1f, 0f);
            stripRect.pivot = new Vector2(0.5f, 0f);
            stripRect.offsetMin = new Vector2(8f, 3f);
            stripRect.offsetMax = new Vector2(-8f, 7f);

            Image icon = UIKit.SetCircle(UIKit.CreateImage("Operator", row.transform, color));
            UIKit.Layout(icon, iconSize, iconSize, 0f);
            TextMeshProUGUI symbol = UIKit.UseDisplayFont(UIKit.CreateText("Symbol", icon.transform, OperatorGlyph(effect.op), iconSize * 0.68f,
                dark, TextAlignmentOptions.Center, true));
            UIKit.Stretch(symbol.rectTransform, 0f, 0f, 0f, 3f);

            string kind = "<size=75%><color=" + hex + ">" + (buff ? "Buff" : tradeOff ? "Trade-off" : "Netral") + "</color></size>";
            TextMeshProUGUI label = UIKit.CreateText("Text", row.transform,
                "<b>" + UIKit.StatLabel(effect.stat) + "</b>  " + effect.Symbol + effect.value.ToString("0.##") +
                (compact ? "  " : "\n") + kind,
                compact ? 20f : 22f, UIKit.Text);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.lineSpacing = -8f;
            UIKit.Layout(label, -1f, -1f, 1f);

            if (hasPreview)
            {
                float now = stats.Get(effect.stat);
                float after = preview.Get(effect.stat);

                TextMeshProUGUI change = UIKit.UseDisplayFont(UIKit.CreateText("Change", row.transform,
                    now.ToString("0.##") + " <color=" + UIKit.MutedHex + ">→</color> <color=" + hex + ">" + after.ToString("0.##") + "</color>",
                    22f, UIKit.Text, TextAlignmentOptions.Right, true));
                change.textWrappingMode = TextWrappingModes.NoWrap;
                UIKit.Layout(change, -1f, 140f, 0f);
            }
        }
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

    private void BuildThreat(MathGateData gate)
    {
        ThreatEstimator estimator = ui.Threat;
        PlayerStats stats = ui.Stats;
        WaveData wave = ui.GateRoom != null ? ui.GateRoom.UpcomingWave : null;

        if (estimator == null || stats == null || wave == null)
        {
            threatSection.SetActive(false);
            return;
        }

        threatSection.SetActive(true);

        ThreatResult now = estimator.Evaluate(wave, stats.GetSnapshot());
        ThreatResult after = estimator.Evaluate(wave, stats.Preview(gate));

        threatText.text =
            "<color=" + now.ColorHex + ">" + now.Label + "</color> <size=75%><color=" + UIKit.MutedHex + ">" + UIKit.FormatRatio(now.ratio) + "</color></size>" +
            "   <color=" + UIKit.MutedHex + ">→</color>   " +
            "<b><color=" + after.ColorHex + ">" + after.Label + "</color></b> <size=75%><color=" + UIKit.MutedHex + ">" + UIKit.FormatRatio(after.ratio) + "</color></size>" +
            "\n<size=75%><color=" + UIKit.MutedHex + ">" + wave.Describe() + "</color></size>";
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible && group.alpha <= 0f)
            return;

        // Geser masuk dari kanan.
        slide = Mathf.MoveTowards(slide, IsVisible ? 0f : 1f, 6f * Time.unscaledDeltaTime);
        float eased = slide * slide;
        card.anchoredPosition = new Vector2(-40f + eased * SlideDistance, -20f);

        // Pita SIGMA berganti warna terus seperti lampu RGB.
        if (animatedRarity)
            PaintHeader(GateRarityStyle.SigmaColor());

        float progress = ui.GateRoom != null ? ui.GateRoom.ConfirmProgress : 0f;
        UIKit.SetBarFill(confirmFill, progress);
        confirmText.text = progress > 0.01f
            ? "Memilih... <b>" + Mathf.RoundToInt(progress * 100f) + "%</b>"
            : "Tahan <b>E</b> untuk memilih gate ini";
    }
}
