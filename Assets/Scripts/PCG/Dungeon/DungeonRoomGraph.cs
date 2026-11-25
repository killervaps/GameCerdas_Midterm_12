using System.Collections.Generic;
using UnityEngine;

// Graf ruangan satu dungeon, dibangun DungeonGenerator setiap dungeon selesai dibuat.
//   Node     setiap room.
//   Edge     dua room bertetangga jika bisa saling dicapai lewat lantai di luar room (koridor) tanpa melewati
//            room lain, atau jika lantainya langsung bersentuhan.
//   Hop      jarak BFS di graf (jumlah perpindahan room). Dipakai objective untuk memilih room spawn dan zona.
//   Pintu    EntranceCount = jumlah celah lantai di keliling room. Satu celah bisa beberapa cell (koridor lebar).
public class DungeonRoomGraph
{
    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    private readonly DungeonGenerator dungeon;
    private readonly int width;
    private readonly int height;
    // Indeks room tiap cell, -1 = bukan room (koridor atau dinding).
    private readonly int[] roomOfCell;
    private readonly List<int>[] neighbors;
    private readonly int[,] hops;
    private readonly int[] entrances;

    public int RoomCount => dungeon.Rooms.Count;

    public DungeonGenerator Dungeon => dungeon;

    private DungeonRoomGraph(DungeonGenerator dungeon)
    {
        this.dungeon = dungeon;
        width = dungeon.MapWidth;
        height = dungeon.MapHeight;

        int count = dungeon.Rooms.Count;
        roomOfCell = new int[width * height];
        neighbors = new List<int>[count];
        hops = new int[count, count];
        entrances = new int[count];

        for (int i = 0; i < roomOfCell.Length; i++)
            roomOfCell[i] = -1;

        for (int r = 0; r < count; r++)
        {
            neighbors[r] = new List<int>();
            DungeonRoom room = dungeon.Rooms[r];

            for (int x = room.x; x < room.x + room.width; x++)
            {
                for (int y = room.y; y < room.y + room.height; y++)
                {
                    if (Inside(x, y))
                        roomOfCell[x + y * width] = r;
                }
            }
        }
    }

    public static DungeonRoomGraph Build(DungeonGenerator dungeon)
    {
        if (dungeon == null || !dungeon.HasLayout)
            return null;

        DungeonRoomGraph graph = new DungeonRoomGraph(dungeon);

        for (int r = 0; r < graph.RoomCount; r++)
        {
            graph.FindNeighbors(r);
            graph.entrances[r] = graph.CountEntrances(r);
        }

        graph.ComputeHops();
        return graph;
    }

    // ---------- API ----------

    // Indeks room di cell ini, -1 jika koridor atau dinding.
    public int RoomAt(Vector2Int cell)
    {
        return Inside(cell.x, cell.y) ? roomOfCell[cell.x + cell.y * width] : -1;
    }

    // Room tempat posisi dunia ini berada, -1 jika di koridor. Posisi yang menempel dinding dipetakan ke cell
    // lantai terdekat di sisi yang sama (DungeonGuide.TryCellOf).
    public int RoomOf(Vector3 world)
    {
        return DungeonGuide.TryCellOf(dungeon, world, out Vector2Int cell) ? RoomAt(cell) : -1;
    }

    public IReadOnlyList<int> Neighbors(int room)
    {
        return neighbors[room];
    }

    // Jumlah perpindahan room dari a ke b. 0 = room sama, -1 = tidak terhubung.
    public int HopDistance(int a, int b)
    {
        if (a < 0 || b < 0 || a >= RoomCount || b >= RoomCount)
            return -1;

        return hops[a, b];
    }

    public int EntranceCount(int room)
    {
        return room >= 0 && room < RoomCount ? entrances[room] : 0;
    }

    public DungeonRoom Room(int index)
    {
        return dungeon.Rooms[index];
    }

    public Vector3 Center(int room)
    {
        return dungeon.CellToWorld(dungeon.Rooms[room].Center);
    }

    // ---------- Pembangunan ----------

    // BFS dari semua cell room lewat lantai non-room. Masuk ke cell room lain = tetangga, BFS tidak melewatinya.
    private void FindNeighbors(int room)
    {
        HashSet<int> found = new HashSet<int>();
        bool[] visited = new bool[width * height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        DungeonRoom r = dungeon.Rooms[room];

        for (int x = r.x; x < r.x + r.width; x++)
        {
            for (int y = r.y; y < r.y + r.height; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);

                if (!dungeon.IsWalkable(cell))
                    continue;

                visited[x + y * width] = true;
                queue.Enqueue(cell);
            }
        }

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();

            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = cell + direction;

                if (!Inside(next.x, next.y) || !dungeon.IsWalkable(next))
                    continue;

                int index = next.x + next.y * width;

                if (visited[index])
                    continue;

                visited[index] = true;
                int other = roomOfCell[index];

                if (other == room)
                    continue;

                if (other >= 0)
                {
                    found.Add(other);
                    continue;
                }

                queue.Enqueue(next);
            }
        }

        neighbors[room].AddRange(found);
        neighbors[room].Sort();
    }

    // Cell lantai di luar room yang bersebelahan dengan keliling room, dikelompokkan menjadi celah yang bersambung.
    private int CountEntrances(int room)
    {
        DungeonRoom r = dungeon.Rooms[room];
        HashSet<Vector2Int> gaps = new HashSet<Vector2Int>();

        for (int x = r.x; x < r.x + r.width; x++)
        {
            for (int y = r.y; y < r.y + r.height; y++)
            {
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int outside = new Vector2Int(x, y) + direction;

                    if (!r.Contains(outside) && Inside(outside.x, outside.y) && dungeon.IsWalkable(outside))
                        gaps.Add(outside);
                }
            }
        }

        int groups = 0;
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        Stack<Vector2Int> stack = new Stack<Vector2Int>();

        foreach (Vector2Int start in gaps)
        {
            if (!seen.Add(start))
                continue;

            groups++;
            stack.Push(start);

            while (stack.Count > 0)
            {
                Vector2Int cell = stack.Pop();

                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = cell + direction;

                    if (gaps.Contains(next) && seen.Add(next))
                        stack.Push(next);
                }
            }
        }

        return groups;
    }

    private void ComputeHops()
    {
        int count = RoomCount;
        Queue<int> queue = new Queue<int>();

        for (int start = 0; start < count; start++)
        {
            for (int i = 0; i < count; i++)
                hops[start, i] = -1;

            hops[start, start] = 0;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                foreach (int next in neighbors[current])
                {
                    if (hops[start, next] >= 0)
                        continue;

                    hops[start, next] = hops[start, current] + 1;
                    queue.Enqueue(next);
                }
            }
        }
    }

    private bool Inside(int x, int y)
    {
        return x >= 0 && y >= 0 && x < width && y < height;
    }
}
