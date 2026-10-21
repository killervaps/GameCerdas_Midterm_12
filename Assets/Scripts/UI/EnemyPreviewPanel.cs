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

        title = UIKit.CreateText("Title", host, "Musuh berikutnya", 20f, UIKit.TextMuted, TextAlignmentOptions.Left, true);

        // Hanya tampil jika wave belum ada (pesan kosong). Nama wave tidak ditampilkan karena sama dengan ronde.
        waveName = UIKit.CreateText("WaveName", host, string.Empty, 19f, UIKit.TextMuted);

        list = UIKit.CreateRect("Enemies", host);
        UIKit.AddVertical(list.gameObject, 10f);

        // Garis tinta pemisah daftar musuh dan meter ancaman.
        Image line = UIKit.CreateImage("InkLine", host, UIKit.Ink, false);
        UIKit.Layout(line, 3f);

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
            ? "Musuh berikutnya" + (run.CurrentRound >= run.TotalRounds && run.TotalRounds > 0 ? "  <color=" + UIKit.TradeOffHex + ">FINAL</color>" : string.Empty)
            : "Musuh berikutnya";

        waveName.text = "Belum ada data wave untuk ronde ini.";
        waveName.gameObject.SetActive(wave == null);

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

    // Satu baris per role: token bulat warna role, nama + stat ringkas, dan jumlah besar yang miring.
    private void CreateRow(WaveEntry entry, EnemyRoleData data)
    {
        Color roleColor = RoleColors.Get(entry.role);

        RectTransform row = UIKit.CreateRect("Row_" + entry.role, list);
        HorizontalLayoutGroup layout = UIKit.AddHorizontal(row.gameObject, 12f);
        layout.childAlignment = TextAnchor.MiddleLeft;
        UIKit.Layout(row, 52f);

        UIKit.CreateToken("Token", row, 46f, roleColor, EnemyPreviewBoard.RoleBadgeLetter(entry.role), out _);

        string roleName = data != null ? data.DisplayName : entry.role.ToString();
        string stats = data != null
            ? "HP " + data.MaxHealth.ToString("0") + " · DEF " + data.Defense.ToString("0.#") + " · DPS " + data.DPS.ToString("0.#")
            : "Role Data tidak ditemukan";

        RectTransform info = UIKit.CreateRect("Info", row);
        UIKit.AddVertical(info.gameObject, 0f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(info, -1f, -1f, 1f);

        TextMeshProUGUI name = UIKit.UseDisplayFont(UIKit.CreateText("Name", info, roleName, 22f, UIKit.Text, TextAlignmentOptions.Left, true));
        name.textWrappingMode = TextWrappingModes.NoWrap;
        TextMeshProUGUI statLine = UIKit.CreateText("Stats", info, stats, 16f, UIKit.TextMuted);
        statLine.textWrappingMode = TextWrappingModes.NoWrap;

        TextMeshProUGUI count = UIKit.UseDisplayFont(UIKit.CreateText("Count", row, "×" + entry.count, 36f, roleColor, TextAlignmentOptions.Right, true));
        count.textWrappingMode = TextWrappingModes.NoWrap;
        // Tanpa outline: baris ini sering dibangun saat HUD masih nonaktif (mis. saat StartRun), dan TMP
        // belum punya material di objek nonaktif, sehingga outlineWidth melempar ArgumentNullException.
        count.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -5f);
        UIKit.Layout(count, -1f, 64f, 0f);
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
