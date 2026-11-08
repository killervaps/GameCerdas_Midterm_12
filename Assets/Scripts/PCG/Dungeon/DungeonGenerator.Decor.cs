using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum RoomTheme
{
    None,
    Start,
    Goal,
    Vault,
    // Barrel, pot, dan table berkelompok di sudut dan sepanjang dinding.
    Storage,
    // Batu dan tanah berserakan, lantai lebih banyak yang retak.
    Ruins,
    // Dua baris column, bagian tengah kosong untuk bertarung.
    Hall
}

// Dekorasi berbasis aturan (rule-based decoration). Setiap room diberi tema dari seed, lalu props ditaruh dengan aturan tema itu.
// Props penghalang (barrel, column, dll.) diberi collider di layer NavBlock dan NavMeshObstacle, jadi menahan MC, musuh,
// proyektil, dan pandangan, serta otomatis dihindari spawner (CheckSphere NavBlock). Setiap props penghalang diuji dulu:
// BFS dari Start dengan cell props dianggap dinding harus tetap menjangkau semua cell lantai lain. Jika tidak, props dibatalkan.
// Props kecil (batu, koin, banner) hanya visual. Semuanya memakai stream seed Decor, terpisah dari layout.
public partial class DungeonGenerator
{
    [System.Serializable]
    public class DecorProp
    {
        public GameObject prefab;
        [Min(0f)] public float weight = 1f;
        [Min(0.01f)] public float scale = 1f;
        // Geseran lokal. Sumbu Z menghadap ke dalam room (menjauhi dinding).
        public Vector3 offset = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        // True = rotasi Y acak penuh. False = menghadap ke dalam room dengan sedikit acak.
        public bool randomYaw = true;
        // Hanya untuk Rubble dan Treasure Props. True = collider NavBlock + NavMeshObstacle, ditaruh di tengah cell,
        // dan diuji BFS seperti barrel. Storage dan Column selalu menghalangi, Wall dan Corridor selalu visual.
        public bool blocking = true;
    }

    [Header("Decoration")]
    [SerializeField] private bool decoration = true;
    // Bobot tema room biasa. Start, Goal, dan Vault punya tema sendiri. Aula hanya untuk room minimal Hall Min Size.
    [SerializeField, Min(0f)] private float storageThemeWeight = 4f;
    [SerializeField, Min(0f)] private float ruinsThemeWeight = 3.5f;
    [SerializeField, Min(0f)] private float hallThemeWeight = 2.5f;
    // Sisi pendek minimal room Aula. Column berjarak 2 cell dari dinding dan antar baris tersisa minimal 2 cell.
    [SerializeField, Min(8)] private int hallMinSize = 8;
    // Tambahan peluang lantai varian di room Reruntuhan.
    [SerializeField, Range(0f, 1f)] private float ruinsFloorBonus = 0.25f;

    // Penghalang (collider + NavMeshObstacle).
    [SerializeField] private DecorProp[] storageProps;
    [SerializeField] private DecorProp[] columnProps;
    // Visual saja.
    [SerializeField] private DecorProp[] rubbleProps;
    [SerializeField] private DecorProp[] treasureProps;
    // Menempel dinding room (banner, perisai). Visual saja.
    [SerializeField] private DecorProp[] wallProps;
    // Sisi dinding koridor (penyangga kayu). Visual saja.
    [SerializeField] private DecorProp[] corridorProps;

    // Props gudang per cell keliling room.
    [SerializeField, Range(0f, 0.5f)] private float storageDensity = 0.18f;
    // Props kecil per cell lantai room (dikali faktor tema).
    [SerializeField, Range(0f, 0.3f)] private float rubbleDensity = 0.05f;
    [SerializeField, Min(2)] private int columnSpacing = 3;
    [SerializeField, Range(0, 4)] private int wallPropsPerRoom = 2;
    // Jarak minimal antar props koridor (cell, Manhattan). 0 = koridor tanpa props.
    [SerializeField, Min(0)] private int corridorPropSpacing = 6;

    private readonly List<RoomTheme> roomThemes = new List<RoomTheme>();
    private readonly HashSet<Vector2Int> propBlocked = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> propOccupied = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> propReserved = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> torchCells = new HashSet<Vector2Int>();
    private int baselineReachable;
    private int[,] baselineDistance;

