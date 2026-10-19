using System.Text;
using UnityEngine;

// Menyusun teks kartu gate dengan rich text yang dipahami TextMeshPro maupun IMGUI.
// Hanya memakai karakter ASCII/Latin-1 supaya tidak ada glyph hilang di font TMP bawaan.
public static class GateTextFormatter
{
    private const string BuffColor = "#55dd55";
    private const string TradeOffColor = "#ff6666";
    private const string MutedColor = "#aaaaaa";

    private static readonly StringBuilder builder = new StringBuilder();

    public static string BuildCard(MathGateData gate, PlayerStats stats)
    {
        return BuildCard(gate, stats, null);
    }

    // Dengan threat, kartu menampilkan label risiko wave berikutnya jika gate ini dipilih.
    public static string BuildCard(MathGateData gate, PlayerStats stats, ThreatResult? threat)
    {
        builder.Clear();

        builder.Append("<b>").Append(gate.DisplayName).Append("</b>\n");
        builder.Append("<size=70%><color=").Append(MutedColor).Append('>').Append(gate.Rarity);

        foreach (string tag in gate.PreviewTags)
            builder.Append(" | ").Append(tag);

        builder.Append("</color></size>\n");

        foreach (ModifierEffect effect in gate.Effects)
        {
            string color = effect.IsBuff ? BuffColor : effect.IsTradeOff ? TradeOffColor : MutedColor;
            builder.Append("<color=").Append(color).Append('>').Append(effect).Append("</color>\n");
        }

        if (threat.HasValue)
        {
            ThreatResult t = threat.Value;
            builder.Append("<b>Ancaman <color=").Append(t.ColorHex).Append('>').Append(t.Label)
                .Append("</color></b> <size=70%>(").Append(t.ratio.ToString("0.00")).Append(")</size>\n");
        }

        if (stats != null)
            AppendPreview(gate, stats);

        return builder.ToString();
    }

    // Hanya stat yang berubah yang ditampilkan, mis. "Defense 10 -> 15".
    private static void AppendPreview(MathGateData gate, PlayerStats stats)
    {
        StatSnapshot preview = stats.Preview(gate);

        builder.Append("<size=70%>");

        for (int i = 0; i < StatTypes.Count; i++)
        {
            StatType stat = (StatType)i;
            float now = stats.Get(stat);
            float after = preview.Get(stat);

            if (Mathf.Approximately(now, after))
                continue;

            string color = after > now ? BuffColor : TradeOffColor;

            builder.Append(stat).Append(' ').Append(now.ToString("0.##"))
                .Append(" -> <color=").Append(color).Append('>').Append(after.ToString("0.##")).Append("</color>\n");
        }

        builder.Append("</size>");
    }
}
