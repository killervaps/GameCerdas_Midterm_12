using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Pandu berurutan di grid dungeon, dipakai penanda arah (grup musuh, portal Goal).
//   1. Search: BFS dari cell MC ke seluruh grid. Pintu vault yang masih tertutup dianggap dinding.
//   2. Follow: rute BFS ke target dipecah menjadi rencana titik pandu berurutan: pintu keluar room, belokan koridor,
//      pintu masuk room berikutnya, dst., lalu target itu sendiri. Hanya satu titik yang dipakai pada satu waktu.
//      Titik berikutnya baru dipakai setelah MC sampai di titik sekarang (jarak BFS ke cell titik <= Reach Cells).
//      Setiap titik terlihat dari titik sebelumnya (garis lurus hanya lewat cell lantai, dengan jarak aman dari dinding).
//   3. Rencana dibuat ulang jika target pindah jauh, MC menjauh dari titik sekarang, atau pemilik meminta (RequestReplan).
public class DungeonGuide
{
    // Titik pandu dianggap tercapai jika jarak jalan (BFS) dari cell MC ke cell titik paling banyak segini.
    public int reachCells = 1;
    // Rencana dibuat ulang jika MC menjauh dari titik sekarang lebih dari segini cell dibanding saat titik itu mulai dipakai.
    public int strayCells = 4;
    // Rencana dibuat ulang jika cell target bergeser lebih dari segini dari ujung rencana.
    public int targetMoveCells = 3;
    // Jarak aman garis pandang dari dinding, dalam cell. Garis yang menyerempet sudut dinding dianggap terhalang.
    public float sightClearance = 0.3f;
    // Nama titik terakhir di status debug, mis. "grup" atau "portal".
    public string targetName = "target";

    private const int MaxPlanNodes = 32;

    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    private struct Node
    {
        public Vector3 point;
        public Vector2Int cell;
        // Posisi cell titik ini di rute.
        public int index;
        public string kind;
    }

    private int[] distance = new int[0];
    private int[] parent = new int[0];
    private int[] queue = new int[0];
    private int width;

    private readonly List<Vector2Int> route = new List<Vector2Int>();
    private readonly List<Vector3> routeWorld = new List<Vector3>();
    private readonly List<Node> plan = new List<Node>();
    private int planIndex;
    private int nodeStartDistance;
    private Vector2Int planTarget;
    private Vector3 finalPoint;
    private bool hasPlan;
    private bool replanRequested;

    private static Material debugMaterial;

    public bool HasPlan => hasPlan;

    // Nomor titik yang sedang dipakai (mulai 0) dan jumlah titik rencana.
    public int StepIndex => planIndex;

    public int StepCount => plan.Count;

    public bool AtFinalStep => hasPlan && planIndex == plan.Count - 1;

    public string CurrentKind => hasPlan ? plan[planIndex].kind : string.Empty;

    // Titik yang harus ditunjuk penanda. Titik terakhir mengikuti posisi target terbaru.
    public Vector3 CurrentPoint => !hasPlan ? finalPoint : AtFinalStep ? finalPoint : plan[planIndex].point;

    // Jarak jalan ke target dalam cell, -1 jika tidak terjangkau.
    public int TargetDistance { get; private set; } = -1;

    public void Reset()
    {
        hasPlan = false;
        replanRequested = false;
        plan.Clear();
        routeWorld.Clear();
        TargetDistance = -1;
    }

    public void RequestReplan()
    {
        replanRequested = true;
    }

    // ---------- Pencarian ----------

    // BFS dari posisi MC. False = posisi MC tidak bisa dipetakan ke cell lantai.
    public bool Search(DungeonGenerator dungeon, Vector3 from)
    {
        if (!TryCellOf(dungeon, from, out Vector2Int start))
            return false;

        RunBfs(dungeon, start);
        return true;
    }

    public int DistanceTo(Vector2Int cell)
    {
        int index = cell.x + cell.y * width;
        return cell.x >= 0 && cell.y >= 0 && cell.x < width && index < distance.Length ? distance[index] : -1;
    }