    // Tema room ke-i, sejajar dengan Rooms.
    public IReadOnlyList<RoomTheme> RoomThemes => roomThemes;

    public int PropCount { get; private set; }

    public int BlockingPropCount => propBlocked.Count;

    // Props penghalang yang dibatalkan karena memutus jalur BFS.
    public int RejectedPropCount { get; private set; }

    public IReadOnlyCollection<Vector2Int> BlockingPropCells => propBlocked;

    public static string ThemeLabel(RoomTheme theme)
    {
        switch (theme)
        {
            case RoomTheme.Start: return "Start";
            case RoomTheme.Goal: return "Goal";
            case RoomTheme.Vault: return "Vault";
            case RoomTheme.Storage: return "Gudang";
            case RoomTheme.Ruins: return "Reruntuhan";
            case RoomTheme.Hall: return "Aula";
            default: return "-";
        }
    }

    public RoomTheme ThemeAt(Vector2Int cell)
    {
        for (int i = 0; i < rooms.Count && i < roomThemes.Count; i++)
        {
            if (rooms[i].Contains(cell))
                return roomThemes[i];
        }

        return RoomTheme.None;
    }

    // Dipanggil sebelum lantai direncanakan, supaya room Reruntuhan bisa mendapat lantai retak lebih banyak.
    // Setiap room selalu mengambil satu angka acak, jadi mengubah bobot tidak menggeser tema room lain.
    private void AssignRoomThemes()
    {
        roomThemes.Clear();

        SeededRandom themeRandom = new SeededRandom(SeededRandom.Derive(BuiltSeed, 2, PcgStream.Decor));

        foreach (DungeonRoom room in rooms)
        {
            float roll = themeRandom.Value;

            if (room == StartRoom)
                roomThemes.Add(RoomTheme.Start);
            else if (room == GoalRoom)
                roomThemes.Add(RoomTheme.Goal);
            else if (room == VaultRoom)
                roomThemes.Add(RoomTheme.Vault);
            else
                roomThemes.Add(PickTheme(room, roll));
        }
    }

    private RoomTheme PickTheme(DungeonRoom room, float roll)
    {
        float hall = Mathf.Min(room.width, room.height) >= hallMinSize ? hallThemeWeight : 0f;
        float total = storageThemeWeight + ruinsThemeWeight + hall;

        if (total <= 0f)
            return RoomTheme.Storage;

        float target = roll * total;

        if (target < storageThemeWeight)
            return RoomTheme.Storage;

        return target < storageThemeWeight + ruinsThemeWeight ? RoomTheme.Ruins : RoomTheme.Hall;
    }

    private float ThemeFloorBonus(Vector2Int cell)
    {
        return ThemeAt(cell) == RoomTheme.Ruins ? ruinsFloorBonus : 0f;
    }

    private void BuildDecoration()
    {
        propBlocked.Clear();
        propOccupied.Clear();
        PropCount = 0;
        RejectedPropCount = 0;

        if (!decoration || rooms.Count == 0 || roomThemes.Count != rooms.Count)
            return;

        SeededRandom decor = new SeededRandom(SeededRandom.Derive(BuiltSeed, 3, PcgStream.Decor));

        GameObject group = new GameObject("Decoration");
        group.transform.SetParent(levelRoot, false);

        ReservePropCells();

        baselineDistance = ComputeBfs(StartCell, null);
        baselineReachable = CountReachable(baselineDistance);

        for (int i = 0; i < rooms.Count; i++)
        {
            DungeonRoom room = rooms[i];

            switch (roomThemes[i])
            {
                case RoomTheme.Start:
                    PlaceRubble(group.transform, room, 0.5f, decor);
                    break;
                case RoomTheme.Goal:
                    if (Mathf.Min(room.width, room.height) >= hallMinSize)
                        PlaceColumns(group.transform, room, decor);
                    else
                        PlaceStorage(group.transform, room, 0.35f, decor);

                    PlaceWallProps(group.transform, room, wallPropsPerRoom + 1, decor);
                    PlaceRubble(group.transform, room, 1f, decor);
                    break;
                case RoomTheme.Vault:
                    PlaceTreasure(group.transform, room, decor);
                    break;
                case RoomTheme.Storage:
                    PlaceStorage(group.transform, room, 1f, decor);
                    PlaceWallProps(group.transform, room, decor.Value < 0.5f ? 1 : 0, decor);
                    PlaceRubble(group.transform, room, 0.6f, decor);
                    break;
                case RoomTheme.Ruins:
                    PlaceStorage(group.transform, room, 0.2f, decor);
                    PlaceWallProps(group.transform, room, decor.Value < 0.5f ? 1 : 0, decor);
                    PlaceRubble(group.transform, room, 3f, decor);
                    break;
                case RoomTheme.Hall:
                    PlaceColumns(group.transform, room, decor);
                    PlaceWallProps(group.transform, room, wallPropsPerRoom, decor);
                    PlaceRubble(group.transform, room, 1f, decor);
                    break;
            }
        }

        PlaceCorridorProps(group.transform, decor);
    }

