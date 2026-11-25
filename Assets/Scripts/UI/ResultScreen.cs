using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Layar akhir run (menang, kalah, atau ekstraksi) dengan ringkasan waktu, ronde atau wave, gate, skor Aura,
// rekor lokal, dan stat akhir. Menggantikan layar hasil OnGUI di RunStatusPanel.
public class ResultScreen : UIScreen
{
    private TextMeshProUGUI title;
    private TextMeshProUGUI subtitle;
    private TextMeshProUGUI timeValue;
    private TextMeshProUGUI roundValue;
    private TextMeshProUGUI roundCaption;
    private TextMeshProUGUI gateValue;
    private TextMeshProUGUI scoreValue;
    private TextMeshProUGUI lostValue;
    private TextMeshProUGUI recordValue;
    private TextMeshProUGUI recordCaption;
    private Image recordBadge;
    private TextMeshProUGUI recordBadgeLabel;
    private GameObject scoresRow;
    private GameObject recordBadgeRow;
    private TextMeshProUGUI statsText;
    private TextMeshProUGUI gatesText;
    private Image glow;

    private readonly StringBuilder builder = new StringBuilder();

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        glow = UIKit.CreateImage("Glow", rect, Color.white, false);
        glow.sprite = UIKit.VerticalFade;
        UIKit.Stretch(glow.rectTransform);
        glow.rectTransform.localScale = new Vector3(1f, -1f, 1f);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 0f));
        UIKit.AddVertical(panel.gameObject, 14f, new RectOffset(48, 48, 40, 40));
        UIKit.FitHeight(panel.gameObject);

        // Judul MENANG/KALAH adalah momen kunci: tetap kapital dengan spacing, dan emas saat menang.
        title = UIKit.UseDisplayFont(UIKit.CreateText("Title", panel.transform, string.Empty, 84f, UIKit.Text, TextAlignmentOptions.Center, true));
        title.characterSpacing = 8f;

        subtitle = UIKit.CreateText("Subtitle", panel.transform, string.Empty, 26f, UIKit.TextMuted, TextAlignmentOptions.Center);

        // Tiga angka ringkas.
        RectTransform stats = UIKit.CreateRect("Summary", panel.transform);
        HorizontalLayoutGroup statsLayout = UIKit.AddHorizontal(stats.gameObject, 14f);
        statsLayout.childForceExpandWidth = true;
        UIKit.Layout(stats, 96f);

        timeValue = SummaryTile(stats, "Waktu", out _);
        roundValue = SummaryTile(stats, "Ronde", out roundCaption);
        gateValue = SummaryTile(stats, "Gate", out _);

        // Skor Aura: aman, hilang (MC mati), dan rekor mode ini.
        RectTransform scores = UIKit.CreateRect("Scores", panel.transform);
        HorizontalLayoutGroup scoresLayout = UIKit.AddHorizontal(scores.gameObject, 14f);
        scoresLayout.childForceExpandWidth = true;
        UIKit.Layout(scores, 96f);

        scoreValue = SummaryTile(scores, "Skor aman", out _);
        lostValue = SummaryTile(scores, "Skor hilang", out _);
        recordValue = SummaryTile(scores, "Rekor", out recordCaption);

        scoresRow = scores.gameObject;

        // Stiker "Rekor baru!" di tengah, hanya tampil jika rekor terpecahkan.
        RectTransform badgeRow = UIKit.CreateRect("RecordBadgeRow", panel.transform);
        HorizontalLayoutGroup badgeLayout = UIKit.AddHorizontal(badgeRow.gameObject, 0f, null, TextAnchor.MiddleCenter);
        badgeLayout.childForceExpandWidth = false;
        recordBadge = UIKit.CreateStamp("RecordBadge", badgeRow, 24f, -3f, out recordBadgeLabel);
        UIKit.SetStamp(recordBadge, recordBadgeLabel, "Rekor baru!", UIKit.Accent);
        recordBadgeRow = badgeRow.gameObject;

        // Detail build.
        RectTransform detail = UIKit.CreateRect("Detail", panel.transform);
        HorizontalLayoutGroup detailLayout = UIKit.AddHorizontal(detail.gameObject, 24f, null, TextAnchor.UpperLeft);
        detailLayout.childForceExpandWidth = true;

        RectTransform statsColumn = UIKit.CreateRect("Stats", detail);
        UIKit.AddVertical(statsColumn.gameObject, 6f);
        UIKit.Layout(statsColumn, -1f, -1f, 1f);
        Heading(statsColumn, "Stat akhir");
        statsText = UIKit.CreateText("Text", statsColumn, string.Empty, 22f, UIKit.Text);

        RectTransform gatesColumn = UIKit.CreateRect("Gates", detail);
        UIKit.AddVertical(gatesColumn.gameObject, 6f);
        UIKit.Layout(gatesColumn, -1f, -1f, 1f);
        Heading(gatesColumn, "Gate dipilih");
        gatesText = UIKit.CreateText("Text", gatesColumn, string.Empty, 22f, UIKit.Text);

        // Tombol.
        RectTransform buttons = UIKit.CreateRect("Buttons", panel.transform);
        HorizontalLayoutGroup buttonLayout = UIKit.AddHorizontal(buttons.gameObject, 16f, new RectOffset(0, 0, 12, 0), TextAnchor.MiddleCenter);
        buttonLayout.childForceExpandWidth = true;
        UIKit.Layout(buttons, 80f);

        Button again = UIKit.CreateButton("PlayAgainButton", buttons, "MAIN LAGI", UIKit.Safe(ui.StartGame), 68f, true);
        UIKit.Layout(again, 68f, 260f, 1f);

        Button menu = UIKit.CreateButton("MenuButton", buttons, "MENU UTAMA", UIKit.Safe(ui.BackToMainMenu));
        UIKit.Layout(menu, 68f, 260f, 1f);

        firstSelected = again;
    }

    private static void Heading(Transform parent, string text)
    {
        UIKit.CreateText("Heading", parent, text, 20f, UIKit.TextMuted, TextAlignmentOptions.Left, true);
    }

    private static TextMeshProUGUI SummaryTile(Transform parent, string caption, out TextMeshProUGUI label)
    {
        Image tile = UIKit.SetCorners(UIKit.CreateImage("Tile_" + caption, parent, UIKit.Well), UIKit.InkMedium);
        UIKit.Layout(tile, 96f, -1f, 1f);

        TextMeshProUGUI value = UIKit.UseDisplayFont(UIKit.CreateText("Value", tile.transform, "-", 38f, UIKit.Text, TextAlignmentOptions.Center, true));
        UIKit.Stretch(value.rectTransform, 8f, 8f, 10f, 34f);

        label = UIKit.CreateText("Caption", tile.transform, caption, 18f, UIKit.TextMuted, TextAlignmentOptions.Center, true);
        UIKit.Stretch(label.rectTransform, 8f, 8f, 60f, 10f);

        return value;
    }

    public void ShowResult(RunState ending)
    {
        RunManager run = ui.Run;
        PlayerStats stats = ui.Stats;
        AuraScore aura = AuraScore.Current;

        bool victory = ending == RunState.Victory;
        bool extracted = ending == RunState.Extracted;
        Color tone = victory || extracted ? UIKit.Accent : UIKit.TradeOff;
        title.text = victory ? "MENANG!" : extracted ? "EKSTRAKSI!" : "KALAH";
        title.color = tone;
        glow.color = new Color(tone.r, tone.g, tone.b, 0.18f);

        if (run != null)
        {
            int cleared = aura != null ? aura.WavesCleared : victory ? run.TotalRounds : Mathf.Max(0, run.CurrentRound - 1);

            if (run.IsEndless)
            {
                subtitle.text = extracted
                    ? "Keluar dengan selamat setelah wave " + cleared + "."
                    : "MC tumbang di wave " + run.CurrentRound + ".";
                roundCaption.text = "Wave dibersihkan";
                roundValue.text = cleared.ToString();
            }
            else
            {
                subtitle.text = victory
                    ? "Semua " + run.TotalRounds + " ronde berhasil dibersihkan."
                    : "MC tumbang di ronde " + run.CurrentRound + " dari " + run.TotalRounds + ".";
                roundCaption.text = "Ronde";
                roundValue.text = cleared + " / " + run.TotalRounds;
            }

            timeValue.text = UIKit.FormatTime(run.RunDuration);
        }

        bool hasScore = aura != null;
        scoresRow.SetActive(hasScore);
        recordBadgeRow.SetActive(hasScore && (aura.IsNewRecord || aura.IsNewWaveRecord));

        if (hasScore)
        {
            scoreValue.text = aura.Final.ToString("N0");
            lostValue.text = aura.Lost > 0 ? "-" + aura.Lost.ToString("N0") : "0";
            lostValue.color = aura.Lost > 0 ? UIKit.TradeOff : UIKit.Text;
            recordValue.text = aura.BestScore.ToString("N0");
            recordCaption.text = "Rekor " + aura.RecordLabel() + ", wave " + aura.BestWave;
            recordBadgeLabel.text = aura.IsNewRecord && aura.IsNewWaveRecord ? "Rekor baru! Skor dan wave"
                : aura.IsNewRecord ? "Rekor baru!" : "Rekor wave baru!";
        }

        gateValue.text = stats != null ? stats.SelectedGates.Count.ToString() : "-";
        statsText.text = PauseScreen.BuildStatsText(stats, builder);
        gatesText.text = PauseScreen.BuildGatesText(stats, builder);

        Show();
        UIKit.ForceRebuild((RectTransform)title.transform.parent);
    }
}
