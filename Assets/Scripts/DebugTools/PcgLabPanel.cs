using System.Collections.Generic;
using UnityEngine;

// Panel debug tambahan PCG di pojok kanan bawah, ikut AI Debug Mode (F1).
//   Seed History     (Challenge 7) seed run terakhir + ringkasan ronde pertamanya. Fav = favorit (tidak terhapus),
//                    Main = mulai run baru dengan seed itu. Alur designer: Generate -> Evaluate -> Select.
//   Spawn Statistik  (Challenge 6) simulasi 100 / 500 / 1000 musuh dari profil ronde sekarang (termasuk bias arena),
//                    lalu membandingkan bobot (Harapan) dengan hasil hitungan (Murni dan Generator).
//                    Tabel lengkap juga ditulis ke Console supaya bisa disalin ke laporan.
public class PcgLabPanel : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private EnemySpawner enemySpawner;
    [SerializeField] private PcgSeedHistory history;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    // Aktif = panel hanya tampil saat AI Debug Mode menyala (F1).
    [SerializeField] private bool followDebugToggle = true;
    // Jarak dari pojok kanan bawah layar.
    [SerializeField] private Vector2 margin = new Vector2(10f, 10f);
    [SerializeField] private float width = 440f;
    [SerializeField] private float height = 430f;
    // Tiap seed memakai dua baris (seed lalu catatan ronde).
    [SerializeField] private float historyListHeight = 120f;
    [SerializeField] private float uiScale = 1f;

    [Header("Statistik")]
    [SerializeField] private int[] sampleSizes = { 100, 500, 1000 };
    [SerializeField] private bool logStatistics = true;

    private PcgStatistics.Result lastStats;
    private string statusMessage = string.Empty;
    private Vector2 historyScroll;
    private GUIStyle label;
    private GUIStyle smallLabel;

    // Aksi dari tombol dijalankan setelah GUI selesai digambar, supaya daftar tidak berubah di tengah layout.
    private int pendingFavorite;
    private bool hasPendingFavorite;
    private int pendingReplay;
    private bool hasPendingReplay;
    private int pendingSample;
    private bool clearRequested;

    private bool Visible => showPanel && (!followDebugToggle || AIDebugSettings.Enabled);

    private void Awake()
    {
        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();

        if (enemySpawner == null)
            enemySpawner = FindAnyObjectByType<EnemySpawner>();

        if (history == null)
            history = FindAnyObjectByType<PcgSeedHistory>();
    }

    private void OnEnable()
    {
        if (history != null)
            history.Changed += ScrollToNewest;
    }

    private void OnDisable()
    {
        if (history != null)
            history.Changed -= ScrollToNewest;
    }

    // Seed terbaru selalu di atas, jadi daftar digulung ke atas setiap riwayat berubah.
    private void ScrollToNewest()
    {
        historyScroll = Vector2.zero;
    }

    private void OnGUI()
    {
        if (!Visible)
            return;

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false };
            smallLabel = new GUIStyle(label) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip };
        }

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float screenWidth = Screen.width / uiScale;
        float screenHeight = Screen.height / uiScale;
        Rect area = new Rect(screenWidth - margin.x - width, screenHeight - margin.y - height, width, height);

        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f));

        DrawHistory();
        GUILayout.Space(10f);
        DrawStatistics();

        GUILayout.EndArea();

        ApplyPendingActions();
    }

    // ---------- Seed History ----------

    private void DrawHistory()
    {
        GUILayout.Label("<b>SEED HISTORY</b>  <color=#aaaaaa>Fav = simpan, Main = ulangi run</color>", label);

        if (history == null)
        {
            GUILayout.Label("<color=#ff8866>Tambahkan PcgSeedHistory di objek PcgDirector.</color>", label);
            return;
        }

        PcgDirector director = PcgDirector.Current;
        bool hasCurrent = director != null && director.HasRunSeed;
        int current = hasCurrent ? director.RunSeed : 0;

        historyScroll = GUILayout.BeginScrollView(historyScroll, GUILayout.Height(historyListHeight));

        if (history.Records.Count == 0)
            GUILayout.Label("<color=#aaaaaa>Belum ada seed. Mulai run untuk mengisi riwayat.</color>", label);

        foreach (PcgSeedHistory.Record record in history.Records)
        {
            GUILayout.BeginHorizontal();

            bool favorite = GUILayout.Toggle(record.favorite, "Fav", GUILayout.Width(44f));

            if (favorite != record.favorite)
            {
                pendingFavorite = record.seed;
                hasPendingFavorite = true;
            }

            string seedText = hasCurrent && record.seed == current
                ? "<color=#ffee55><b>" + record.seed + "</b></color>"
                : record.seed.ToString();

            GUILayout.Label(seedText, label, GUILayout.Width(110f));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Main", GUILayout.Width(48f)))
            {
                pendingReplay = record.seed;
                hasPendingReplay = true;
            }

            GUILayout.EndHorizontal();

            string note = record.Note;
            GUILayout.Label("   " + (string.IsNullOrEmpty(note) ? "<color=#888888>belum ada ronde</color>" : note), smallLabel);
        }

        GUILayout.EndScrollView();

        if (GUILayout.Button("Hapus riwayat (favorit tetap)"))
            clearRequested = true;
    }

    // ---------- Spawn Statistik ----------

    private void DrawStatistics()
    {
        GUILayout.Label("<b>SPAWN STATISTIK</b>  <color=#aaaaaa>" + StatisticsContext() + "</color>", label);

        if (runManager != null && runManager.CurrentProfile != null)
        {
            GUILayout.Label(runManager.CurrentBudget > 0
                ? "Budget ronde ini: <color=#ffee55><b>" + runManager.CurrentBudgetSpent + " / " + runManager.CurrentBudget + "</b></color>  " + CostText()
                : "<color=#aaaaaa>Budget mati, biaya ronde ini " + runManager.CurrentBudgetSpent + "  " + CostText() + "</color>", label);
        }

        GUILayout.BeginHorizontal();

        foreach (int size in sampleSizes)
        {
            if (GUILayout.Button("Simulasi " + size))
                pendingSample = size;
        }

        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(statusMessage))
            GUILayout.Label("<color=#ff8866>" + statusMessage + "</color>", label);

        if (lastStats == null)
        {
            GUILayout.Label("<color=#aaaaaa>Pilih jumlah sampel untuk membandingkan bobot dengan hasil.</color>", label);
            return;
        }

        Row("<b>Role</b>", "<b>Bobot</b>", "<b>Harapan</b>", "<b>Murni</b>", "<b>Generator</b>");

        foreach (PcgStatistics.RoleRow row in lastStats.rows)
        {
            Row(
                row.role.ToString(),
                row.weight.ToString(),
                PcgStatistics.Percent(row.expected),
                PcgStatistics.Percent(lastStats.PurePercent(row)) + " <color=#aaaaaa>(" + row.pure + ")</color>",
                PcgStatistics.Percent(lastStats.GeneratorPercent(row)) + " <color=#aaaaaa>(" + row.generator + ")</color>");
        }

        GUILayout.Label(
            "Biaya per komposisi: rata-rata <b>" + lastStats.AverageCost.ToString("0.0") + "</b>, min " +
            (lastStats.compositions > 0 ? lastStats.minCost : 0) + ", max " + lastStats.maxCost +
            (lastStats.budget > 0 ? "  (budget " + lastStats.budget + ")" : "  (budget mati)"),
            smallLabel);

        GUILayout.Label(
            "<color=#aaaaaa>" + lastStats.label + "  |  murni " + lastStats.pureTotal + ", generator " + lastStats.generatorTotal +
            " dari " + lastStats.compositions + " komposisi  |  seed " + lastStats.seed + "</color>",
            smallLabel);
    }

    private void Row(string role, string weight, string expected, string pure, string generator)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(role, label, GUILayout.Width(78f));
        GUILayout.Label(weight, label, GUILayout.Width(50f));
        GUILayout.Label(expected, label, GUILayout.Width(70f));
        GUILayout.Label(pure, label, GUILayout.Width(110f));
        GUILayout.Label(generator, label, GUILayout.Width(110f));
        GUILayout.EndHorizontal();
    }

    // Mis. "C2 S2 G3".
    private string CostText()
    {
        if (runManager == null || runManager.RoleCosts == null)
            return string.Empty;

        System.Text.StringBuilder text = new System.Text.StringBuilder("<color=#aaaaaa>biaya");

        foreach (RoleCost cost in runManager.RoleCosts)
        {
            if (cost != null)
                text.Append(' ').Append(cost.role.ToString().Substring(0, 1)).Append(cost.cost);
        }

        return text.Append("</color>").ToString();
    }

    private string StatisticsContext()
    {
        EnemyRoundProfile profile = runManager != null ? runManager.CurrentProfile : null;

        if (profile == null)
            return "mulai run dulu";

        Arena arena = runManager.CurrentArena;
        return profile.label + (arena != null ? " - " + arena.DisplayName : string.Empty);
    }

    private void RunStatistics(int sampleSize)
    {
        statusMessage = string.Empty;

        EnemyRoundProfile profile = runManager != null ? runManager.CurrentProfile : null;

        if (profile == null)
        {
            statusMessage = "Belum ada profil ronde. Mulai run dulu (Safezone atau saat bertarung).";
            return;
        }

        PcgSpawnArea area = enemySpawner != null ? enemySpawner.CurrentArea : null;
        IReadOnlyList<ArenaRoleBias> bias = area != null ? area.RoleBias : null;

        // Seed simulasi diturunkan dari seed run, ronde, dan jumlah sampel: tombol yang sama di run yang sama = hasil sama.
        PcgDirector director = PcgDirector.Current;
        int baseSeed = director != null && director.HasRunSeed ? director.RunSeed : 0;
        int seed = SeededRandom.Derive(baseSeed, runManager.CurrentRound * 10000 + sampleSize, PcgStream.Preview);

        lastStats = PcgStatistics.Simulate(profile, bias, sampleSize, seed, StatisticsContext(), runManager.RoleCosts);

        if (logStatistics)
            Debug.Log(lastStats.ToTable(), this);
    }

    private void ApplyPendingActions()
    {
        if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint)
            return;

        if (hasPendingFavorite && history != null)
            history.ToggleFavorite(pendingFavorite);

        if (clearRequested && history != null)
            history.ClearNonFavorites();

        if (pendingSample > 0)
            RunStatistics(pendingSample);

        if (hasPendingReplay)
            Replay(pendingReplay);

        hasPendingFavorite = false;
        hasPendingReplay = false;
        clearRequested = false;
        pendingSample = 0;
    }

    private void Replay(int seed)
    {
        if (runManager == null || PcgDirector.Current == null)
        {
            statusMessage = "RunManager atau PcgDirector tidak ditemukan.";
            return;
        }

        PcgDirector.Current.QueueSeedForNextRun(seed);
        runManager.StartRun();
    }
}
