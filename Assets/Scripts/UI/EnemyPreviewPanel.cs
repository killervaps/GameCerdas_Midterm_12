using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bagian "musuh berikutnya" di kartu HUD kiri atas saat di Safezone: komposisi musuh ronde berikutnya
// (role, jumlah, stat) dan meter ancaman. Kontennya dibangun di dalam HudPanel.PreviewHost supaya
// HUD dan preview tampil sebagai satu kartu. Saat MC berdiri di depan gate, meter ikut menampilkan ancaman setelah gate itu.
// Angka diambil dari ThreatEstimator, rumus yang sama dengan label di kartu gate dan keputusan AI.
public class EnemyPreviewPanel : UIScreen
{
    private TextMeshProUGUI title;
    private TextMeshProUGUI waveName;
    private RectTransform list;
    private ThreatMeter meter;
    private RectTransform card;
    private RectTransform host;

    private MathGateData compareGate;

    protected override void Build()
    {
        // Gabung ke kartu HUD jika ada. Tanpa HUD, buat kartu sendiri di kiri atas.
        if (ui.Hud != null && ui.Hud.PreviewHost != null)
        {
            host = ui.Hud.PreviewHost;
            card = ui.Hud.CardRect;
        }
        else
        {
            Image panel = UIKit.CreatePanel("Card", rect, UIKit.Panel);
            card = panel.rectTransform;
            UIKit.Place(card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(430f, 0f));
            UIKit.AddVertical(panel.gameObject, 10f, new RectOffset(22, 22, 18, 22));
            UIKit.FitHeight(panel.gameObject);
            host = card;
        }

        UIKit.CreateDivider(host);

        title = UIKit.CreateText("Title", host, "MUSUH BERIKUTNYA", 22f, UIKit.Accent, TextAlignmentOptions.Left, true);
        title.characterSpacing = 3f;

        waveName = UIKit.CreateText("WaveName", host, string.Empty, 19f, UIKit.TextMuted);

        list = UIKit.CreateRect("Enemies", host);
        UIKit.AddVertical(list.gameObject, 8f);

        UIKit.CreateDivider(host);

        meter = new ThreatMeter(host);

        if (host != card)
            host.gameObject.SetActive(false);
    }

    protected override void OnShow()
    {
        if (host != card)
            host.gameObject.SetActive(true);

        Refresh();
    }

    protected override void OnHide()
    {
        if (host != card)
            host.gameObject.SetActive(false);
    }

    // Gate yang sedang dilihat MC. Null = tidak ada perbandingan.
    public void SetCompareGate(MathGateData gate)
    {
        compareGate = gate;

        if (IsVisible)
            RefreshThreat();
    }

    public void Refresh()
    {
        if (title == null)
            return;

        RunManager run = ui.Run;
        WaveData wave = CurrentWave();

        title.text = run != null && run.CurrentRound > 0
            ? "MUSUH BERIKUTNYA" + (run.CurrentRound >= run.TotalRounds && run.TotalRounds > 0 ? "  <color=" + UIKit.TradeOffHex + ">FINAL</color>" : string.Empty)
            : "MUSUH BERIKUTNYA";

        waveName.text = wave != null
            ? wave.DisplayName + "  <color=" + UIKit.MutedHex + ">|  " + wave.TotalCount + " musuh</color>"
            : "Belum ada data wave.";

        RebuildList(wave);
        RefreshThreat();
        UIKit.ForceRebuild(card);
    }

    private WaveData CurrentWave()
    {
        if (ui.GateRoom != null && ui.GateRoom.UpcomingWave != null)
            return ui.GateRoom.UpcomingWave;

        return ui.Run != null ? ui.Run.CurrentWave : null;
    }

    private void RebuildList(WaveData wave)
    {
        UIKit.ClearChildren(list);

        if (wave == null)
            return;

        foreach (WaveEntry entry in wave.Entries)
        {
            if (entry.count <= 0)
                continue;

            EnemyRoleData data = null;

            if (ui.Threat != null)
                ui.Threat.TryGetRoleData(entry.role, out data);

            CreateRow(entry, data);
        }
    }

    private void CreateRow(WaveEntry entry, EnemyRoleData data)
    {
        Color roleColor = RoleColors.Get(entry.role);

        Image row = UIKit.CreateImage("Row_" + entry.role, list, UIKit.PanelLight);
        HorizontalLayoutGroup layout = UIKit.AddHorizontal(row.gameObject, 14f, new RectOffset(10, 16, 8, 8));
        layout.childAlignment = TextAnchor.MiddleLeft;
        UIKit.Layout(row, 64f);

        // Lencana warna role dengan huruf awal.
        Image badge = UIKit.CreateImage("Badge", row.transform, roleColor);
        UIKit.Layout(badge, 48f, 48f, 0f);

        string roleName = data != null ? data.DisplayName : entry.role.ToString();

        TextMeshProUGUI initial = UIKit.CreateText("Initial", badge.transform, roleName.Substring(0, 1).ToUpperInvariant(), 28f,
            new Color(0.06f, 0.07f, 0.1f), TextAlignmentOptions.Center, true);
        UIKit.Stretch(initial.rectTransform);

        string stats = data != null
            ? "HP " + data.MaxHealth.ToString("0") + "   DEF " + data.Defense.ToString("0.#") + "   DPS " + data.DPS.ToString("0.#")
            : "Role Data tidak ditemukan";

        TextMeshProUGUI info = UIKit.CreateText("Info", row.transform,
            "<b>" + roleName + "</b>\n<size=80%><color=" + UIKit.MutedHex + ">" + stats + "</color></size>",
            22f, UIKit.Text);
        info.lineSpacing = -6f;
        UIKit.Layout(info, -1f, -1f, 1f);

        TextMeshProUGUI count = UIKit.CreateText("Count", row.transform, "x" + entry.count, 32f, roleColor, TextAlignmentOptions.Right, true);
        UIKit.Layout(count, -1f, 60f, 0f);
    }

    private void RefreshThreat()
    {
        ThreatEstimator estimator = ui.Threat;
        PlayerStats stats = ui.Stats;
        WaveData wave = CurrentWave();

        if (estimator == null || stats == null || wave == null)
        {
            meter.SetEmpty(estimator == null ? "ThreatEstimator belum ada di scene." : "Tidak ada wave untuk dihitung.");
            return;
        }

        meter.Configure(estimator.SafeBelow, estimator.DangerAbove);

        ThreatResult now = estimator.Evaluate(wave, stats.GetSnapshot());
        ThreatResult? after = compareGate != null ? estimator.Evaluate(wave, stats.Preview(compareGate)) : (ThreatResult?)null;

        meter.Set(now, after);
    }

    protected override void Update()
    {
        base.Update();

        if (IsVisible)
            meter.Tick(Time.unscaledDeltaTime);
    }
}
