using System;
using System.Collections.Generic;
using UnityEngine;

// Angka Hold Zone, diatur di Inspector ObjectiveRunner.
[Serializable]
public class HoldZoneSettings
{
    [Header("Zona")]
    [Min(1f)] public float captureSeconds = 40f;
    // Penurunan progres per detik (0.02 = 2%/s) saat MC di luar zona dan zona tidak contested.
    [Min(0f)] public float decayPerSecond = 0.02f;
    // Radius = setengah sisi room terpendek x tile size, dikurangi nilai ini (meter).
    [Min(0f)] public float edgeMargin = 1f;

    [Header("Pemilihan Room (PCG)")]
    // Room zona: bukan Start, Goal (Boss Room), atau vault, dengan syarat di bawah.
    [Min(1)] public int minRoomArea = 30;
    [Min(1)] public int minEntrances = 2;
    [Min(0)] public int minHopFromStart = 2;
    // Skor room = pintu x Entrance Weight + (luas / Min Room Area) x Area Weight + acak [0, Random Jitter).
    [Min(0f)] public float entranceWeight = 1f;
    [Min(0f)] public float areaWeight = 0.5f;
    [Min(0f)] public float randomJitter = 0.5f;

    [Header("Musuh")]
    // Bantuan datang dari room tetangga zona tiap interval ini.
    [Min(1f)] public float reinforceInterval = 12f;
    [Min(0.1f)] public float retryDelay = 1f;
    // Batas musuh hidup = round(nilai ini x faktor DDA), sama seperti Survival.
    [Min(1f)] public float baseMaxAlive = 5f;
    // Bantuan muncul minimal sejauh ini dari MC dan tidak terlihat dari MC.
    [Min(0f)] public float reinforceMinDistance = 8f;
    [Min(1)] public int placementAttempts = 12;
}

// Objective Hold Zone: kuasai zona di satu room sampai progres 100%.
//   Room zona  dipilih PCG (stream Objective): bukan Start, Goal, atau vault, luas minimal, pintu minimal 2,
//              hop dari Start minimal 2. Skor tertinggi dari jumlah pintu dan luas, ditambah sedikit acak.
//   Progres    lihat HoldZone: naik saat MC di zona tanpa musuh, berhenti saat contested, turun saat ditinggal.
//   Musuh      komposisi wave dimunculkan di room tetangga zona begitu pertempuran mulai. Bantuan datang tiap
//              Reinforce Interval dari room tetangga zona (tidak terlihat MC), dengan batas musuh hidup.
//   AI         titik zona dipasang di GroupBlackboard, jadi musuh mendatangi zona walau tidak melihat MC.
// Selesai saat progres penuh. Musuh sisa di-despawn oleh ObjectiveRunner.
public class HoldZoneObjective : IWaveObjective
{
    private readonly HoldZoneSettings settings;
    private readonly List<EnemyRole> roles = new List<EnemyRole>();
    private readonly List<int> roleWeights = new List<int>();
    private readonly List<int> spawnRooms = new List<int>();

    private ObjectiveContext context;
    private HoldZone zone;
    private int zoneRoom = -1;
    private float elapsed;
    private float nextReinforce;
    private float factor = 1f;
    private bool initialPending;

    public HoldZoneObjective(HoldZoneSettings settings)
    {
        this.settings = settings ?? new HoldZoneSettings();
    }

    public ObjectiveType Type => ObjectiveType.HoldZone;

    public bool IsComplete => zone != null && zone.Progress01 >= 1f;

    public float Progress01 => zone != null ? zone.Progress01 : 0f;

    public string HudText =>
        "Zona " + Mathf.FloorToInt(Progress01 * 100f) + "%" +
        (Contested ? "  <color=" + UIKit.TradeOffHex + "><b>Direbut musuh!</b></color>" : string.Empty);

    public bool Contested => zone != null && zone.State == ZoneState.Contested;

    public HoldZone Zone => zone;

    public int ZoneRoom => zoneRoom;

    public float CaptureSeconds => settings.captureSeconds;

    public int MaxAlive => Mathf.Max(1, Mathf.RoundToInt(settings.baseMaxAlive * factor));

    public int Reinforced { get; private set; }

    public int InitialSpawned { get; private set; }

    // Satu baris untuk panel debug.
    public string DebugStatus
    {
        get
        {
            if (zone == null)
                return "zona belum dibuat";

            DungeonRoomGraph graph = context != null && context.Dungeon != null ? context.Dungeon.RoomGraph : null;
            string room = graph != null && zoneRoom >= 0
                ? "room " + zoneRoom + " (" + graph.EntranceCount(zoneRoom) + " pintu, " + graph.Room(zoneRoom).Area + " cell)"
                : "-";

            return room + ", " + zone.State + ", " + Mathf.FloorToInt(Progress01 * 100f) + "%, musuh di zona " + zone.EnemiesInside +
                   ", hidup " + Alive + "/" + MaxAlive + ", bantuan " + Reinforced + " (berikutnya " + Mathf.Max(0f, nextReinforce - elapsed).ToString("0") + " s)";
        }
    }

