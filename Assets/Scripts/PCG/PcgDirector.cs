using System;
using UnityEngine;

// Pusat PCG di scene: seed run, aturan spawn bawaan, dan preview generate-and-test di Editor.
//   Seed      acak setiap run baru (Use Random Seed) atau tetap (Fixed Seed) untuk demo dan eksperimen.
//             Setiap ronde dan setiap aliran (komposisi musuh, posisi musuh, item) memakai seed turunan dari seed run,
//             jadi seed run yang sama selalu menghasilkan seluruh run yang sama.
//   Preview   menu titik tiga komponen > Preview Placement menjalankan algoritma di Edit Mode tanpa Play,
//             lalu menggambar hasilnya di Scene view: bola hijau = diterima, silang berwarna = ditolak (warna = alasan).
[DisallowMultipleComponent]
public class PcgDirector : MonoBehaviour
{
    [Header("Seed")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int fixedSeed = 12345;

    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;

    [Header("Default Rules")]
    // Dipakai spawner musuh dan item jika profil ronde tidak menimpanya.
    [SerializeField] private PcgSpawnRules defaultRules = new PcgSpawnRules();

    [Header("Editor Preview")]
    [SerializeField] private PcgSpawnArea previewArea;
    [SerializeField] private int previewSeed = 12345;
    [SerializeField, Min(1)] private int previewCount = 12;
    [SerializeField] private bool drawPreview = true;

    [Header("Runtime (hanya dibaca)")]
    [SerializeField] private int currentRunSeed;

    [Header("Debug")]
    [SerializeField] private bool logSeeds = true;

    private readonly PcgPlacementReport previewReport = new PcgPlacementReport();
    private readonly System.Collections.Generic.List<Vector3> previewOccupied = new System.Collections.Generic.List<Vector3>();
    private bool hasPreview;
    private bool hasRunSeed;

    public static PcgDirector Current { get; private set; }

    public int RunSeed => currentRunSeed;

    public bool HasRunSeed => hasRunSeed;

    public bool UsesRandomSeed => useRandomSeed;

    public PcgSpawnRules DefaultRules => defaultRules;

    // Dikirim saat seed run baru dibuat.
    public event Action<int> SeedChanged;

    private void Awake()
    {
        Current = this;

        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();
    }

    private void OnEnable()
    {
        if (runManager != null)
            runManager.RunStarted += BeginRun;
    }

    private void OnDisable()
    {
        if (runManager != null)
            runManager.RunStarted -= BeginRun;
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }

    // Membuat seed untuk run baru. Dipanggil otomatis saat RunManager memulai run.
    public void BeginRun()
    {
        SetRunSeed(useRandomSeed ? NewRandomSeed() : fixedSeed);
    }

    // Memakai seed tertentu, mis. dari panel debug untuk mengulang run yang sama.
    public void SetRunSeed(int seed)
    {
        currentRunSeed = seed;
        hasRunSeed = true;

        if (logSeeds)
            Debug.Log("PcgDirector: seed run " + seed + (useRandomSeed ? " (acak)" : " (tetap)"), this);

        SeedChanged?.Invoke(seed);
    }

    // Generator untuk satu ronde dan satu aliran. Selalu mulai dari awal urutan, jadi bisa dipanggil ulang untuk regenerate.
    public SeededRandom CreateStream(int round, PcgStream stream)
    {
        if (!hasRunSeed)
            BeginRun();

        return new SeededRandom(SeededRandom.Derive(currentRunSeed, round, stream));
    }

    private static int NewRandomSeed()
    {
        // Environment.TickCount seperti di modul, diaduk supaya run yang dimulai berdekatan tetap berbeda jauh.
        return SeededRandom.Derive(Environment.TickCount, 0, PcgStream.Preview);
    }

    // ---------- Preview di Editor ----------

    [ContextMenu("Preview Placement")]
    private void PreviewPlacement()
    {
        if (previewArea == null)
        {
            Debug.LogWarning("PcgDirector: isi Preview Area dengan PcgSpawnArea salah satu arena.", this);
            return;
        }

        previewReport.Clear();
        previewOccupied.Clear();
        previewReport.Requested = previewCount;

        SeededRandom random = new SeededRandom(SeededRandom.Derive(previewSeed, 1, PcgStream.Preview));
        Vector3? player = previewArea.PlayerEntryPoint;

        for (int i = 0; i < previewCount; i++)
            PcgPlacement.TryPlace(previewArea, defaultRules, random, player, previewOccupied, previewReport, out _);

        hasPreview = true;

        Debug.Log("PcgDirector preview seed " + previewSeed + " di " + previewArea.name + ": " + previewReport.Summary(), this);
    }

    [ContextMenu("Preview Next Seed")]
    private void PreviewNextSeed()
    {
        previewSeed++;
        PreviewPlacement();
    }

    [ContextMenu("Clear Preview")]
    private void ClearPreview()
    {
        hasPreview = false;
        previewReport.Clear();
    }

    private void OnDrawGizmos()
    {
        if (!drawPreview || !hasPreview || previewArea == null)
            return;

        // Rule 1: lingkaran merah radius aman di sekitar titik masuk MC.
        Vector3? player = previewArea.PlayerEntryPoint;

        if (player.HasValue)
        {
            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.9f);
            DrawCircle(player.Value + Vector3.up * 0.05f, defaultRules.minDistanceFromPlayer);
        }

        // Diterima: bola hijau seukuran check radius, plus lingkaran minimum spacing.
        foreach (Vector3 point in previewReport.placed)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Gizmos.DrawSphere(point + Vector3.up * defaultRules.checkRadius, defaultRules.checkRadius);
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.35f);
            DrawCircle(point + Vector3.up * 0.05f, defaultRules.minimumSpacing * 0.5f);
        }

        // Ditolak: silang kecil, warnanya menunjukkan alasan.
        foreach (PcgRejectedCandidate candidate in previewReport.rejected)
        {
            Gizmos.color = RejectColor(candidate.reason);
            DrawCross(candidate.position + Vector3.up * 0.05f, 0.15f);
        }
    }

    public static Color RejectColor(PcgReject reason)
    {
        switch (reason)
        {
            case PcgReject.TooCloseToPlayer: return new Color(1f, 0.25f, 0.25f);
            case PcgReject.Obstacle: return new Color(1f, 0.6f, 0.1f);
            case PcgReject.TooCloseToOther: return new Color(1f, 0.95f, 0.2f);
            case PcgReject.Elevated: return new Color(0.75f, 0.45f, 1f);
            case PcgReject.Unreachable: return new Color(0.3f, 0.6f, 1f);
            default: return new Color(0.6f, 0.6f, 0.6f);
        }
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 40;
        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }

    private static void DrawCross(Vector3 center, float size)
    {
        Gizmos.DrawLine(center + new Vector3(-size, 0f, -size), center + new Vector3(size, 0f, size));
        Gizmos.DrawLine(center + new Vector3(-size, 0f, size), center + new Vector3(size, 0f, -size));
    }
}
