using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Menu jeda (Esc). Time.timeScale diatur GameUI, layar ini hanya menampilkan pilihan
// dan ringkasan build saat ini supaya pemain bisa berpikir ulang tanpa tekanan waktu.
public class PauseScreen : UIScreen
{
    private TextMeshProUGUI info;
    private TextMeshProUGUI statsText;
    private TextMeshProUGUI gatesText;

    private readonly StringBuilder builder = new StringBuilder();

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 560f));
        HorizontalLayoutGroup row = UIKit.AddHorizontal(panel.gameObject, 36f, new RectOffset(44, 44, 40, 40), TextAnchor.UpperLeft);
        row.childForceExpandHeight = true;

        // Kiri: judul dan tombol.
        RectTransform left = UIKit.CreateRect("Actions", panel.transform);
        UIKit.Layout(left, -1f, 380f, 0f);
        UIKit.AddVertical(left.gameObject, 14f);

        // Judul berupa stiker krem miring. Dibungkus baris horizontal supaya stiker tidak ikut melebar.
        RectTransform titleRow = UIKit.CreateRect("TitleRow", left);
        UIKit.AddHorizontal(titleRow.gameObject, 0f, new RectOffset(4, 0, 4, 8), TextAnchor.MiddleLeft);
        Image title = UIKit.CreateStamp("Title", titleRow, 56f, -3f, out TextMeshProUGUI titleLabel);
        UIKit.SetStamp(title, titleLabel, "Jeda", UIKit.Cream);
        UIKit.SetShadowDistance(title, new Vector2(6f, -7f));

        info = UIKit.CreateText("Info", left, string.Empty, 22f, UIKit.TextMuted);
        UIKit.Layout(info, 40f);

        RectTransform spacer = UIKit.CreateRect("Spacer", left);
        UIKit.Layout(spacer, 10f);

        Button resume = UIKit.CreateButton("ResumeButton", left, "LANJUTKAN", UIKit.Safe(ui.Resume), 68f, true);
        UIKit.CreateButton("TutorialButton", left, "CARA BERMAIN", UIKit.Safe(ui.OpenTutorial));
        UIKit.CreateButton("RestartButton", left, "ULANGI RUN", UIKit.Safe(ui.RestartRun));
        UIKit.CreateButton("MenuButton", left, "MENU UTAMA", UIKit.Safe(ui.BackToMainMenu));

        firstSelected = resume;

        // Kanan: build saat ini.
        Image card = UIKit.SetCorners(UIKit.CreatePanel("BuildCard", panel.transform, UIKit.Well, false), UIKit.InkMedium);
        UIKit.Layout(card, -1f, -1f, 1f);
        UIKit.AddVertical(card.gameObject, 10f, new RectOffset(28, 28, 24, 24));

        UIKit.UseDisplayFont(UIKit.CreateText("Heading", card.transform, "Build saat ini", 26f, UIKit.Text, TextAlignmentOptions.Left, true));

        statsText = UIKit.CreateText("Stats", card.transform, string.Empty, 24f, UIKit.Text);
        UIKit.CreateDivider(card.transform);

        UIKit.UseDisplayFont(UIKit.CreateText("GatesHeading", card.transform, "Gate dipilih", 26f, UIKit.Text, TextAlignmentOptions.Left, true));

        gatesText = UIKit.CreateText("Gates", card.transform, string.Empty, 22f, UIKit.Text);
        // Gate SIGMA di daftar ini berwarna pelangi.
        gatesText.gameObject.AddComponent<TMPRainbowLinks>();

        TextMeshProUGUI footer = UIKit.CreateText("Footer", rect, "<b>[Esc]</b> lanjutkan", 20f, UIKit.TextMuted, TextAlignmentOptions.Center);
        UIKit.Place(footer.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(600f, 30f));
    }

    protected override void OnShow()
    {
        RunManager run = ui.Run;

        if (run != null)
            info.text = "Ronde " + run.CurrentRound + " / " + run.TotalRounds + "   |   " + UIKit.FormatTime(run.RunDuration);
        else
            info.text = string.Empty;

        statsText.text = BuildStatsText(ui.Stats, builder);
        gatesText.text = BuildGatesText(ui.Stats, builder);
    }

    // Satu baris per stat, mis. "Defense    15   (+5)". Dipakai juga layar hasil.
    public static string BuildStatsText(PlayerStats stats, StringBuilder builder)
    {
        if (stats == null)
            return "-";

        builder.Clear();

        for (int i = 0; i < StatTypes.Count; i++)
        {
            StatType stat = (StatType)i;
            float now = stats.Get(stat);
            float baseValue = stats.GetBase(stat);

            builder.Append(UIKit.StatLabel(stat)).Append("<pos=55%><b>").Append(now.ToString("0.##")).Append("</b>");

            if (!Mathf.Approximately(now, baseValue))
            {
                float delta = now - baseValue;
                string color = delta > 0f ? UIKit.BuffHex : UIKit.TradeOffHex;
                builder.Append("<pos=75%><size=80%><color=").Append(color).Append('>')
                    .Append(delta > 0f ? "+" : string.Empty).Append(delta.ToString("0.##")).Append("</color></size>");
            }

            if (i < StatTypes.Count - 1)
                builder.Append('\n');
        }

        return builder.ToString();
    }

    public static string BuildGatesText(PlayerStats stats, StringBuilder builder)
    {
        if (stats == null || stats.SelectedGates.Count == 0)
            return "<color=" + UIKit.MutedHex + ">Belum ada gate.</color>";

        builder.Clear();

        for (int i = 0; i < stats.SelectedGates.Count; i++)
        {
            MathGateData gate = stats.SelectedGates[i];

            if (gate == null)
                continue;

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(GateRarityStyle.Wrap(gate.Rarity, (i + 1) + ". " + gate.DisplayName));
        }

        return builder.ToString();
    }
}