    // Cell yang harus tetap kosong dari props penghalang: titik masuk MC, portal Goal, titik boss, kunci, chest,
    // bukaan room dan dua cell ke dalamnya, serta pintu vault.
    private void ReservePropCells()
    {
        propReserved.Clear();

        ReserveArea(StartCell, 1);
        ReserveArea(GoalCell, 1);

        foreach (Vector3 point in bossPoints)
            ReserveArea(WorldToCell(point), 0);

        if (VaultRoom != null)
        {
            propReserved.Add(vaultKeyCell);
            ReserveArea(VaultRoom.Center, 1);
        }

        foreach (Vector2Int cell in vaultDoorCells)
            propReserved.Add(cell);

        foreach (DungeonRoom room in rooms)
        {
            for (int x = room.x; x < room.x + room.width; x++)
            {
                ReserveEntrance(room, new Vector2Int(x, room.y), Vector2Int.down);
                ReserveEntrance(room, new Vector2Int(x, room.y + room.height - 1), Vector2Int.up);
            }

            for (int y = room.y; y < room.y + room.height; y++)
            {
                ReserveEntrance(room, new Vector2Int(room.x, y), Vector2Int.left);
                ReserveEntrance(room, new Vector2Int(room.x + room.width - 1, y), Vector2Int.right);
            }
        }
    }

