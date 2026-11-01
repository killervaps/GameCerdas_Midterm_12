using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Debug PCG untuk Game view, ikut AI Debug Mode (F1).
//   Panel     seed run, arena dan biasnya, komposisi musuh, laporan generate-and-test musuh dan item
//             (berhasil, gagal, percobaan, ditolak per aturan).
//   Overlay   digambar di dunia dengan GL: kotak area spawn (cyan), lingkaran aman MC (merah),
//             musuh di lantai (hijau), musuh di titik khusus (ungu), item (biru muda),
//             kandidat ditolak (silang, warna = aturan yang menolak).
//   Kontrol   F5 ulangi ronde dengan seed yang sama (hasil harus identik), F6 seed baru lalu ulangi ronde,
//             kolom seed + tombol untuk memulai run baru dengan seed tertentu.
public class PcgDebugPanel : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private EnemySpawner enemySpawner;
    [SerializeField] private ItemSpawner itemSpawner;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    // Aktif = panel dan overlay hanya tampil saat AI Debug Mode menyala (F1).
    [SerializeField] private bool followDebugToggle = true;
    // Jarak dari pojok kiri bawah layar.
    [SerializeField] private Vector2 margin = new Vector2(10f, 10f);
    [SerializeField] private float width = 340f;
    [SerializeField] private float uiScale = 1f;
    [SerializeField] private float refreshInterval = 0.2f;

    [Header("Overlay")]
    [SerializeField] private bool drawOverlay = true;
    [SerializeField] private bool drawRejected = true;
    [SerializeField] private bool drawThroughWalls = true;

    [Header("Hotkeys")]
    [SerializeField] private bool hotkeys = true;

    private static readonly Color AreaColor = new Color(0.2f, 0.9f, 1f, 0.8f);
    private static readonly Color SafeColor = new Color(1f, 0.25f, 0.25f, 0.9f);
    private static readonly Color EnemyColor = new Color(0.3f, 1f, 0.4f, 1f);
    private static readonly Color AnchorColor = new Color(0.75f, 0.45f, 1f, 1f);
    private static readonly Color ItemColor = new Color(0.4f, 0.85f, 1f, 1f);

    private readonly StringBuilder builder = new StringBuilder();

    private string text = string.Empty;
    private int lineCount = 1;
    private float nextRefresh;
    private GUIStyle style;
    private string seedInput = string.Empty;
    private Material material;

    private bool Visible => showPanel && (!followDebugToggle || AIDebugSettings.Enabled);

    private void Awake()
    {
        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();

        if (enemySpawner == null)
            enemySpawner = FindAnyObjectByType<EnemySpawner>();

        if (itemSpawner == null)
            itemSpawner = FindAnyObjectByType<ItemSpawner>();

        Shader shader = Shader.Find("Hidden/Internal-Colored");

        if (shader != null)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)(drawThroughWalls ? CompareFunction.Always : CompareFunction.LessEqual));
        }
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
    }

    private void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }

    private void Update()
    {
        if (!Visible)
            return;

        if (hotkeys && Keyboard.current != null)
        {
            if (Keyboard.current.f5Key.wasPressedThisFrame)
                Regenerate(false);
            else if (Keyboard.current.f6Key.wasPressedThisFrame)
                Regenerate(true);
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            BuildText();
        }
    }

    // ---------- Kontrol ----------

    private void Regenerate(bool newSeed)
    {
        if (runManager == null || !runManager.CanRegenerate)
        {
            Debug.Log("PCG debug: regenerate hanya bisa di Safezone atau saat bertarung.", this);
            return;
        }

        if (newSeed && PcgDirector.Current != null)
            PcgDirector.Current.RerollSeed();

        runManager.RegenerateRound();
        nextRefresh = 0f;
    }

    private void RestartWithSeed()
    {
        if (runManager == null || PcgDirector.Current == null || !int.TryParse(seedInput.Trim(), out int seed))
        {
            Debug.LogWarning("PCG debug: isi kolom seed dengan angka, dan pastikan PcgDirector ada di scene.", this);
            return;
        }

        PcgDirector.Current.QueueSeedForNextRun(seed);
        runManager.StartRun();
        nextRefresh = 0f;
    }

    // ---------- Panel ----------

    private void BuildText()
    {
        builder.Clear();
        lineCount = 1;

        PcgDirector director = PcgDirector.Current;

        builder.Append("<b>PCG</b>  ");

        if (director != null && director.HasRunSeed)
            builder.Append("seed <color=#ffee55><b>").Append(director.RunSeed).Append("</b></color>");
        else
            builder.Append("<color=#ff8866>PcgDirector belum ada</color>");

        if (runManager != null && runManager.CurrentRound > 0)
            builder.Append("  <color=#aaaaaa>ronde ").Append(runManager.CurrentRound).Append('/').Append(runManager.TotalRounds).Append("</color>");

        AppendArena();

        WaveData wave = runManager != null ? runManager.CurrentWave : null;
        Line("Komposisi: ").Append(wave != null ? wave.Describe() : "-");

        AppendEnemies();
        AppendItems();

        Line("<color=#aaaaaa>Ditolak:</color> ")
            .Append(Swatch(PcgReject.TooCloseToPlayer, "pemain")).Append(' ')
            .Append(Swatch(PcgReject.Obstacle, "obstacle")).Append(' ')
            .Append(Swatch(PcgReject.TooCloseToOther, "jarak")).Append(' ')
            .Append(Swatch(PcgReject.Elevated, "menara")).Append(' ')
            .Append(Swatch(PcgReject.Unreachable, "jalur"));

        Line("<color=#aaaaaa>[F5] ulangi seed  [F6] seed baru</color>");

        text = builder.ToString();
    }

    private void AppendArena()
    {
        PcgSpawnArea area = enemySpawner != null ? enemySpawner.CurrentArea : null;
        Arena arena = runManager != null ? runManager.CurrentArena : null;

        Line("Arena: ").Append(arena != null ? arena.DisplayName : area != null ? area.name : "-");

        if (area == null || area.RoleBias == null || area.RoleBias.Count == 0)
            return;

        builder.Append("  <color=#aaaaaa>bias</color>");

        foreach (ArenaRoleBias bias in area.RoleBias)
        {
            if (bias == null)
                continue;

            builder.Append(' ').Append(bias.role).Append(" x").Append(bias.weightMultiplier.ToString("0.##"));

            if (bias.maxBonus > 0)
                builder.Append(" +").Append(bias.maxBonus);
        }
    }

    private void AppendEnemies()
    {
        if (enemySpawner == null)
            return;

        PcgPlacementReport report = enemySpawner.LastReport;

        Line("Musuh: ").Append(enemySpawner.Spawned.Count).Append(" muncul, ")
            .Append(enemySpawner.AnchorPositions.Count).Append(" di titik khusus");

        AppendReport(report);
    }

    private void AppendItems()
    {
        if (itemSpawner == null)
        {
            Line("Item: <color=#aaaaaa>ItemSpawner belum ada</color>");
            return;
        }

        Line("Item: ").Append(itemSpawner.Spawned.Count).Append(" tersisa di arena");
        AppendReport(itemSpawner.LastReport);
    }

    private void AppendReport(PcgPlacementReport report)
    {
        Line("  lantai ").Append(report.Placed).Append('/').Append(report.Requested)
            .Append(", gagal ").Append(report.Failed)
            .Append(", ").Append(report.Attempts).Append(" percobaan");

        Line("  ditolak ").Append(report.RejectedTotal).Append(": ")
            .Append(Count(PcgReject.TooCloseToPlayer, report.RejectedPlayer)).Append(' ')
            .Append(Count(PcgReject.Obstacle, report.RejectedObstacle)).Append(' ')
            .Append(Count(PcgReject.TooCloseToOther, report.RejectedSpacing)).Append(' ')
            .Append(Count(PcgReject.Elevated, report.RejectedElevated)).Append(' ')
            .Append(Count(PcgReject.Unreachable, report.RejectedUnreachable));

        if (report.RejectedNoGround > 0)
            builder.Append(" <color=#999999>").Append(report.RejectedNoGround).Append("</color>");
    }

    private StringBuilder Line(string value)
    {
        lineCount++;
        return builder.Append('\n').Append(value);
    }

    private static string Count(PcgReject reason, int value)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(PcgDirector.RejectColor(reason)) + ">" + value + "</color>";
    }

    private static string Swatch(PcgReject reason, string label)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(PcgDirector.RejectColor(reason)) + ">x " + label + "</color>";
    }

    private void OnGUI()
    {
        if (!Visible || string.IsNullOrEmpty(text))
            return;

        if (style == null)
            style = new GUIStyle(GUI.skin.box) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = true };

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float textHeight = 10f + lineCount * 18f;
        float controlsHeight = 56f;
        float screenHeight = Screen.height / uiScale;
        float x = margin.x;
        float y = screenHeight - margin.y - textHeight - controlsHeight;

        GUI.Box(new Rect(x, y, width, textHeight), text, style);

        float row = y + textHeight + 4f;
        float half = (width - 4f) * 0.5f;

        GUI.enabled = runManager != null && runManager.CanRegenerate;

        if (GUI.Button(new Rect(x, row, half, 24f), "Ulangi seed (F5)"))
            Regenerate(false);

        if (GUI.Button(new Rect(x + half + 4f, row, half, 24f), "Seed baru (F6)"))
            Regenerate(true);

        GUI.enabled = true;
        row += 28f;

        seedInput = GUI.TextField(new Rect(x, row, 110f, 24f), seedInput, 10);

        if (GUI.Button(new Rect(x + 114f, row, width - 114f, 24f), "Run baru dengan seed ini"))
            RestartWithSeed();
    }

    // ---------- Overlay dunia ----------

    private void HandleEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (!drawOverlay || !Visible || material == null || cam.cameraType != CameraType.Game || enemySpawner == null)
            return;

        PcgSpawnArea area = enemySpawner.CurrentArea;

        if (area == null)
            return;

        PcgSpawnRules rules = enemySpawner.ActiveRules;

        GL.PushMatrix();
        material.SetPass(0);
        GL.LoadProjectionMatrix(cam.projectionMatrix);
        GL.modelview = cam.worldToCameraMatrix;
        GL.Begin(GL.LINES);

        DrawArea(area);

        Vector3? player = area.PlayerEntryPoint;

        if (player.HasValue && rules != null)
            Circle(player.Value, rules.minDistanceFromPlayer, SafeColor);

        float radius = rules != null ? rules.checkRadius : 0.35f;

        DrawPoints(enemySpawner.LastReport.placed, radius, EnemyColor);
        DrawPoints(enemySpawner.AnchorPositions, radius, AnchorColor);

        if (itemSpawner != null)
            DrawPoints(itemSpawner.LastReport.placed, 0.25f, ItemColor);

        if (drawRejected)
        {
            DrawRejected(enemySpawner.LastReport.rejected, 0.15f);

            if (itemSpawner != null)
                DrawRejected(itemSpawner.LastReport.rejected, 0.1f);
        }

        GL.End();
        GL.PopMatrix();
    }

    private static void DrawArea(PcgSpawnArea area)
    {
        Vector3 center = area.WorldCenter + Vector3.up * 0.05f;
        Quaternion yaw = Quaternion.Euler(0f, area.transform.eulerAngles.y, 0f);
        Vector2 half = area.Size * 0.5f;

        Vector3 a = center + yaw * new Vector3(-half.x, 0f, -half.y);
        Vector3 b = center + yaw * new Vector3(half.x, 0f, -half.y);
        Vector3 c = center + yaw * new Vector3(half.x, 0f, half.y);
        Vector3 d = center + yaw * new Vector3(-half.x, 0f, half.y);

        GL.Color(AreaColor);
        Segment(a, b);
        Segment(b, c);
        Segment(c, d);
        Segment(d, a);
    }

    private static void DrawPoints(IReadOnlyList<Vector3> points, float radius, Color color)
    {
        foreach (Vector3 point in points)
        {
            Circle(point, radius, color);
            Circle(point, radius * 0.5f, color);
            GL.Color(color);
            Segment(point, point + Vector3.up * 1.2f);
        }
    }

    private static void DrawRejected(IReadOnlyList<PcgRejectedCandidate> rejected, float size)
    {
        foreach (PcgRejectedCandidate candidate in rejected)
        {
            if (candidate.reason == PcgReject.NoGround)
                continue;

            GL.Color(PcgDirector.RejectColor(candidate.reason));
            Vector3 p = candidate.position + Vector3.up * 0.05f;
            Segment(p + new Vector3(-size, 0f, -size), p + new Vector3(size, 0f, size));
            Segment(p + new Vector3(-size, 0f, size), p + new Vector3(size, 0f, -size));
        }
    }

    private static void Circle(Vector3 center, float radius, Color color)
    {
        const int segments = 32;

        GL.Color(color);
        center += Vector3.up * 0.05f;
        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Segment(previous, point);
            previous = point;
        }
    }

    private static void Segment(Vector3 from, Vector3 to)
    {
        GL.Vertex(from);
        GL.Vertex(to);
    }
}