    // Cell dekat ujung rencana sekarang. Dipakai pemilik untuk histeresis saat memilih target.
    public bool IsNearPlannedTarget(Vector2Int cell)
    {
        return hasPlan && Chebyshev(cell, planTarget) <= targetMoveCells;
    }

    // Dipanggil setelah Search. False = target tidak terjangkau dari MC.
    public bool Follow(DungeonGenerator dungeon, Vector2Int targetCell, Vector3 targetPoint)
    {
        int targetDistance = DistanceTo(targetCell);
        TargetDistance = targetDistance;
        finalPoint = targetPoint;

        if (targetDistance < 0)
        {
            hasPlan = false;
            return false;
        }

        if (NeedsReplan(targetCell))
            BuildPlan(dungeon, targetCell);

        Advance();
        return true;
    }

    private bool NeedsReplan(Vector2Int targetCell)
    {
        if (!hasPlan || replanRequested)
            return true;

        if (Chebyshev(targetCell, planTarget) > targetMoveCells)
            return true;

        int current = DistanceTo(plan[planIndex].cell);
        return current < 0 || current > nodeStartDistance + strayCells;
    }

    // Maju ke titik berikutnya hanya jika titik sekarang sudah dicapai, satu per satu.
    private void Advance()
    {
        while (planIndex < plan.Count - 1)
        {
            int current = DistanceTo(plan[planIndex].cell);

            if (current < 0 || current > reachCells)
                break;

            planIndex++;
            nodeStartDistance = Mathf.Max(0, DistanceTo(plan[planIndex].cell));
        }
    }

    // Dari titik sekarang, cari batas region pertama (keluar room, atau keluar koridor ke room). Cell terakhir
    // sebelum batas terlihat = titik di ambang pintu, lalu lanjut dari cell pertama region berikutnya. Room berbentuk
    // persegi, jadi di dalam room pintunya selalu terlihat dan cukup satu titik per room. Di koridor yang berbelok,
    // titik ditaruh di cell terjauh yang masih terlihat, lalu lanjut dari situ.
    private void BuildPlan(DungeonGenerator dungeon, Vector2Int targetCell)
    {
        replanRequested = false;
        hasPlan = true;
        planTarget = targetCell;
        planIndex = 0;
        plan.Clear();
        route.Clear();
        routeWorld.Clear();

        for (int index = targetCell.x + targetCell.y * width; index >= 0; index = parent[index])
            route.Add(new Vector2Int(index % width, index / width));

        route.Reverse();

        for (int i = 0; i < route.Count; i++)
            routeWorld.Add(dungeon.CellToWorld(route[i]));

        int last = route.Count - 1;
        int from = 0;

        while (from < last && plan.Count < MaxPlanNodes - 1)
        {
            int region = RoomIndex(dungeon, route[from]);
            int boundary = last;

            for (int i = from + 1; i <= last; i++)
            {
                if (RoomIndex(dungeon, route[i]) != region)
                {
                    boundary = i;
                    break;
                }
            }

            // Batas region di tengah rute: cukup terlihat sampai cell terakhir sebelum batas (sisi dalam pintu).
            bool regionEnds = boundary < last;
            int limit = regionEnds ? boundary - 1 : last;
            int visible = from;

            for (int i = from + 1; i <= limit; i++)
            {
                // Cell tetangga langsung selalu bisa dituju, sisanya harus terlihat dari titik awal segmen.
                if (i > from + 1 && !GridLineOfSight(dungeon, route[from], route[i]))
                    break;

                visible = i;
            }

            // Target sudah terlihat di region yang sama.
            if (!regionEnds && visible == last)
                break;

            Node node;

            if (regionEnds && visible == limit)
            {
                node.point = (dungeon.CellToWorld(route[boundary - 1]) + dungeon.CellToWorld(route[boundary])) * 0.5f;
                node.cell = route[boundary];
                node.index = boundary;
                node.kind = region >= 0 ? "pintu keluar room" : "pintu masuk room";
                from = boundary;
            }
            else
            {
                node.point = dungeon.CellToWorld(route[visible]);
                node.cell = route[visible];
                node.index = visible;
                node.kind = region >= 0 ? "dalam room" : "belokan koridor";
                from = visible;
            }

            // Dua titik yang hanya berjarak satu cell di rute (mis. koridor sepanjang satu cell di antara dua pintu)
            // digabung menjadi titik yang lebih jauh, supaya tidak ada dua titik yang tercapai bersamaan.
            if (plan.Count > 0 && node.index - plan[plan.Count - 1].index <= 1)
                plan[plan.Count - 1] = node;
            else
                plan.Add(node);
        }

        plan.Add(new Node { point = finalPoint, cell = route[last], index = last, kind = targetName });
        nodeStartDistance = Mathf.Max(0, DistanceTo(plan[0].cell));
    }