    private int Alive => context != null && context.Battle != null ? context.Battle.AliveCount : 0;

    // ---------- Pemilihan room ----------

    // Dipakai RunManager: Hold Zone hanya ditawarkan jika dungeon punya room yang memenuhi syarat.
    public static bool CanHost(DungeonGenerator dungeon)
    {
        DungeonRoomGraph graph = dungeon != null ? dungeon.RoomGraph : null;
        HoldZoneSettings rules = ObjectiveRunner.Current != null ? ObjectiveRunner.Current.HoldZone : new HoldZoneSettings();
        return graph != null && ChooseRoom(graph, rules, null) >= 0;
    }

    // Room zona dengan skor tertinggi. random null = tanpa acak (cek ketersediaan saja).
    public static int ChooseRoom(DungeonRoomGraph graph, HoldZoneSettings rules, SeededRandom random)
    {
        DungeonGenerator dungeon = graph.Dungeon;
        int start = IndexOf(graph, dungeon.StartRoom);
        int best = -1;
        float bestScore = float.NegativeInfinity;

        for (int r = 0; r < graph.RoomCount; r++)
        {
            DungeonRoom room = graph.Room(r);

            if (room == dungeon.StartRoom || room == dungeon.GoalRoom || room == dungeon.VaultRoom)
                continue;

            if (room.Area < rules.minRoomArea || graph.EntranceCount(r) < rules.minEntrances)
                continue;

            int hop = start >= 0 ? graph.HopDistance(start, r) : rules.minHopFromStart;

            if (hop < rules.minHopFromStart)
                continue;

            float score = graph.EntranceCount(r) * rules.entranceWeight +
                          (float)room.Area / rules.minRoomArea * rules.areaWeight +
                          (random != null ? random.Range(0f, rules.randomJitter) : 0f);

            if (score > bestScore)
            {
                bestScore = score;
                best = r;
            }
        }

        return best;
    }

    private static int IndexOf(DungeonRoomGraph graph, DungeonRoom room)
    {
        for (int r = 0; r < graph.RoomCount; r++)
        {
            if (graph.Room(r) == room)
                return r;
        }

        return -1;
    }

    // ---------- Siklus ----------

    public void Begin(ObjectiveContext objectiveContext)
    {
        context = objectiveContext;
        elapsed = 0f;
        nextReinforce = settings.reinforceInterval;
        factor = RoomSpawner.DdaFactor();
        Reinforced = 0;
        InitialSpawned = 0;
        initialPending = true;

        RoomSpawner.ReadRoles(context.Wave, roles, roleWeights);

        DungeonRoomGraph graph = context.Dungeon != null ? context.Dungeon.RoomGraph : null;

        if (graph == null)
        {
            Debug.LogWarning("HoldZoneObjective: tidak ada graf ruangan, zona tidak bisa dibuat.");
            return;
        }

        zoneRoom = ChooseRoom(graph, settings, context.Random);

        if (zoneRoom < 0)
        {
            Debug.LogWarning("HoldZoneObjective: tidak ada room yang memenuhi syarat zona.");
            return;
        }

        CreateZone(graph);

        Debug.Log("HoldZoneObjective: zona di room " + zoneRoom + " (" + graph.EntranceCount(zoneRoom) + " pintu, luas " +
                  graph.Room(zoneRoom).Area + " cell, hop dari Start " +
                  graph.HopDistance(IndexOf(graph, context.Dungeon.StartRoom), zoneRoom) + "), radius " + zone.Radius.ToString("0.0") + " m");
    }

    private void CreateZone(DungeonRoomGraph graph)
    {
        DungeonGenerator dungeon = context.Dungeon;
        DungeonRoom room = graph.Room(zoneRoom);

        // Tengah geometris room (bukan cell tengah), supaya lingkaran pas di room bersisi genap.
        Vector3 center = dungeon.transform.position +
                         new Vector3((room.x + room.width * 0.5f) * dungeon.TileSize, 0f, (room.y + room.height * 0.5f) * dungeon.TileSize);
        float radius = Mathf.Min(room.width, room.height) * 0.5f * dungeon.TileSize - settings.edgeMargin;

        GameObject zoneObject = new GameObject("HoldZone");
        zoneObject.transform.SetParent(dungeon.LevelRoot, false);
        zoneObject.transform.position = center;

        zone = zoneObject.AddComponent<HoldZone>();
        zone.Setup(radius, settings.captureSeconds, settings.decayPerSecond);

        if (TacticalCoordinator.Current != null)
            TacticalCoordinator.Current.Group.SetObjective(zone.transform, zone.Radius, graph.EntrancePoints(zoneRoom));
    }

