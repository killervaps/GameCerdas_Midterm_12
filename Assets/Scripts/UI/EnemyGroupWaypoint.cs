using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Penanda arah ke grup musuh terdekat, supaya pemain tidak bingung masih ada musuh di mana, terutama di dungeon
// dengan kamera third-person. Tampilannya sama dengan GoalIndicator: belah ketupat di dunia saat terlihat, panah di
// tepi layar saat tidak, plus jarak jalan dalam meter.
//   - Tiap Update Interval, musuh hidup dikelompokkan dengan single-link clustering (radius Cluster Radius).
//   - Mode Dungeon: DungeonGuide (BFS di grid), grup terdekat = jarak BFS terpendek. Penanda memandu berurutan,
//     satu titik pada satu waktu: pintu keluar room, belokan koridor, pintu masuk room berikutnya, lalu grup.
//   - Mode Arena (tanpa grid): jalur NavMesh terpendek ke centroid grup, penanda di tikungan berikutnya.
//   - Muncul setelah WaypointDelay detik (knob DDA) tanpa pertempuran. Pertempuran = dalam Combat Memory detik
//     terakhir MC menerima atau memberi damage, atau ada musuh yang tahu posisi MC. Pertempuran lagi = penanda
//     hilang dan hitungan diulang.
//   - Reveal: saat sisa musuh <= Reveal Count, setiap musuh yang ada di layar mendapat titik merah sendiri
//     (menembus tembok). Musuh di luar layar tidak diberi panah lurus, karena panah itu menembus dinding.
//     Selama reveal, penanda pandu berurutan muncul tanpa menunggu delay (cukup tidak sedang bertarung).
// Ditampilkan GameUI hanya saat Battle. F1 menggambar rute, semua titik rencana, dan garis antar anggota grup.
public class EnemyGroupWaypoint : UIScreen
{
    [Header("Grup")]
    [SerializeField, Min(0.05f)] private float updateInterval = 0.5f;
    [SerializeField, Min(0.5f)] private float clusterRadius = 6f;
    // Jarak cari titik NavMesh terdekat untuk centroid grup dan posisi MC (Mode Arena).
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 4f;
    // Mode Arena: tikungan yang lebih dekat dari ini ke MC dilewati.
    [SerializeField, Min(0f)] private float cornerMinDistance = 3f;

    [Header("Pandu Dungeon")]
    // Titik pandu dianggap tercapai jika jarak jalan (BFS) dari cell MC ke cell titik paling banyak segini.
    [SerializeField, Min(0)] private int reachCells = 1;
    // Rencana dibuat ulang jika MC menjauh dari titik sekarang lebih dari segini cell.
    [SerializeField, Min(1)] private int strayCells = 4;
    // Rencana dibuat ulang jika cell grup bergeser lebih dari segini dari ujung rencana.
    [SerializeField, Min(1)] private int targetMoveCells = 3;
    // Jarak aman garis pandang dari dinding, dalam cell.
    [SerializeField, Range(0f, 0.45f)] private float sightClearance = 0.3f;

    [Header("Pertempuran")]
    [SerializeField, Min(0f)] private float combatMemory = 5f;
    // Dipakai jika DifficultyModel tidak ada di scene.
    [SerializeField, Min(0f)] private float fallbackDelay = 20f;

    [Header("Reveal Musuh Terakhir")]
    [SerializeField, Min(0)] private int revealCount = 3;
    [SerializeField] private float revealHeight = 2.4f;
    [SerializeField] private float revealDotSize = 18f;

    [Header("Tampilan")]
    [SerializeField] private float edgeMargin = 80f;
    [SerializeField] private float markerHeight = 1.4f;
    [SerializeField] private float arrowSize = 40f;
    [SerializeField] private float diamondSize = 26f;
    [SerializeField] private float bobAmount = 5f;
    [SerializeField] private Color markerColor = new Color(1f, 0.4f, 0.4f);

    [Header("Debug (F1)")]
    [SerializeField] private bool drawDebugPath = true;

    private const int MaxCorners = 64;

