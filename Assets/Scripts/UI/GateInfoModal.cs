using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Kartu detail gate. Muncul saat MC masuk collider trigger GatePortal (lewat event
// GateSelectionRoom.PortalFocused) dan hilang saat keluar. Berisi deskripsi, efek buff/trade-off,
// perubahan stat, ancaman ronde berikutnya, serta progress "Tahan E".
// Tidak menjeda game: MC tetap bisa berjalan ke gate sebelah untuk membandingkan.
public class GateInfoModal : UIScreen
{
    private const float Width = 560f;
    private const float SlideDistance = 60f;

    private RectTransform card;
    private Image rarityStripe;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI rarityText;
    private Image rarityPill;
    private TextMeshProUGUI tagsText;
    private TextMeshProUGUI descriptionText;
    private RectTransform effectsList;
    private TextMeshProUGUI statsText;
    private TextMeshProUGUI threatText;
    private GameObject threatSection;
    private TextMeshProUGUI confirmText;
    private RectTransform confirmFill;

    private float slide;
    private readonly StringBuilder builder = new StringBuilder();

    protected override void Build()
    {
        Image panel = UIKit.CreatePanel("Card", rect, UIKit.Panel);
        card = panel.rectTransform;
        UIKit.Place(card, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, -20f), new Vector2(Width, 0f));
        UIKit.AddVertical(panel.gameObject, 10f, new RectOffset(28, 28, 30, 24));
        UIKit.FitHeight(panel.gameObject);

        rarityStripe = UIKit.CreateImage("RarityStripe", panel.transform, Color.white);
        LayoutElement stripeLayout = rarityStripe.gameObject.AddComponent<LayoutElement>();
        stripeLayout.ignoreLayout = true;
        RectTransform stripe = rarityStripe.rectTransform;
        stripe.anchorMin = new Vector2(0f, 1f);
        stripe.anchorMax = new Vector2(1f, 1f);
        stripe.pivot = new Vector2(0.5f, 1f);
        stripe.offsetMin = new Vector2(0f, -8f);
        stripe.offsetMax = new Vector2(0f, 0f);

        // Kepala kartu: nama + label rarity.
        RectTransform header = UIKit.CreateRect("Header", panel.transform);
        UIKit.AddHorizontal(header.gameObject, 12f);
        UIKit.Layout(header, 48f);

        nameText = UIKit.CreateText("Name", header, string.Empty, 38f, UIKit.Text, TextAlignmentOptions.Left, true);
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        UIKit.Layout(nameText, 48f, -1f, 1f);

        rarityPill = UIKit.CreateImage("RarityPill", header, Color.white);
        UIKit.Layout(rarityPill, 32f, 110f, 0f);
        rarityText = UIKit.CreateText("Label", rarityPill.transform, string.Empty, 17f, new Color(0.06f, 0.07f, 0.1f), TextAlignmentOptions.Center, true);
        rarityText.characterSpacing = 2f;
        UIKit.Stretch(rarityText.rectTransform);

        tagsText = UIKit.CreateText("Tags", panel.transform, string.Empty, 19f, UIKit.TextMuted);

        descriptionText = UIKit.CreateText("Description", panel.transform, string.Empty, 22f, new Color(0.82f, 0.85f, 0.9f));
        descriptionText.fontStyle = FontStyles.Italic;

        UIKit.CreateDivider(panel.transform);

        SectionTitle(panel.transform, "EFEK GATE");
        effectsList = UIKit.CreateRect("Effects", panel.transform);
        UIKit.AddVertical(effectsList.gameObject, 6f);

        SectionTitle(panel.transform, "PERUBAHAN STAT");
        statsText = UIKit.CreateText("Stats", panel.transform, string.Empty, 22f, UIKit.Text);

        threatSection = UIKit.CreateRect("ThreatSection", panel.transform).gameObject;
        UIKit.AddVertical(threatSection, 4f);
        SectionTitle(threatSection.transform, "ANCAMAN RONDE BERIKUTNYA");
        threatText = UIKit.CreateText("Threat", threatSection.transform, string.Empty, 24f, UIKit.Text);

        UIKit.CreateDivider(panel.transform);

        // Kaki kartu: petunjuk tahan E dengan progress.
        confirmText = UIKit.CreateText("Confirm", panel.transform, "Tahan <b>[E]</b> untuk memilih gate ini", 22f, UIKit.Accent, TextAlignmentOptions.Center, true);

        RectTransform bar = UIKit.CreateBar("ConfirmBar", panel.transform, new Color(0f, 0f, 0f, 0.45f), UIKit.Accent, out Image fill);
        UIKit.Layout(bar, 10f);
        confirmFill = fill.rectTransform;
        UIKit.SetBarFill(confirmFill, 0f);
    }

    private static void SectionTitle(Transform parent, string text)
    {
        TextMeshProUGUI label = UIKit.CreateText("SectionTitle", parent, text, 17f, UIKit.TextMuted, TextAlignmentOptions.Left, true);
        label.characterSpacing = 3f;
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
        Color rarityColor = UIKit.RarityColor(gate.Rarity);

        rarityStripe.color = rarityColor;
        rarityPill.color = rarityColor;
        rarityText.text = UIKit.RarityLabel(gate.Rarity);
        nameText.text = gate.DisplayName;

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
        statsText.text = BuildStatChanges(gate);
        BuildThreat(gate);
    }

    private void BuildEffects(MathGateData gate)
    {
        UIKit.ClearChildren(effectsList);

        foreach (ModifierEffect effect in gate.Effects)
        {
            bool buff = effect.IsBuff;
            bool tradeOff = effect.IsTradeOff;
            Color color = buff ? UIKit.Buff : tradeOff ? UIKit.TradeOff : UIKit.Neutral;

            Image row = UIKit.CreateImage("Effect", effectsList, new Color(color.r, color.g, color.b, 0.12f));
            HorizontalLayoutGroup layout = UIKit.AddHorizontal(row.gameObject, 12f, new RectOffset(12, 14, 6, 6));
            layout.childAlignment = TextAnchor.MiddleLeft;
            UIKit.Layout(row, 46f);

            Image icon = UIKit.CreateImage("Icon", row.transform, color);
            UIKit.Layout(icon, 30f, 30f, 0f);
            TextMeshProUGUI symbol = UIKit.CreateText("Symbol", icon.transform, buff ? "+" : tradeOff ? "-" : "=", 24f,
                new Color(0.06f, 0.07f, 0.1f), TextAlignmentOptions.Center, true);
            UIKit.Stretch(symbol.rectTransform, 0f, 0f, 0f, 2f);

            TextMeshProUGUI label = UIKit.CreateText("Text", row.transform,
                "<b>" + UIKit.StatLabel(effect.stat) + "</b>  " + effect.Symbol + effect.value.ToString("0.##"), 24f, UIKit.Text);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Layout(label, -1f, -1f, 1f);

            TextMeshProUGUI kind = UIKit.CreateText("Kind", row.transform, buff ? "BUFF" : tradeOff ? "TRADE-OFF" : "NETRAL",
                16f, color, TextAlignmentOptions.Right, true);
            kind.characterSpacing = 2f;
            UIKit.Layout(kind, -1f, 120f, 0f);
        }
    }

    // Hanya stat yang berubah, mis. "Defense   10 -> 15  (+5)".
    private string BuildStatChanges(MathGateData gate)
    {
        PlayerStats stats = ui.Stats;

        if (stats == null)
            return "-";

        StatSnapshot preview = stats.Preview(gate);
        builder.Clear();

        for (int i = 0; i < StatTypes.Count; i++)
        {
            StatType stat = (StatType)i;
            float now = stats.Get(stat);
            float after = preview.Get(stat);

            if (Mathf.Approximately(now, after))
                continue;

            string color = after > now ? UIKit.BuffHex : UIKit.TradeOffHex;
            float delta = after - now;

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(UIKit.StatLabel(stat))
                .Append("<pos=38%>").Append(now.ToString("0.##"))
                .Append("<pos=54%><color=").Append(UIKit.MutedHex).Append(">-></color>")
                .Append("<pos=64%><color=").Append(color).Append("><b>").Append(after.ToString("0.##")).Append("</b>")
                .Append("<pos=82%><size=80%>(").Append(delta > 0f ? "+" : string.Empty).Append(delta.ToString("0.##")).Append(")</size></color>");
        }

        return builder.Length > 0 ? builder.ToString() : "<color=" + UIKit.MutedHex + ">Tidak ada stat yang berubah.</color>";
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
            "   <color=" + UIKit.MutedHex + ">-></color>   " +
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

        float progress = ui.GateRoom != null ? ui.GateRoom.ConfirmProgress : 0f;
        UIKit.SetBarFill(confirmFill, progress);
        confirmText.text = progress > 0.01f
            ? "Memilih... <b>" + Mathf.RoundToInt(progress * 100f) + "%</b>"
            : "Tahan <b>[E]</b> untuk memilih gate ini";
    }
}