    private void RunBfs(DungeonGenerator dungeon, Vector2Int start)
    {
        int height = dungeon.MapHeight;
        int total = dungeon.MapWidth * height;

        if (distance.Length != total || width != dungeon.MapWidth)
        {
            distance = new int[total];
            parent = new int[total];
            queue = new int[total];
            hasPlan = false;
        }

        width = dungeon.MapWidth;

        for (int i = 0; i < total; i++)
            distance[i] = -1;

        int head = 0;
        int tail = 0;
        int startIndex = start.x + start.y * width;
        distance[startIndex] = 0;
        parent[startIndex] = -1;
        queue[tail++] = startIndex;

        while (head < tail)
        {
            int current = queue[head++];
            Vector2Int cell = new Vector2Int(current % width, current / width);

            for (int k = 0; k < Directions.Length; k++)
            {
                Vector2Int next = cell + Directions[k];

                if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height)
                    continue;

                int nextIndex = next.x + next.y * width;

                if (distance[nextIndex] >= 0 || !Passable(dungeon, next))
                    continue;

                distance[nextIndex] = distance[current] + 1;
                parent[nextIndex] = current;
                queue[tail++] = nextIndex;
            }
        }
    }

    // ---------- Grid ----------

    // Cell lantai, kecuali pintu vault yang belum terbuka.
    public static bool Passable(DungeonGenerator dungeon, Vector2Int cell)
    {
        if (!dungeon.IsWalkable(cell))
            return false;

        DungeonVault vault = DungeonVault.Active;

        if (vault == null || vault.Door == null || vault.Door.IsOpen)
            return true;

        IReadOnlyList<Vector2Int> doorCells = dungeon.VaultDoorCells;

        for (int i = 0; i < doorCells.Count; i++)
        {
            if (doorCells[i] == cell)
                return false;
        }

        return true;
    }

    // Cell lantai tempat sebuah posisi dunia berada. Mesh dinding tidak selalu mengisi penuh cell-nya, jadi MC
    // atau musuh yang menempel dinding bisa terbaca berada di cell dinding. Dalam kasus itu dipilih cell lantai
    // tetangga terdekat yang tidak terhalang collider dinding dari posisi tersebut, supaya tidak pernah
    // terambil cell di balik dinding.
    public static bool TryCellOf(DungeonGenerator dungeon, Vector3 world, out Vector2Int result)
    {
        Vector2Int raw = dungeon.WorldToCell(world);
        result = raw;

        if (Passable(dungeon, raw))
            return true;

        int mask = GameLayers.ObstacleMask;
        Vector3 eye = world + Vector3.up * 0.6f;
        float bestClear = float.MaxValue;
        float bestBlocked = float.MaxValue;
        Vector2Int clearCell = raw;
        Vector2Int blockedCell = raw;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                Vector2Int cell = new Vector2Int(raw.x + dx, raw.y + dy);

                if ((dx == 0 && dy == 0) || !Passable(dungeon, cell))
                    continue;

                Vector3 center = dungeon.CellToWorld(cell);
                Vector3 offset = center - world;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                bool blocked = Physics.Linecast(eye, center + Vector3.up * 0.6f, mask, QueryTriggerInteraction.Ignore);

                if (!blocked && sqr < bestClear)
                {
                    bestClear = sqr;
                    clearCell = cell;
                }
                else if (blocked && sqr < bestBlocked)
                {
                    bestBlocked = sqr;
                    blockedCell = cell;
                }
            }
        }

        if (bestClear < float.MaxValue)
        {
            result = clearCell;
            return true;
        }

        if (bestBlocked < float.MaxValue)
        {
            result = blockedCell;
            return true;
        }

        return false;
    }

    private static int RoomIndex(DungeonGenerator dungeon, Vector2Int cell)
    {
        IReadOnlyList<DungeonRoom> rooms = dungeon.Rooms;

        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].Contains(cell))
                return i;
        }

        return -1;
    }

    // Garis lurus antar pusat cell, disampel tiap sepersepuluh cell. Setiap sampel diperiksa bersama empat titik di
    // sekitarnya (jarak Sight Clearance), jadi garis yang menyerempet sudut dinding dianggap terhalang.
    private bool GridLineOfSight(DungeonGenerator dungeon, Vector2Int a, Vector2Int b)
    {
        Vector2 from = new Vector2(a.x + 0.5f, a.y + 0.5f);
        Vector2 to = new Vector2(b.x + 0.5f, b.y + 0.5f);
        int samples = Mathf.CeilToInt(Vector2.Distance(from, to) * 10f);

        for (int i = 1; i < samples; i++)
        {
            Vector2 p = Vector2.Lerp(from, to, i / (float)samples);

            if (!PassableAt(dungeon, p) ||
                !PassableAt(dungeon, p + new Vector2(sightClearance, 0f)) ||
                !PassableAt(dungeon, p - new Vector2(sightClearance, 0f)) ||
                !PassableAt(dungeon, p + new Vector2(0f, sightClearance)) ||
                !PassableAt(dungeon, p - new Vector2(0f, sightClearance)))
                return false;
        }

        return true;
    }

    private static bool PassableAt(DungeonGenerator dungeon, Vector2 gridPoint)
    {
        return Passable(dungeon, new Vector2Int(Mathf.FloorToInt(gridPoint.x), Mathf.FloorToInt(gridPoint.y)));
    }

    private static int Chebyshev(Vector2Int a, Vector2Int b)
    {
        return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }

    // ---------- Debug F1 ----------

    // Material garis debug bersama (tembus dinding). Null jika shader tidak ditemukan.
    public static Material DebugMaterial
    {
        get
        {
            if (debugMaterial != null)
                return debugMaterial;

            Shader shader = Shader.Find("Hidden/Internal-Colored");

            if (shader == null)
                return null;

            debugMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            debugMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            debugMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            debugMaterial.SetInt("_Cull", (int)CullMode.Off);
            debugMaterial.SetInt("_ZWrite", 0);
            debugMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            return debugMaterial;
        }
    }

    // Dipanggil di dalam GL.Begin(GL.LINES). Garis = rute, tiang abu = titik yang sudah lewat,
    // tiang biru = titik berikutnya, tiang putih tinggi = titik yang sedang ditunjuk.
    public void DrawDebugLines(Color routeColor, float markerHeight)
    {
        Vector3 lift = Vector3.up * 0.15f;

        GL.Color(routeColor);

        for (int i = 1; i < routeWorld.Count; i++)
        {
            GL.Vertex(routeWorld[i - 1] + lift);
            GL.Vertex(routeWorld[i] + lift);
        }

        if (!hasPlan)
            return;

        for (int i = 0; i < plan.Count; i++)
        {
            if (i == planIndex)
                continue;

            Vector3 point = i == plan.Count - 1 ? finalPoint : plan[i].point;
            GL.Color(i < planIndex ? new Color(0.6f, 0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.7f, 1f, 0.9f));
            GL.Vertex(point + lift);
            GL.Vertex(point + Vector3.up * 1.2f);
        }

        GL.Color(Color.white);
        GL.Vertex(CurrentPoint + lift);
        GL.Vertex(CurrentPoint + Vector3.up * (markerHeight + 0.8f));
    }

    // Satu baris status untuk panel debug, mis. "2/5 pintu keluar room".
    public string DescribeStep()
    {
        return hasPlan ? (planIndex + 1) + "/" + plan.Count + " " + CurrentKind : "tanpa rencana";
    }
}