    // Buffer dipakai ulang supaya clustering dan path tidak mengalokasi tiap update.
    private readonly List<EnemyHealth> members = new List<EnemyHealth>();
    private readonly List<Vector3> positions = new List<Vector3>();
    private int[] parent = new int[16];
    private int[] clusterSize = new int[16];
    private Vector3[] clusterSum = new Vector3[16];
    private readonly Vector3[] corners = new Vector3[MaxCorners];
    private readonly List<Vector3> arenaRoute = new List<Vector3>();
    private NavMeshPath path;

    private readonly DungeonGuide guide = new DungeonGuide { targetName = "grup" };
    private bool usingDungeon;
    private bool wasShown;

    private int bestRoot = -1;
    private int bestMembers;
    private float bestLength;
    private Vector3 bestCentroid;
    private Vector3 waypointPoint;
    private bool hasGroup;
    private string guideLabel = string.Empty;

    private float lastCombatTime = float.NegativeInfinity;
    private float quietTime;
    private float nextUpdate;
    private PlayerHealth subscribedPlayer;
    private Camera cam;

    private RectTransform marker;
    private CanvasGroup markerGroup;
    private RectTransform arrow;
    private RectTransform diamond;
    private TextMeshProUGUI label;

    private readonly List<RevealMarker> revealMarkers = new List<RevealMarker>();

    public static EnemyGroupWaypoint Current { get; private set; }

    // Sedang dipakai di ronde ini (layar terlihat saat Battle).
    public bool IsTracking => IsVisible;

    public bool WaypointShown { get; private set; }

    public bool RevealActive { get; private set; }

    // Musuh yang sedang di-reveal (titik merah), dipakai minimap. Kosong jika reveal tidak aktif.
    public IReadOnlyList<EnemyHealth> RevealedEnemies => RevealActive ? members : NoEnemies;

    private static readonly EnemyHealth[] NoEnemies = new EnemyHealth[0];

    public float Delay => DifficultyModel.Current != null ? DifficultyModel.Current.WaypointDelay : fallbackDelay;

    public bool InCombat => Time.time - lastCombatTime < combatMemory || AnyAware;

    public float QuietTime => quietTime;

    // Satu baris untuk panel DDA.
    public string DebugStatus
    {
        get
        {
            string reveal = RevealActive ? "<color=#ff6655>reveal " + members.Count + "</color>, " : string.Empty;

            string state = reveal + (InCombat
                ? "<color=#ffaa33>bertarung</color>"
                : WaypointShown
                    ? "<color=#66ff88>tampil</color>"
                    : RevealActive ? "menunggu" : "tenang " + quietTime.ToString("0") + "/" + Delay.ToString("0") + " s");

            if (!hasGroup)
                return state + ", tidak ada jalur";

            string step = usingDungeon ? guide.DescribeStep() : guideLabel;
            return state + ", grup " + bestMembers + " musuh, " + bestLength.ToString("0") + " m, pandu " + step;
        }
    }