    private void ReserveArea(Vector2Int center, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
                propReserved.Add(new Vector2Int(center.x + dx, center.y + dy));
        }
    }

    private void ReserveEntrance(DungeonRoom room, Vector2Int edge, Vector2Int outward)
    {
        Vector2Int outside = edge + outward;

        if (!IsInsideGrid(outside.x, outside.y) || !IsWalkable(grid[outside.x, outside.y]) || room.Contains(outside))
            return;

        propReserved.Add(outside);
        propReserved.Add(edge);
        propReserved.Add(edge - outward);
    }

    private static int CountReachable(int[,] distance)
    {
        int count = 0;

        foreach (int value in distance)
        {
            if (value >= 0)
                count++;
        }

        return count;
    }

    // Constraint konektivitas: props penghalang hanya diterima jika semua cell lantai lain tetap terjangkau dari Start.
    private bool TryBlockCell(Vector2Int cell)
    {
        if (propReserved.Contains(cell) || propOccupied.Contains(cell) || cell == StartCell)
            return false;

        if (baselineDistance == null || baselineDistance[cell.x, cell.y] < 0)
            return false;

        propBlocked.Add(cell);

        if (CountReachable(ComputeBfs(StartCell, propBlocked)) == baselineReachable - propBlocked.Count)
            return true;

        propBlocked.Remove(cell);
        RejectedPropCount++;
        return false;
    }

    // Gudang: props berkelompok di sepanjang dinding. Sudut room dan cell di samping props yang sudah ada lebih mungkin dipilih.
    private void PlaceStorage(Transform parent, DungeonRoom room, float factor, SeededRandom decor)
    {
        if (!HasProps(storageProps))
            return;

        List<TorchSlot> candidates = RoomEdgeSlots(room);
        int count = Mathf.RoundToInt(2f * (room.width + room.height) * storageDensity * factor);
        List<Vector2Int> placedHere = new List<Vector2Int>();

        while (count > 0 && candidates.Count > 0)
        {
            int index = PickWeightedSlot(candidates, placedHere, decor);
            TorchSlot slot = candidates[index];
            candidates.RemoveAt(index);

            if (!TryBlockCell(slot.cell))
                continue;

            DecorProp prop = PickProp(storageProps, decor);
            Vector3 toWall = new Vector3(slot.toWall.x, 0f, slot.toWall.y);
            Vector3 position = CellToWorld(slot.cell) + toWall * tileSize * 0.12f;
            SpawnDecor(parent, prop, position, FacingAwayFrom(toWall, prop, decor), true, decor);

            propOccupied.Add(slot.cell);
            placedHere.Add(slot.cell);
            count--;
        }
    }

    private int PickWeightedSlot(List<TorchSlot> candidates, List<Vector2Int> placedHere, SeededRandom decor)
    {
        float total = 0f;
        float[] weights = new float[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2Int cell = candidates[i].cell;
            float weight = 1f;

            if (WallNeighbours(cell) >= 2)
                weight *= 3f;

            foreach (Vector2Int other in placedHere)
            {
                if (Mathf.Abs(other.x - cell.x) <= 1 && Mathf.Abs(other.y - cell.y) <= 1)
                {
                    weight *= 4f;
                    break;
                }
            }

            weights[i] = weight;
            total += weight;
        }

        float target = decor.Value * total;

        for (int i = 0; i < weights.Length; i++)
        {
            target -= weights[i];

            if (target < 0f)
                return i;
        }

        return weights.Length - 1;
    }

    private int WallNeighbours(Vector2Int cell)
    {
        int count = 0;

        for (int i = 0; i < Neighbours.Length; i++)
        {
            if (IsWallOrOutside(cell.x + Neighbours[i].x, cell.y + Neighbours[i].y))
                count++;
        }

        return count;
    }

    // Satu tempat per cell tepi room yang menempel dinding (cell sudut memakai sisi pertama yang ditemukan).
    private List<TorchSlot> RoomEdgeSlots(DungeonRoom room)
    {
        List<TorchSlot> slots = RoomTorchSlots(room);
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        List<TorchSlot> unique = new List<TorchSlot>();

        foreach (TorchSlot slot in slots)
        {
            if (seen.Add(slot.cell))
                unique.Add(slot);
        }

        return unique;
    }

    // Aula: dua baris column sejajar sisi panjang, 2 cell dari dinding, satu column tiap Column Spacing cell.
    private void PlaceColumns(Transform parent, DungeonRoom room, SeededRandom decor)
    {
        if (!HasProps(columnProps) || Mathf.Min(room.width, room.height) < hallMinSize)
            return;

        bool alongX = room.width >= room.height;
        int length = alongX ? room.width : room.height;
        int depth = alongX ? room.height : room.width;
        int[] rows = { 2, depth - 3 };

        for (int r = 0; r < rows.Length; r++)
        {
            for (int along = 2; along <= length - 3; along += columnSpacing)
            {
                Vector2Int cell = alongX
                    ? new Vector2Int(room.x + along, room.y + rows[r])
                    : new Vector2Int(room.x + rows[r], room.y + along);

                if (!TryBlockCell(cell))
                    continue;

                DecorProp prop = PickProp(columnProps, decor);
                SpawnDecor(parent, prop, CellToWorld(cell), Quaternion.Euler(0f, 90f * decor.Range(0, 4), 0f), true, decor);
                propOccupied.Add(cell);
            }
        }
    }

    // Batu dan tanah di lantai room, lebih sering di dekat dinding. Props Blocking ditaruh di tengah cell dan harus lolos
    // TryBlockCell (bukan cell bukaan, Start, Goal, kunci, chest, dan tidak memutus jalur BFS).
    // Props visual boleh di mana saja dan posisinya sedikit digeser dari tengah cell.
    private void PlaceRubble(Transform parent, DungeonRoom room, float factor, SeededRandom decor)
    {
        if (!HasProps(rubbleProps))
            return;

        int count = Mathf.RoundToInt(room.Area * rubbleDensity * factor);

        for (int attempt = 0; attempt < count * 4 && count > 0; attempt++)
        {
            Vector2Int cell = new Vector2Int(decor.Range(room.x, room.x + room.width), decor.Range(room.y, room.y + room.height));
            DecorProp prop = PickProp(rubbleProps, decor);
            float centerRoll = decor.Value;

            // Cell yang tidak menempel dinding lebih jarang dipilih, apalagi untuk penghalang, supaya tengah room tetap lapang.
            if (WallNeighbours(cell) == 0 && centerRoll > (prop.blocking ? 0.25f : 0.5f))
                continue;

            if (propOccupied.Contains(cell) || cell == GoalCell || cell == StartCell || cell == vaultKeyCell)
                continue;

            if (prop.blocking)
            {
                if (!TryBlockCell(cell))
                    continue;

                SpawnDecor(parent, prop, CellToWorld(cell), Quaternion.Euler(0f, decor.Range(0f, 360f), 0f), true, decor);
            }
            else
            {
                Vector3 jitter = new Vector3(decor.Range(-0.3f, 0.3f), 0f, decor.Range(-0.3f, 0.3f)) * tileSize;
                SpawnDecor(parent, prop, CellToWorld(cell) + jitter, Quaternion.Euler(0f, decor.Range(0f, 360f), 0f), false, decor);
            }

            propOccupied.Add(cell);
            count--;
        }
    }

    // Vault: koin dan pot di cincin sekitar chest. Props Blocking tidak pernah menempel chest (cincin dalam dicadangkan),
    // dan BFS memastikan cell di samping chest tetap bisa didatangi.
    private void PlaceTreasure(Transform parent, DungeonRoom room, SeededRandom decor)
    {
        if (!HasProps(treasureProps))
            return;

        Vector2Int center = room.Center;
        List<Vector2Int> ring = new List<Vector2Int>();

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                Vector2Int cell = new Vector2Int(center.x + dx, center.y + dy);

                if ((dx != 0 || dy != 0) && room.Contains(cell) && IsWalkable(grid[cell.x, cell.y]))
                    ring.Add(cell);
            }
        }

        int count = Mathf.Min(ring.Count, decor.Range(4, 8));

        for (int i = 0; i < count; i++)
        {
            int index = decor.Range(0, ring.Count);
            Vector2Int cell = ring[index];
            ring.RemoveAt(index);

            DecorProp prop = PickProp(treasureProps, decor);
            Quaternion yaw = Quaternion.Euler(0f, decor.Range(0f, 360f), 0f);

            if (prop.blocking)
            {
                if (!TryBlockCell(cell))
                    continue;

                SpawnDecor(parent, prop, CellToWorld(cell), yaw, true, decor);
            }
            else
            {
                Vector3 jitter = new Vector3(decor.Range(-0.25f, 0.25f), 0f, decor.Range(-0.25f, 0.25f)) * tileSize;
                SpawnDecor(parent, prop, CellToWorld(cell) + jitter, yaw, false, decor);
            }

            propOccupied.Add(cell);
        }
    }

    // Banner atau perisai di dinding room, disebar seperti obor dan tidak di cell yang sudah dipakai obor.
    private void PlaceWallProps(Transform parent, DungeonRoom room, int count, SeededRandom decor)
    {
        if (count <= 0 || !HasProps(wallProps))
            return;

        List<TorchSlot> slots = RoomTorchSlots(room);
        slots.RemoveAll(slot => torchCells.Contains(slot.cell) || propBlocked.Contains(slot.cell));

        if (slots.Count == 0)
            return;

        foreach (TorchSlot slot in SpreadTorchSlots(slots, count, decor))
        {
            Vector3 toWall = new Vector3(slot.toWall.x, 0f, slot.toWall.y);
            Vector3 position = CellToWorld(slot.cell) + toWall * (tileSize * 0.5f - torchWallOffset);
            DecorProp prop = PickProp(wallProps, decor);
            SpawnDecor(parent, prop, position, Quaternion.LookRotation(-toWall), false, decor);
        }
    }

    // Penyangga kayu di sisi dinding koridor, seperti terowongan tambang.
    private void PlaceCorridorProps(Transform parent, SeededRandom decor)
    {
        if (corridorPropSpacing <= 0 || !HasProps(corridorProps))
            return;

        List<TorchSlot> slots = CorridorTorchSlots();

        for (int i = slots.Count - 1; i > 0; i--)
        {
            int j = decor.Range(0, i + 1);
            TorchSlot swap = slots[i];
            slots[i] = slots[j];
            slots[j] = swap;
        }

        List<Vector2Int> placed = new List<Vector2Int>();

        foreach (TorchSlot slot in slots)
        {
            if (torchCells.Contains(slot.cell) || !FarFromAll(slot.cell, placed, corridorPropSpacing))
                continue;

            Vector3 toWall = new Vector3(slot.toWall.x, 0f, slot.toWall.y);
            Vector3 position = CellToWorld(slot.cell) + toWall * (tileSize * 0.5f - torchWallOffset);
            SpawnDecor(parent, PickProp(corridorProps, decor), position, Quaternion.LookRotation(-toWall), false, decor);
            placed.Add(slot.cell);
        }
    }

    private static Quaternion FacingAwayFrom(Vector3 toWall, DecorProp prop, SeededRandom decor)
    {
        if (prop != null && prop.randomYaw)
            return Quaternion.Euler(0f, decor.Range(0f, 360f), 0f);

        return Quaternion.LookRotation(-toWall) * Quaternion.Euler(0f, decor.Range(-20f, 20f), 0f);
    }

    private static bool HasProps(DecorProp[] props)
    {
        if (props == null)
            return false;

        foreach (DecorProp prop in props)
        {
            if (prop != null && prop.prefab != null)
                return true;
        }

        return false;
    }

    // Undian berbobot. Semua bobot 0 = semua props berpeluang sama.
    private static DecorProp PickProp(DecorProp[] props, SeededRandom decor)
    {
        float total = 0f;
        int usable = 0;

        foreach (DecorProp prop in props)
        {
            if (prop == null || prop.prefab == null)
                continue;

            total += Mathf.Max(0f, prop.weight);
            usable++;
        }

        bool equal = total <= 0f;
        float target = decor.Value * (equal ? usable : total);
        DecorProp last = null;

        foreach (DecorProp prop in props)
        {
            if (prop == null || prop.prefab == null)
                continue;

            last = prop;
            target -= equal ? 1f : Mathf.Max(0f, prop.weight);

            if (target < 0f)
                return prop;
        }

        return last;
    }

    private void SpawnDecor(Transform parent, DecorProp prop, Vector3 position, Quaternion facing, bool blocking, SeededRandom decor)
    {
        if (prop == null || prop.prefab == null)
            return;

        GameObject instance = Instantiate(prop.prefab, parent);
        instance.transform.SetPositionAndRotation(position + facing * prop.offset, facing * Quaternion.Euler(prop.rotation));

        // Ukuran sedikit beragam supaya props yang sama tidak terlihat dicetak.
        float jitter = blocking ? 1f : decor.Range(0.85f, 1.15f);
        instance.transform.localScale = prop.prefab.transform.localScale * prop.scale * jitter;

        StripPhysics(instance);

        if (blocking)
        {
            SetLayerRecursive(instance, LayerMask.NameToLayer(GameLayers.NavBlock));
            AddBoundsCollider(instance);

            BoxCollider box = instance.GetComponent<BoxCollider>();

            if (box != null)
            {
                NavMeshObstacle obstacle = instance.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = box.center;
                obstacle.size = box.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            }
        }
        else
        {
            int decorLayer = LayerMask.NameToLayer(GameLayers.Decor);
            SetLayerRecursive(instance, decorLayer >= 0 ? decorLayer : 0);
        }

        PropCount++;
        builtObjects++;
    }

    private void ClearDecoration()
    {
        roomThemes.Clear();
        propBlocked.Clear();
        propOccupied.Clear();
        propReserved.Clear();
        torchCells.Clear();
        baselineDistance = null;
        PropCount = 0;
        RejectedPropCount = 0;
    }

    private static Color ThemeColor(RoomTheme theme)
    {
        switch (theme)
        {
            case RoomTheme.Storage: return new Color(0.75f, 0.5f, 0.25f);
            case RoomTheme.Ruins: return new Color(0.6f, 0.6f, 0.6f);
            case RoomTheme.Hall: return new Color(0.6f, 0.45f, 0.9f);
            default: return Color.clear;
        }
    }

    private void DrawDecorGizmos()
    {
        for (int i = 0; i < rooms.Count && i < roomThemes.Count; i++)
        {
            Color color = ThemeColor(roomThemes[i]);

            if (color.a <= 0f)
                continue;

            Gizmos.color = color;
            Gizmos.DrawCube(CellToWorld(rooms[i].Center) + Vector3.up * 0.6f, Vector3.one * tileSize * 0.5f);
        }

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 1f);

        foreach (Vector2Int cell in propBlocked)
            Gizmos.DrawWireCube(CellToWorld(cell) + Vector3.up * 0.4f, new Vector3(tileSize * 0.8f, 0.8f, tileSize * 0.8f));
    }
}