    public void Tick(float deltaTime)
    {
        if (context == null || zone == null)
            return;

        elapsed += deltaTime;

        // Musuh baru bisa didaftarkan setelah BattleManager mulai, jadi komposisi awal dimunculkan di tick pertama.
        if (initialPending && context.Battle != null && context.Battle.IsBattleActive)
        {
            initialPending = false;
            SpawnInitial();
        }

        zone.Tick(deltaTime, PlayerHealth.Current, context.Battle);

        if (elapsed >= nextReinforce)
        {
            if (Alive < MaxAlive && SpawnReinforcement())
                nextReinforce = elapsed + settings.reinforceInterval;
            else
                nextReinforce = elapsed + settings.retryDelay;
        }
    }

    public void End()
    {
        if (TacticalCoordinator.Current != null)
            TacticalCoordinator.Current.Group.ClearObjective();

        if (zone != null)
            UnityEngine.Object.Destroy(zone.gameObject);

        zone = null;
        context = null;
    }

    // ---------- Spawn ----------

    // Komposisi wave dibagi ke room tetangga zona secara acak ber-seed. Room tetangga penuh = room 2 hop dari zona.
    private void SpawnInitial()
    {
        DungeonRoomGraph graph = context.Dungeon != null ? context.Dungeon.RoomGraph : null;
        PlayerHealth player = PlayerHealth.Current;

        if (graph == null || player == null || context.Wave == null)
            return;

        Vector3 playerPosition = player.transform.position;
        List<Vector3> taken = RoomSpawner.CollectOccupied(context.Battle);
        PcgSpawnRules rules = context.Spawner.ActiveRules;

        foreach (WaveEntry entry in context.Wave.Entries)
        {
            for (int i = 0; i < entry.count; i++)
            {
                bool placed = false;

                for (int ring = 1; ring <= 2 && !placed; ring++)
                {
                    CollectRoomsAround(graph, ring, -1);
                    Shuffle(spawnRooms);

                    foreach (int room in spawnRooms)
                    {
                        if (!RoomSpawner.TryPlaceInRoom(context, graph.Room(room), playerPosition, rules.minDistanceFromPlayer, false,
                                settings.placementAttempts, taken, out Vector3 position))
                            continue;

                        if (RoomSpawner.Spawn(context, entry.role, position, false, taken) != null)
                            InitialSpawned++;

                        placed = true;
                        break;
                    }
                }

                if (!placed)
                    Debug.LogWarning("HoldZoneObjective: " + entry.role + " dilewati, room di sekitar zona penuh.");
            }
        }
    }

    // Bantuan satu musuh dari room tetangga zona yang tidak terlihat MC. Tidak ada = room 2 hop dari zona.
    private bool SpawnReinforcement()
    {
        DungeonRoomGraph graph = context.Dungeon != null ? context.Dungeon.RoomGraph : null;
        PlayerHealth player = PlayerHealth.Current;

        if (graph == null || player == null)
            return false;

        Vector3 playerPosition = player.transform.position;
        int playerRoom = RoomSpawner.NearestRoom(graph, playerPosition);
        Vector3 eye = playerPosition + Vector3.up * 1.2f;
        List<Vector3> taken = RoomSpawner.CollectOccupied(context.Battle);
        EnemyRole role = RoomSpawner.PickRole(context.Random, roles, roleWeights);

        for (int ring = 1; ring <= 2; ring++)
        {
            CollectRoomsAround(graph, ring, playerRoom);
            Shuffle(spawnRooms);

            foreach (int room in spawnRooms)
            {
                if (!RoomSpawner.Hidden(eye, graph.Center(room)))
                    continue;

                if (!RoomSpawner.TryPlaceInRoom(context, graph.Room(room), playerPosition, settings.reinforceMinDistance, true,
                        settings.placementAttempts, taken, out Vector3 position))
                    continue;

                if (RoomSpawner.Spawn(context, role, position, false, taken) == null)
                    return false;

                Reinforced++;
                return true;
            }
        }

        return false;
    }

    // Room dengan hop tepat sebanyak ring dari room zona, kecuali vault terkunci dan room yang dikecualikan.
    private void CollectRoomsAround(DungeonRoomGraph graph, int ring, int exclude)
    {
        spawnRooms.Clear();

        for (int r = 0; r < graph.RoomCount; r++)
        {
            if (r == exclude || graph.HopDistance(zoneRoom, r) != ring || RoomSpawner.IsLockedVault(graph, r))
                continue;

            spawnRooms.Add(r);
        }
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = context.Random.Range(0, i + 1);
            int swap = list[i];
            list[i] = list[j];
            list[j] = swap;
        }
    }
}