    private static bool AnyAware
    {
        get
        {
            TacticalCoordinator coordinator = TacticalCoordinator.Current;
            return coordinator != null && coordinator.Group != null && coordinator.Group.AnyAware;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    private class RevealMarker
    {
        public RectTransform root;
        public RectTransform dot;
    }

    // ---------- Build ----------

    protected override void Build()
    {
        Current = this;
        path = new NavMeshPath();

        marker = UIKit.CreateRect("GroupMarker", rect);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(arrowSize, arrowSize);
        markerGroup = marker.gameObject.AddComponent<CanvasGroup>();
        markerGroup.blocksRaycasts = false;
        markerGroup.alpha = 0f;

        arrow = CreateInkedShape("Arrow", marker, ReticleSprites.TriangleInk, ReticleSprites.Triangle, arrowSize);
        diamond = CreateInkedShape("Diamond", marker, ReticleSprites.DiamondInk, ReticleSprites.Diamond, diamondSize);

        Image labelStamp = UIKit.CreateFreeStamp("Label", marker, "BRAINROT", 18f, markerColor, 0f, out label);
        RectTransform labelRect = labelStamp.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -(arrowSize * 0.5f + 6f));

        EnemyHealth.AnyDamaged += HandleEnemyDamaged;
    }

    private RectTransform CreateInkedShape(string shapeName, RectTransform parentRect, Sprite inkSprite, Sprite fillSprite, float size)
    {
        RectTransform root = UIKit.CreateRect(shapeName, parentRect);
        root.sizeDelta = new Vector2(size, size);

        Image ink = UIKit.CreateImage("Ink", root, UIKit.Ink, false);
        ink.sprite = inkSprite;
        UIKit.Stretch(ink.rectTransform);

        Image fill = UIKit.CreateImage("Fill", root, markerColor, false);
        fill.sprite = fillSprite;
        UIKit.Stretch(fill.rectTransform);

        return root;
    }

    private RevealMarker CreateRevealMarker(int index)
    {
        RevealMarker reveal = new RevealMarker();
        reveal.root = UIKit.CreateRect("Reveal" + index, rect);
        reveal.root.anchorMin = reveal.root.anchorMax = new Vector2(0.5f, 0.5f);
        reveal.root.sizeDelta = new Vector2(revealDotSize, revealDotSize);

        reveal.dot = UIKit.CreateRect("Dot", reveal.root);
        reveal.dot.sizeDelta = new Vector2(revealDotSize, revealDotSize);
        Image ink = UIKit.CreateImage("Ink", reveal.dot, UIKit.Ink, false);
        ink.sprite = UIKit.Circle;
        UIKit.Stretch(ink.rectTransform);
        Image fill = UIKit.CreateImage("Fill", reveal.dot, UIKit.TradeOff, false);
        fill.sprite = UIKit.Circle;
        UIKit.Stretch(fill.rectTransform);
        fill.rectTransform.offsetMin = new Vector2(3f, 3f);
        fill.rectTransform.offsetMax = new Vector2(-3f, -3f);

        revealMarkers.Add(reveal);
        return reveal;
    }

    private void OnDestroy()
    {
        EnemyHealth.AnyDamaged -= HandleEnemyDamaged;
        SubscribePlayer(null);

        if (Current == this)
            Current = null;
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
    }

    protected override void OnShow()
    {
        // Ronde baru: hitungan tenang mulai dari nol.
        quietTime = 0f;
        lastCombatTime = float.NegativeInfinity;
        nextUpdate = 0f;
        hasGroup = false;
        wasShown = false;
        guide.Reset();
        WaypointShown = false;
        RevealActive = false;
        markerGroup.alpha = 0f;
    }

    protected override void OnHide()
    {
        WaypointShown = false;
        RevealActive = false;
        guide.Reset();
    }

    // ---------- Deteksi pertempuran ----------

    private void HandleEnemyDamaged(EnemyHealth enemy, float amount)
    {
        if (amount > 0f)
            lastCombatTime = Time.time;
    }

    private void HandlePlayerDamaged(float amount, DamageInfo info)
    {
        if (amount > 0f)
            lastCombatTime = Time.time;
    }

    private void SubscribePlayer(PlayerHealth player)
    {
        if (subscribedPlayer == player)
            return;

        if (subscribedPlayer != null)
            subscribedPlayer.Damaged -= HandlePlayerDamaged;

        subscribedPlayer = player;

        if (subscribedPlayer != null)
            subscribedPlayer.Damaged += HandlePlayerDamaged;
    }

    // ---------- Update ----------

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        SubscribePlayer(PlayerHealth.Current);

        if (cam == null || !cam.isActiveAndEnabled)
            cam = Camera.main;

        BattleManager battle = ui.Battle;
        PlayerHealth player = PlayerHealth.Current;

        if (battle == null || player == null || cam == null)
        {
            HideAll();
            return;
        }

        if (InCombat)
            quietTime = 0f;
        else
            quietTime += Time.deltaTime;

        // Saat reveal, pandu berurutan tidak menunggu delay, cukup tidak sedang bertarung.
        // Penanda baru muncul: pandu dimulai dari posisi MC sekarang, bukan dari rencana lama.
        bool readyToShow = RevealActive ? !InCombat : quietTime >= Delay;

        if (readyToShow && !wasShown)
        {
            guide.RequestReplan();
            nextUpdate = 0f;
        }

        wasShown = readyToShow;

        if (Time.time >= nextUpdate)
        {
            nextUpdate = Time.time + updateInterval;
            CollectAlive(battle);
            RevealActive = members.Count > 0 && members.Count <= revealCount;

            DungeonGenerator dungeon = ActiveDungeon();
            usingDungeon = dungeon != null;

            if (usingDungeon)
                UpdateDungeonGuide(dungeon, player.transform.position);
            else
                FindGroupOnNavMesh(player.transform.position);
        }

        UpdateReveal();

        WaypointShown = hasGroup && readyToShow;

        if (WaypointShown)
            DrawGroupMarker();
        else
            markerGroup.alpha = Mathf.MoveTowards(markerGroup.alpha, 0f, 8f * Time.unscaledDeltaTime);
    }

    private DungeonGenerator ActiveDungeon()
    {
        RunManager run = ui.Run;

        if (run == null || run.Mode != GameMode.Dungeon || run.Dungeon == null || !run.Dungeon.HasLayout)
            return null;

        return run.Dungeon;
    }

    private void HideAll()
    {
        markerGroup.alpha = 0f;

        foreach (RevealMarker reveal in revealMarkers)
            reveal.root.gameObject.SetActive(false);
    }

    private void CollectAlive(BattleManager battle)
    {
        members.Clear();
        positions.Clear();

        IReadOnlyList<EnemyHealth> alive = battle.Alive;

        for (int i = 0; i < alive.Count; i++)
        {
            EnemyHealth enemy = alive[i];

            if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled)
                continue;

            members.Add(enemy);
            positions.Add(enemy.transform.position);
        }
    }

    // ---------- Clustering ----------

    // Single-link clustering dengan union-find: dua musuh dalam radius = satu grup.
    private void BuildClusters()
    {
        int count = positions.Count;
        EnsureCapacity(count);

        for (int i = 0; i < count; i++)
        {
            parent[i] = i;
            clusterSize[i] = 0;
            clusterSum[i] = Vector3.zero;
        }

        float radiusSqr = clusterRadius * clusterRadius;

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if ((positions[i] - positions[j]).sqrMagnitude <= radiusSqr)
                    Union(i, j);
            }
        }

        for (int i = 0; i < count; i++)
        {
            int root = Find(i);
            clusterSize[root]++;
            clusterSum[root] += positions[i];
        }
    }

    private void EnsureCapacity(int count)
    {
        if (parent.Length >= count)
            return;

        int size = Mathf.NextPowerOfTwo(count);
        parent = new int[size];
        clusterSize = new int[size];
        clusterSum = new Vector3[size];
    }

    private int Find(int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private void Union(int a, int b)
    {
        int rootA = Find(a);
        int rootB = Find(b);

        if (rootA != rootB)
            parent[rootB] = rootA;
    }

    // ---------- Mode Dungeon ----------

    private void UpdateDungeonGuide(DungeonGenerator dungeon, Vector3 playerPosition)
    {
        hasGroup = false;

        guide.reachCells = reachCells;
        guide.strayCells = strayCells;
        guide.targetMoveCells = targetMoveCells;
        guide.sightClearance = sightClearance;

        if (positions.Count == 0)
        {
            guide.Reset();
            return;
        }

        BuildClusters();

        if (!guide.Search(dungeon, playerPosition))
            return;

        if (!FindNearestGroupCell(dungeon, out Vector2Int targetCell, out int targetDistance))
        {
            guide.Reset();
            return;
        }

        if (!guide.Follow(dungeon, targetCell, bestCentroid))
            return;

        hasGroup = true;
        bestLength = targetDistance * dungeon.TileSize;
        waypointPoint = guide.CurrentPoint;
    }

    private bool FindNearestGroupCell(DungeonGenerator dungeon, out Vector2Int targetCell, out int targetDistance)
    {
        targetCell = default;
        targetDistance = int.MaxValue;
        int bestScore = int.MaxValue;

        for (int root = 0; root < positions.Count; root++)
        {
            if (clusterSize[root] == 0)
                continue;

            Vector3 centroid = clusterSum[root] / clusterSize[root];
            int distance = int.MaxValue;
            Vector2Int cell = default;

            // Centroid bisa jatuh di dinding atau cell tak terjangkau, jadi anggota grup ikut dicoba.
            if (DungeonGuide.TryCellOf(dungeon, centroid, out Vector2Int c) && guide.DistanceTo(c) >= 0)
            {
                distance = guide.DistanceTo(c);
                cell = c;
            }
            else
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    if (Find(i) != root || !DungeonGuide.TryCellOf(dungeon, positions[i], out c))
                        continue;

                    int d = guide.DistanceTo(c);

                    if (d >= 0 && d < distance)
                    {
                        distance = d;
                        cell = c;
                    }
                }
            }

            if (distance == int.MaxValue)
                continue;

            // Histeresis: grup yang sedang dipandu diberi keunggulan beberapa cell, supaya pandu tidak berganti-ganti
            // antara dua grup yang jaraknya hampir sama.
            int score = guide.IsNearPlannedTarget(cell) ? distance - targetMoveCells : distance;

            if (score >= bestScore)
                continue;

            bestScore = score;
            targetDistance = distance;
            targetCell = cell;
            bestRoot = root;
            bestMembers = clusterSize[root];
            bestCentroid = centroid;
        }

        return targetDistance != int.MaxValue;
    }

    // ---------- Mode Arena: jalur NavMesh ----------

    private void FindGroupOnNavMesh(Vector3 playerPosition)
    {
        hasGroup = false;
        arenaRoute.Clear();

        if (positions.Count == 0)
            return;

        BuildClusters();

        if (!NavMesh.SamplePosition(playerPosition, out NavMeshHit playerHit, navMeshSampleDistance, NavMesh.AllAreas))
            return;

        bestLength = float.MaxValue;

        for (int root = 0; root < positions.Count; root++)
        {
            if (clusterSize[root] == 0)
                continue;

            Vector3 centroid = clusterSum[root] / clusterSize[root];

            if (!NavMesh.SamplePosition(centroid, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
                continue;

            if (!NavMesh.CalculatePath(playerHit.position, hit.position, NavMesh.AllAreas, path) || path.status == NavMeshPathStatus.PathInvalid)
                continue;

            int cornerCount = path.GetCornersNonAlloc(corners);
            float length = 0f;

            for (int i = 1; i < cornerCount; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);

            if (path.status == NavMeshPathStatus.PathPartial && cornerCount > 0)
                length += Vector3.Distance(corners[cornerCount - 1], hit.position) * 2f;

            if (length >= bestLength)
                continue;

            bestLength = length;
            bestRoot = root;
            bestMembers = clusterSize[root];
            bestCentroid = hit.position;
            hasGroup = true;

            arenaRoute.Clear();

            for (int i = 0; i < cornerCount; i++)
                arenaRoute.Add(corners[i]);
        }

        if (!hasGroup)
            return;

        // Tikungan pertama yang cukup jauh dari MC dan tidak terhalang. Tikungan terakhir = grup.
        waypointPoint = bestCentroid;
        guideLabel = "grup";
        float minSqr = cornerMinDistance * cornerMinDistance;
        int chosen = -1;

        for (int i = 1; i < arenaRoute.Count - 1; i++)
        {
            if (chosen >= 0 && NavMesh.Raycast(playerHit.position, arenaRoute[i], out _, NavMesh.AllAreas))
                break;

            chosen = i;
            Vector3 offset = arenaRoute[i] - playerPosition;
            offset.y = 0f;

            if (offset.sqrMagnitude >= minSqr)
                break;
        }

        if (chosen > 0)
        {
            waypointPoint = arenaRoute[chosen];
            guideLabel = "tikungan";
        }
    }

    // ---------- Tampilan ----------

    private void DrawGroupMarker()
    {
        markerGroup.alpha = Mathf.MoveTowards(markerGroup.alpha, 1f, 8f * Time.unscaledDeltaTime);

        float bob = Mathf.Sin(Time.unscaledTime * 4f) * bobAmount;
        bool onScreen = WaypointProjection.Project(cam, rect, edgeMargin, waypointPoint + Vector3.up * markerHeight,
            out Vector2 local, out Vector2 direction, out float edgeScale);

        label.text = bestMembers + " BRAINROT  " + Mathf.CeilToInt(bestLength) + " m";

        if (onScreen)
        {
            marker.anchoredPosition = local + Vector2.up * bob;
            arrow.gameObject.SetActive(false);
            diamond.gameObject.SetActive(true);
            return;
        }

        marker.anchoredPosition = direction * edgeScale - direction * bob;
        arrow.gameObject.SetActive(true);
        diamond.gameObject.SetActive(false);
        // Ujung sprite segitiga menunjuk ke bawah (-90 derajat), jadi diputar +90 dari sudut arah.
        arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f);
    }

    private void UpdateReveal()
    {
        int shown = RevealActive ? members.Count : 0;

        while (revealMarkers.Count < shown)
            CreateRevealMarker(revealMarkers.Count);

        for (int i = 0; i < revealMarkers.Count; i++)
        {
            RevealMarker reveal = revealMarkers[i];
            EnemyHealth enemy = i < shown ? members[i] : null;
            bool active = enemy != null && !enemy.IsDead && enemy.isActiveAndEnabled;

            if (reveal.root.gameObject.activeSelf != active)
                reveal.root.gameObject.SetActive(active);

            if (!active)
                continue;

            // Musuh di luar layar tidak diberi panah lurus (panah itu menunjuk menembus dinding).
            // Arah ke sana ditunjukkan penanda pandu berurutan.
            bool onScreen = WaypointProjection.Project(cam, rect, edgeMargin, enemy.transform.position + Vector3.up * revealHeight,
                out Vector2 local, out _, out _);

            if (reveal.dot.gameObject.activeSelf != onScreen)
                reveal.dot.gameObject.SetActive(onScreen);

            if (onScreen)
                reveal.root.anchoredPosition = local;
        }
    }

    // ---------- Debug F1 ----------

    // Kuning = rute ke grup terpilih, merah = sambungan clustering antar anggota grup itu.
    // Mode Dungeon: tiang abu = titik rencana yang sudah lewat, biru = berikutnya, putih tinggi = yang ditunjuk.
    private void HandleEndCameraRendering(ScriptableRenderContext context, Camera renderCamera)
    {
        if (!drawDebugPath || !AIDebugSettings.Enabled || !IsVisible || !hasGroup || renderCamera.cameraType != CameraType.Game)
            return;

        Material material = DungeonGuide.DebugMaterial;

        if (material == null)
            return;

        Vector3 lift = Vector3.up * 0.15f;

        GL.PushMatrix();
        material.SetPass(0);
        GL.LoadProjectionMatrix(renderCamera.projectionMatrix);
        GL.modelview = renderCamera.worldToCameraMatrix;
        GL.Begin(GL.LINES);

        if (usingDungeon)
        {
            guide.DrawDebugLines(new Color(1f, 0.85f, 0.2f, 0.9f), markerHeight);
        }
        else
        {
            GL.Color(new Color(1f, 0.85f, 0.2f, 0.9f));

            for (int i = 1; i < arenaRoute.Count; i++)
            {
                GL.Vertex(arenaRoute[i - 1] + lift);
                GL.Vertex(arenaRoute[i] + lift);
            }

            GL.Color(Color.white);
            GL.Vertex(waypointPoint + lift);
            GL.Vertex(waypointPoint + Vector3.up * (markerHeight + 0.8f));
        }

        GL.Color(new Color(1f, 0.35f, 0.3f, 0.8f));
        float radiusSqr = clusterRadius * clusterRadius;

        for (int i = 0; i < positions.Count; i++)
        {
            if (Find(i) != bestRoot)
                continue;

            for (int j = i + 1; j < positions.Count; j++)
            {
                if (Find(j) == bestRoot && (positions[i] - positions[j]).sqrMagnitude <= radiusSqr)
                {
                    GL.Vertex(positions[i] + lift);
                    GL.Vertex(positions[j] + lift);
                }
            }
        }

        GL.End();
        GL.PopMatrix();
    }
}
