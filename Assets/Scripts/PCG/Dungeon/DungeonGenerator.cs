using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// Procedural dungeon dengan Binary Space Partitioning (Praktikum 10).
// Pipeline sama dengan modul:
//   1. InitializeGrid      seluruh grid diisi Wall (wall-first)
//   2. CreateBSP           area dibagi dua berulang kali sampai setiap Leaf cukup kecil
//   3. CreateRooms         satu room acak di dalam setiap Leaf, diukir menjadi Floor
//   4. CreateCorridors     room ke-i dihubungkan ke room ke-(i+1) dengan corridor berbentuk L
//   5. PlaceStartAndGoal   Start di room pertama, BFS dari Start, Goal di room dengan jarak jalan BFS terjauh
//   6. Validate            hard constraint: jumlah room cukup, Start-Goal punya jalur, semua room terjangkau (BFS Validation).
//                          Gagal = dungeon dibuat ulang dengan seed turunan (generate-and-test), maksimal Max Generation Attempts
//   7. BuildDungeon        grid diubah menjadi GameObject: Floor (layer Ground), Wall (layer NavBlock), portal Goal,
//                          titik Boss Room (Guardian hanya muncul di room Goal), dan Vault Room
// Vault Room (Treasure Room): room dead-end (tepat satu bukaan, bukan Start atau Goal) ditutup pintu selebar bukaannya.
// Kunci ditaruh di room lain dengan peluang sebanding jarak BFS dari Start, chest di tengah vault memberi gate gratis.
// Hard constraint vault: dengan pintu tertutup, Goal, semua room lain, dan kunci tetap terjangkau dari Start.
// Soft constraint: dungeon valid tanpa dead-end dicoba ulang (Retry For Vault); jika tetap tidak ada, ronde itu tanpa vault.
// Perbedaan dengan modul:
//   - Angka acak dari SeededRandom, bukan Random.InitState, supaya tidak terganggu AI dan sistem lain.
//   - Satu cell = Tile Size meter (bawaan 2 m), karena AI musuh butuh ruang untuk lunge dan menjaga jarak.
//   - Corridor bisa lebih dari satu cell (Corridor Width).
//   - Tile berurutan dalam satu baris digabung menjadi satu blok (Merge Tiles), jadi jumlah GameObject dan collider jauh lebih sedikit.
// Dungeon dibangun mulai dari posisi objek ini ke arah +X dan +Z. Taruh objek ini jauh dari Safezone dan arena.
// Mode Dungeon: objek ini juga punya komponen Arena dan PcgSpawnArea (MathGate > PCG > 4), jadi spawner musuh dan item
// memperlakukan dungeon seperti arena biasa. Setiap dungeon baru, titik masuk MC dipindah ke Start, area spawn disesuaikan
// dengan ukuran grid, dan NavMesh di-bake ulang (pengembangan Runtime NavMesh di modul).
// Preview di Editor: menu titik tiga komponen > Generate Preview (tanpa Play, hasilnya tidak ikut tersimpan di scene).
[DisallowMultipleComponent]
public partial class DungeonGenerator : MonoBehaviour
{
    private const string LevelRootName = "GeneratedLevel";

    [Header("Dungeon Size (cell)")]
    [SerializeField, Min(10)] private int mapWidth = 36;
    [SerializeField, Min(10)] private int mapHeight = 28;
    // Ukuran satu cell di dunia, dalam meter.
    [SerializeField, Min(0.5f)] private float tileSize = 2f;

    [Header("BSP Settings (cell)")]
    // Area lebih kecil dari dua kali nilai ini tidak dibagi lagi.
    [SerializeField, Min(4)] private int minLeafSize = 9;
    // Area lebih besar dari nilai ini akan dibagi lagi.
    [SerializeField, Min(4)] private int maxLeafSize = 14;

    [Header("Room Settings (cell)")]
    [SerializeField, Min(3)] private int minRoomSize = 5;
    // Jarak room dari tepi Leaf, supaya room tetangga tidak menempel.
    [SerializeField, Min(0)] private int roomMargin = 1;

    [Header("Corridor (cell)")]
    [SerializeField, Range(1, 4)] private int corridorWidth = 2;

    [Header("Validation")]
    // Hard constraint jumlah room. Dungeon dengan room lebih sedikit dianggap tidak valid.
    [SerializeField, Min(2)] private int minRoomCount = 3;
    // Batas generate-and-test. Jika semua percobaan tidak valid, percobaan terakhir tetap dibangun dan diberi peringatan.
    [SerializeField, Min(1)] private int maxGenerationAttempts = 10;

    [Header("Rooms Khusus")]
    // Start room adalah safe room (modul: safeRoomCount): musuh dan item tidak ditempatkan di sana.
    [SerializeField] private bool safeStartRoom = true;
    // Boss Room: role ini hanya muncul di room Goal, menjaga portal keluar.
    [SerializeField] private bool bossRoom = true;
    [SerializeField] private EnemyRole bossRole = EnemyRole.Guardian;
    [SerializeField, Range(1, 6)] private int bossSpawnPoints = 3;

    [Header("Vault Room (Treasure Room)")]
    [SerializeField] private bool vaultRoom = true;
    // Peluang vault muncul per dungeon (per ronde), diacak dari seed.
    [SerializeField, Range(0f, 1f)] private float vaultChance = 1f;
    // Soft constraint: dungeon valid tanpa room dead-end dicoba lagi dengan seed percobaan berikutnya (dalam batas Max Generation Attempts).
    [SerializeField] private bool retryForVault = true;
    [SerializeField] private GateRarity vaultMinRarity = GateRarity.VeryRare;
    // Kosong = bentuk bawaan. Prefab kunci diputar dan naik-turun oleh komponen kunci, jadi atur hadapnya lewat Rotation.
    [SerializeField] private GameObject keyPrefab;
    [SerializeField] private Vector3 keyPrefabRotation = Vector3.zero;
    [SerializeField, Min(0.01f)] private float keyPrefabScale = 1f;
    // Chest menghadap pintu (sumbu Z lokal). Offset dan Rotation relatif terhadap arah hadap itu.
    [SerializeField] private GameObject chestPrefab;
    [SerializeField] private Vector3 chestPrefabOffset = Vector3.zero;
    [SerializeField] private Vector3 chestPrefabRotation = Vector3.zero;
    // Satu prefab pintu (mis. wall-gate) di tengah bukaan, sumbu X lokal sepanjang bukaan. Offset di dunia, seperti prefab tile.
    [SerializeField] private GameObject doorPrefab;
    [SerializeField] private Vector3 doorPrefabOffset = Vector3.zero;
    [SerializeField] private Vector3 doorPrefabRotation = Vector3.zero;
    // Pengali scale asli prefab untuk bukaan selebar Corridor Width. Bukaan yang lebih lebar ikut melebarkan X sebanding.
    [SerializeField] private Vector3 doorPrefabScale = new Vector3(2f, 1.3f, 1f);
    // Child pintu yang turun ke lantai lalu disembunyikan saat vault terbuka (awalan nama), mis. "gate" di wall-gate.
    // Tembok di sekitarnya tetap. Tidak ada yang cocok = seluruh pintu yang turun.
    [SerializeField] private string doorOpenPartPrefix = "gate";
    // Tinggi collider pintu. Dibuat cukup tinggi untuk menahan MC walau dinding dungeon rendah.
    [SerializeField, Min(0.5f)] private float doorBlockHeight = 2f;

    [Header("Build")]
    // Kosong = blok kubus bawaan dengan material di bawah. Prefab diisi = satu prefab per cell (tidak digabung).
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject goalPrefab;
    // Geseran prefab dari titik tengah cell di permukaan lantai. Prefab tileset punya pivot masing-masing,
    // jadi posisinya diatur di sini, mis. Y = -0,05 supaya dinding menempel lantai.
    [SerializeField] private Vector3 floorPrefabOffset = Vector3.zero;
    [SerializeField] private Vector3 wallPrefabOffset = Vector3.zero;
    [SerializeField] private Vector3 goalPrefabOffset = Vector3.zero;

    [Header("Floor Variation")]
    // Prefab lantai alternatif yang diselipkan di antara Floor Prefab polos. Weight = peluang relatif antar varian,
    // Offset = tambahan dari Floor Prefab Offset untuk prefab dengan pivot berbeda.
    [SerializeField] private FloorVariant[] floorVariants;
    // Rata-rata bagian lantai yang memakai varian.
    [SerializeField, Range(0f, 1f)] private float floorVariantChance = 0.3f;
    // 0 = tiap tile diacak sendiri. Makin besar, varian makin berkelompok menjadi area usang (Perlin noise dari seed).
    [SerializeField, Range(0f, 1f)] private float floorClusterStrength = 0.6f;
    // Ukuran kira-kira satu kelompok, dalam cell.
    [SerializeField, Min(1f)] private float floorClusterSize = 6f;
    // Tambahan peluang varian di koridor dan di cell room yang menempel dinding, bagian lantai yang paling aus.
    [SerializeField, Range(0f, 1f)] private float corridorVariantBonus = 0.15f;
    [SerializeField, Range(0f, 1f)] private float wallEdgeVariantBonus = 0.1f;
    // Putar tiap tile lantai (polos dan varian) 0/90/180/270 derajat supaya pola tekstur tidak terlihat berulang.
    [SerializeField] private bool randomFloorRotation = true;

    [Header("Torches")]
    // Suasana gelapnya diatur DungeonAtmosphere. Obor menempel di dinding room dan koridor, posisinya dari seed.
    [SerializeField] private bool torches = true;
    // Kosong = obor bawaan (gagang gelap + api bercahaya). Prefab menghadap ke dalam room (sumbu Z lokal), pivot di permukaan dinding.
    [SerializeField] private GameObject torchPrefab;
    [SerializeField] private Vector3 torchPrefabOffset = Vector3.zero;
    [SerializeField] private Vector3 torchPrefabRotation = Vector3.zero;
    // Geseran obor dari tepi cell ke arah dalam room (meter), untuk dinding yang lebih tipis dari satu cell.
    [SerializeField] private float torchWallOffset = 0.05f;
    [SerializeField] private float torchHeight = 0.9f;
    // Satu obor per sekian cell keliling room, minimal satu per room. Obor disebar sejauh mungkin satu sama lain.
    [SerializeField, Min(2)] private int roomTorchSpacing = 10;
    [SerializeField, Range(0, 4)] private int goalRoomExtraTorches = 2;
    // Jarak minimal antar obor di koridor (cell, Manhattan), juga terhadap obor room. 0 = koridor tanpa obor.
    [SerializeField, Min(0)] private int corridorTorchSpacing = 9;
    [SerializeField] private Color torchColor = new Color(1f, 0.56f, 0.22f);
    [SerializeField, Min(0f)] private float torchIntensity = 2.2f;
    [SerializeField, Min(0.5f)] private float torchRange = 5f;
    [SerializeField, Range(0f, 1f)] private float torchFlicker = 0.3f;
    [SerializeField, Min(0.1f)] private float torchFlickerSpeed = 6f;

    [Header("Default Blocks (jika prefab kosong)")]
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    // Dinding rendah supaya MC tetap terlihat dari kamera top-down.
    [SerializeField, Min(0.5f)] private float wallHeight = 1.8f;
    [SerializeField, Min(0.05f)] private float floorThickness = 0.2f;
    [SerializeField] private bool mergeTiles = true;

    [Header("NavMesh")]
    // Bake NavMesh dari lantai (layer Ground) setelah dungeon dibangun. Dinding tidak ikut, jadi atasnya tidak bisa dipijak,
    // dan tepi lantai otomatis dikikis sejauh radius agent.
    [SerializeField] private bool bakeNavMesh = true;
    // 0 = Humanoid, sama dengan NavMesh arena.
    [SerializeField] private int agentTypeId = 0;

    [Header("Mode Dungeon")]
    // Titik masuk MC. Kosong = child "PlayerEntry" dibuat otomatis.
    [SerializeField] private Transform playerEntry;

    [Header("Eksperimen (Editor, menu titik tiga)")]
    // Run Seed Experiment: satu baris tabel per seed.
    [SerializeField] private int[] experimentSeeds = { 101, 202, 303, 404, 505 };
    // Run BSP Parameter Experiment: setiap nilai diuji dengan semua Experiment Seeds, parameter lain tetap.
    [SerializeField] private int[] experimentLeafSizes = { 8, 12, 16 };
    [SerializeField] private int[] experimentRoomMargins = { 1, 2, 4 };
    [SerializeField] private Vector2Int[] experimentMapSizes = { new Vector2Int(40, 30), new Vector2Int(60, 40), new Vector2Int(80, 60) };

    [Header("Editor Preview")]
    [SerializeField] private int previewSeed = 12345;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawLeaves = true;

    [Header("Debug")]
    [SerializeField] private bool logGeneration = true;

    private static Material defaultFloorMaterial;
    private static Material defaultWallMaterial;
    private static Material defaultGoalMaterial;
    private static Material defaultDoorMaterial;
    private static Material defaultChestMaterial;
    private static Material defaultGoldMaterial;
    private static Material defaultFlameMaterial;
    private static Material defaultTorchHandleMaterial;

    private readonly List<BSPNode> leafNodes = new List<BSPNode>();
    private readonly List<DungeonRoom> rooms = new List<DungeonRoom>();

    private TileType[,] grid;
    private BSPNode rootNode;
    private Transform levelRoot;
    private SeededRandom random;
    private int builtObjects;
    private int[,] bfsDistance;
    private readonly List<Vector2Int> goalPath = new List<Vector2Int>();
    private readonly List<Vector3> bossPoints = new List<Vector3>();
    private readonly List<DungeonRoom> deadEndRooms = new List<DungeonRoom>();
    private readonly List<Vector2Int> vaultDoorCells = new List<Vector2Int>();
    private Vector2Int vaultKeyCell;
    private int vaultRewardSeed;
    private readonly List<Vector3> torchPositions = new List<Vector3>();

    // Satu tempat obor: cell lantai dan arah ke dinding yang ditempeli.
    private struct TorchSlot
    {
        public Vector2Int cell;
        public Vector2Int toWall;
    }

    // Per cell lantai: indeks Floor Variants (-1 = Floor Prefab polos) dan jumlah putaran 90 derajat.
    private int[,] floorChoice;
    private int[,] floorTurns;

    [System.Serializable]
    public class FloorVariant
    {
        public GameObject prefab;
        [Min(0f)] public float weight = 1f;
        public Vector3 offset = Vector3.zero;
    }

    public TileType[,] Grid => grid;

    public int MapWidth => mapWidth;

    public int MapHeight => mapHeight;

    public float TileSize => tileSize;

    public IReadOnlyList<DungeonRoom> Rooms => rooms;

    public IReadOnlyList<BSPNode> Leaves => leafNodes;

    public bool HasLayout => grid != null && rooms.Count > 0;

    public int LastSeed { get; private set; }

    public Vector2Int StartCell { get; private set; }

    public Vector2Int GoalCell { get; private set; }

    public DungeonRoom StartRoom { get; private set; }

    public DungeonRoom GoalRoom { get; private set; }

    public int FloorCellCount { get; private set; }

    // Cell lantai yang memakai prefab varian, bukan Floor Prefab polos.
    public int FloorVariantCells { get; private set; }

    public IReadOnlyList<Vector3> TorchPositions => torchPositions;

    // Hasil BFS Validation dungeon terakhir.
    public bool IsValid { get; private set; }

    public string ValidationMessage { get; private set; } = string.Empty;

    // Jumlah percobaan generate-and-test sampai dungeon valid (1 = langsung valid).
    public int GenerationAttempts { get; private set; }

    // Seed percobaan yang akhirnya dibangun (sama dengan LastSeed jika percobaan pertama valid).
    public int BuiltSeed { get; private set; }

    // Panjang jalur terpendek Start ke Goal dalam cell (BFS), -1 jika tidak terhubung.
    public int PathLength { get; private set; } = -1;

    public int ReachableRooms { get; private set; }

    public IReadOnlyList<Vector2Int> GoalPath => goalPath;

    public IReadOnlyList<Vector3> BossPoints => bossPoints;

    public DungeonGoalPortal GoalPortal { get; private set; }

    // Room selain Start dan Goal yang hanya punya satu bukaan.
    public IReadOnlyList<DungeonRoom> DeadEndRooms => deadEndRooms;

    // Lolos undian Vault Chance untuk dungeon ini.
    public bool VaultPlanned { get; private set; }

    public DungeonRoom VaultRoom { get; private set; }

    public IReadOnlyList<Vector2Int> VaultDoorCells => vaultDoorCells;

    public Vector2Int VaultKeyCell => vaultKeyCell;

    // Jarak jalan BFS Start ke kunci (pintu vault tertutup), -1 jika tanpa vault.
    public int VaultKeyDistance { get; private set; } = -1;

    public string VaultMessage { get; private set; } = string.Empty;

    public DungeonVault Vault { get; private set; }

    public GateRarity VaultMinRarity => vaultMinRarity;

    public DungeonMetrics Metrics { get; private set; }

    // Waktu bake NavMesh terakhir dalam milidetik.
    public float LastBakeMilliseconds { get; private set; }

    public Transform PlayerEntry => playerEntry;

    public Transform LevelRoot => levelRoot;

    public Vector3 StartWorld => CellToWorld(StartCell);

    public Vector3 GoalWorld => CellToWorld(GoalCell);

    // Pusat dan ukuran seluruh grid di dunia (XZ), dipakai area spawn PCG.
    public Vector3 WorldCenter => transform.position + new Vector3(mapWidth * tileSize * 0.5f, 0f, mapHeight * tileSize * 0.5f);

    public Vector2 WorldSize => new Vector2(mapWidth * tileSize, mapHeight * tileSize);

    // Dikirim setelah dungeon selesai dibangun.
    public event Action<DungeonGenerator> Generated;

    private void Awake()
    {
        // Preview Editor tidak tersimpan di scene, tapi bisa terbawa ke Play karena scene tidak di-reload.
        ClearDungeon();

        // Cadangan jika menu Setup Dungeon Mode belum dijalankan ulang. Nilainya default dan tidak tersimpan.
        if (Application.isPlaying && GetComponent<DungeonAtmosphere>() == null)
        {
            gameObject.AddComponent<DungeonAtmosphere>();
            Debug.Log("DungeonGenerator: DungeonAtmosphere ditambahkan otomatis. Jalankan MathGate > PCG > 4. Setup Dungeon Mode supaya nilainya bisa diatur dan tersimpan.", this);
        }
    }

    // ---------- Pipeline ----------

    public void Generate(int seed)
    {
        ClearDungeon();

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        LastSeed = seed;
        IsValid = false;
        GenerationAttempts = 0;

        VaultPlanned = vaultRoom && vaultChance > 0f &&
                       new SeededRandom(SeededRandom.Derive(seed, 0, PcgStream.Vault)).Value < vaultChance;

        bool hasFallback = false;
        int fallbackSeed = seed;

        // Generate-and-test: buat, validasi dengan BFS, ulangi dengan seed turunan jika tidak valid.
        for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
        {
            int attemptSeed = attempt == 0 ? seed : SeededRandom.Derive(seed, attempt, PcgStream.Dungeon);

            GenerationAttempts = attempt + 1;

            bool valid = RunAttempt(attemptSeed, out string message);
            ValidationMessage = message;

            if (valid && (!VaultPlanned || !retryForVault || VaultRoom != null))
            {
                IsValid = true;
                break;
            }

            if (valid)
            {
                if (!hasFallback)
                {
                    hasFallback = true;
                    fallbackSeed = attemptSeed;
                }

                if (logGeneration)
                    Debug.Log("DungeonGenerator: percobaan " + GenerationAttempts + " (seed " + attemptSeed + ") valid tanpa vault, " + VaultMessage + ". Dicoba lagi.", this);

                continue;
            }

            if (logGeneration)
                Debug.Log("DungeonGenerator: percobaan " + GenerationAttempts + " (seed " + attemptSeed + ") tidak valid, " + message + ". Dibuat ulang.", this);
        }

        // Soft constraint vault tidak terpenuhi di semua percobaan: dungeon valid pertama dipakai tanpa vault.
        if (!IsValid && hasFallback)
        {
            IsValid = RunAttempt(fallbackSeed, out string message);
            ValidationMessage = message;
        }

        if (rooms.Count == 0)
        {
            Debug.LogWarning("DungeonGenerator: seed " + seed + " tidak menghasilkan room. Perbesar map atau perkecil Min Room Size.", this);
            return;
        }

        if (!IsValid)
            Debug.LogWarning("DungeonGenerator: tidak ada dungeon valid dalam " + maxGenerationAttempts + " percobaan (" + ValidationMessage + "). Percobaan terakhir tetap dibangun.", this);

        float generateMs = (float)watch.Elapsed.TotalMilliseconds;

        BuildDungeon();

        float buildMs = (float)watch.Elapsed.TotalMilliseconds - generateMs;

        BakeNavMesh();
        SyncArena();
        ComputeMetrics(generateMs, buildMs);

        if (logGeneration)
            Debug.Log("DungeonGenerator seed " + seed + ": " + Summary(), this);

        Generated?.Invoke(this);
    }

    // Satu percobaan generate-and-test lengkap. Vault direncanakan sesudah validasi dasar.
    private bool RunAttempt(int attemptSeed, out string message)
    {
        BuiltSeed = attemptSeed;
        random = new SeededRandom(attemptSeed);

        InitializeGrid();
        CreateBSP();
        CreateRooms();

        if (rooms.Count > 0)
        {
            CreateCorridors();
            PlaceStartAndGoal();
        }

        bool valid = Validate(out message);
        PlanVault(attemptSeed, valid);
        return valid;
    }

    // ---------- Vault Room ----------

    // Mencari room dead-end, lalu memilih satu sebagai vault. Angka acak vault punya aliran seed sendiri,
    // jadi posisi titik Boss dan bagian lain dungeon tidak bergeser karena vault.
    private void PlanVault(int attemptSeed, bool valid)
    {
        deadEndRooms.Clear();
        vaultDoorCells.Clear();
        VaultRoom = null;
        VaultKeyDistance = -1;

        if (!valid)
        {
            VaultMessage = "dungeon tidak valid";
            return;
        }

        List<DungeonRoom> candidates = new List<DungeonRoom>();
        List<List<Vector2Int>> candidateOpenings = new List<List<Vector2Int>>();

        foreach (DungeonRoom room in rooms)
        {
            if (room == StartRoom || room == GoalRoom)
                continue;

            List<List<Vector2Int>> openings = FindOpenings(room);

            if (openings.Count != 1)
                continue;

            deadEndRooms.Add(room);

            // Bukaan sangat lebar berarti corridor menempel sepanjang sisi room, bukan masuk lewat satu mulut.
            if (openings[0].Count <= corridorWidth + 1)
            {
                candidates.Add(room);
                candidateOpenings.Add(openings[0]);
            }
        }

        if (!VaultPlanned)
        {
            VaultMessage = vaultRoom ? "tidak muncul di dungeon ini (peluang " + Mathf.RoundToInt(vaultChance * 100f) + "%)" : "dimatikan";
            return;
        }

        if (deadEndRooms.Count == 0)
        {
            VaultMessage = "tidak ada room dead-end";
            return;
        }

        if (candidates.Count == 0)
        {
            VaultMessage = "bukaan room dead-end terlalu lebar";
            return;
        }

        SeededRandom vaultRandom = new SeededRandom(SeededRandom.Derive(attemptSeed, 1, PcgStream.Vault));

        while (candidates.Count > 0)
        {
            int index = vaultRandom.Range(0, candidates.Count);
            DungeonRoom room = candidates[index];
            List<Vector2Int> opening = candidateOpenings[index];
            candidates.RemoveAt(index);
            candidateOpenings.RemoveAt(index);

            if (TryPlaceVault(room, opening, vaultRandom))
            {
                vaultRewardSeed = SeededRandom.Derive(attemptSeed, 2, PcgStream.Vault);
                VaultMessage = "room " + rooms.IndexOf(room) + ", pintu " + opening.Count + " cell, kunci " + VaultKeyDistance + " cell dari Start";
                return;
            }
        }

        VaultMessage = "pintu vault memutus jalur dungeon";
    }

    // Bukaan room = cell Floor tepat di luar keempat sisinya, dikelompokkan per sisi menjadi potongan bersambung.
    // Sudut luar tidak dihitung karena BFS dan gerak MC hanya 4 arah.
    private List<List<Vector2Int>> FindOpenings(DungeonRoom room)
    {
        List<List<Vector2Int>> openings = new List<List<Vector2Int>>();

        AddOpenings(openings, new Vector2Int(room.x, room.y - 1), Vector2Int.right, room.width);
        AddOpenings(openings, new Vector2Int(room.x, room.y + room.height), Vector2Int.right, room.width);
        AddOpenings(openings, new Vector2Int(room.x - 1, room.y), Vector2Int.up, room.height);
        AddOpenings(openings, new Vector2Int(room.x + room.width, room.y), Vector2Int.up, room.height);

        return openings;
    }

    private void AddOpenings(List<List<Vector2Int>> openings, Vector2Int start, Vector2Int step, int length)
    {
        List<Vector2Int> current = null;

        for (int i = 0; i < length; i++)
        {
            Vector2Int cell = start + step * i;

            if (!IsWalkable(cell))
            {
                current = null;
                continue;
            }

            if (current == null)
            {
                current = new List<Vector2Int>();
                openings.Add(current);
            }

            current.Add(cell);
        }
    }

    private bool TryPlaceVault(DungeonRoom room, List<Vector2Int> opening, SeededRandom vaultRandom)
    {
        // Hard constraint vault, dicek dengan BFS yang menganggap cell pintu sebagai Wall.
        HashSet<Vector2Int> door = new HashSet<Vector2Int>(opening);
        int[,] distance = ComputeBfs(StartCell, door);

        if (distance[room.Center.x, room.Center.y] >= 0)
            return false;

        foreach (DungeonRoom other in rooms)
        {
            if (other != room && distance[other.Center.x, other.Center.y] < 0)
                return false;
        }

        // Kunci tidak di room Start dan tidak di vault. Room yang lebih jauh (jarak jalan) lebih mungkin dipilih.
        List<DungeonRoom> keyRooms = new List<DungeonRoom>();
        List<float> weights = new List<float>();
        float total = 0f;

        foreach (DungeonRoom other in rooms)
        {
            if (other == room || other == StartRoom)
                continue;

            float weight = distance[other.Center.x, other.Center.y];

            if (weight <= 0f)
                continue;

            keyRooms.Add(other);
            weights.Add(weight);
            total += weight;
        }

        if (keyRooms.Count == 0)
            return false;

        float roll = vaultRandom.Value * total;
        DungeonRoom keyRoom = keyRooms[keyRooms.Count - 1];

        for (int i = 0; i < keyRooms.Count; i++)
        {
            roll -= weights[i];

            if (roll <= 0f)
            {
                keyRoom = keyRooms[i];
                break;
            }
        }

        // Cell kunci di dalam room (tidak menempel dinding jika room cukup besar) dan tidak di atas portal Goal.
        int inset = keyRoom.width > 2 && keyRoom.height > 2 ? 1 : 0;
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int x = keyRoom.x + inset; x < keyRoom.x + keyRoom.width - inset; x++)
        {
            for (int y = keyRoom.y + inset; y < keyRoom.y + keyRoom.height - inset; y++)
            {
                if (distance[x, y] < 0)
                    continue;

                if (Mathf.Abs(x - GoalCell.x) + Mathf.Abs(y - GoalCell.y) < 2)
                    continue;

                cells.Add(new Vector2Int(x, y));
            }
        }

        if (cells.Count == 0)
            return false;

        vaultKeyCell = cells[vaultRandom.Range(0, cells.Count)];
        VaultKeyDistance = distance[vaultKeyCell.x, vaultKeyCell.y];
        VaultRoom = room;
        vaultDoorCells.AddRange(opening);
        return true;
    }

    // Metrics Dungeon (modul): jumlah room, floor, wall, corridor, jalur Start-Goal per strategi, objek, dan waktu.
    private void ComputeMetrics(float generateMs, float buildMs)
    {
        DungeonMetrics m = new DungeonMetrics
        {
            seed = LastSeed,
            builtSeed = BuiltSeed,
            attempts = GenerationAttempts,
            valid = IsValid,
            leafCount = leafNodes.Count,
            roomCount = rooms.Count,
            floorCells = FloorCellCount,
            totalCells = mapWidth * mapHeight,
            builtObjects = builtObjects,
            generateMs = generateMs,
            buildMs = buildMs,
            bakeMs = LastBakeMilliseconds,
            pathBfs = PathLength,
            straightDistance = Vector2Int.Distance(StartCell, GoalCell),
            smallestRoomSide = int.MaxValue,
            deadEndCount = deadEndRooms.Count,
            hasVault = VaultRoom != null,
            keyDistance = VaultKeyDistance,
            propCount = PropCount,
            blockingProps = BlockingPropCount,
            rejectedProps = RejectedPropCount
        };

        int roomArea = 0;

        foreach (DungeonRoom room in rooms)
        {
            roomArea += room.Area;
            m.smallestRoomSide = Mathf.Min(m.smallestRoomSide, Mathf.Min(room.width, room.height));
            m.largestRoomSide = Mathf.Max(m.largestRoomSide, Mathf.Max(room.width, room.height));
        }

        m.averageRoomArea = rooms.Count > 0 ? (float)roomArea / rooms.Count : 0f;

        if (rooms.Count == 0)
            m.smallestRoomSide = 0;

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                if (grid[x, y] == TileType.Wall)
                {
                    if (IsWallVisible(x, y))
                        m.wallCells++;

                    continue;
                }

                if (!IsInsideAnyRoom(new Vector2Int(x, y)))
                    m.corridorCells++;
            }
        }

        // Eksperimen 6: jarak jalan ke Goal versi A (room terakhir) dan B (terjauh garis lurus).
        if (rooms.Count > 0 && bfsDistance != null)
        {
            Vector2Int last = rooms[rooms.Count - 1].Center;
            Vector2Int geometric = FindFarthestRoom(StartCell).Center;
            m.pathLastRoom = bfsDistance[last.x, last.y];
            m.pathGeometric = bfsDistance[geometric.x, geometric.y];
        }

        Metrics = m;
    }

    private bool IsInsideAnyRoom(Vector2Int cell)
    {
        foreach (DungeonRoom room in rooms)
        {
            if (room.Contains(cell))
                return true;
        }

        return false;
    }

    public string Summary()
    {
        return "seed dibangun " + BuiltSeed + ", " + (IsValid ? "VALID" : "TIDAK VALID") + " setelah " + GenerationAttempts + " percobaan, " +
               leafNodes.Count + " leaf, " + rooms.Count + " room (" + ReachableRooms + " terjangkau), " + FloorCellCount + " cell floor, " +
               "jalur Start-Goal " + PathLength + " cell (" + (PathLength * tileSize).ToString("0") + " m), " +
               builtObjects + " objek dibangun" + (bakeNavMesh ? ", bake NavMesh " + LastBakeMilliseconds.ToString("0") + " ms" : string.Empty) +
               ", obor " + torchPositions.Count + ", props " + PropCount + " (" + BlockingPropCount + " penghalang, " + RejectedPropCount + " ditolak BFS)" +
               ", lantai varian " + FloorVariantCells + " cell (" + (FloorCellCount > 0 ? 100f * FloorVariantCells / FloorCellCount : 0f).ToString("0") + "%)" +
               ", " + deadEndRooms.Count + " dead-end, vault " + (VaultRoom != null ? VaultMessage : "tidak ada (" + VaultMessage + ")");
    }

    private void InitializeGrid()
    {
        grid = new TileType[mapWidth, mapHeight];

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
                grid[x, y] = TileType.Wall;
        }
    }

    // Satu cell di setiap tepi disisakan sebagai Wall pembatas.
    private void CreateBSP()
    {
        rootNode = new BSPNode(1, 1, mapWidth - 2, mapHeight - 2);
        leafNodes.Clear();
        SplitNode(rootNode);
    }

    private void SplitNode(BSPNode node)
    {
        if (node.width <= maxLeafSize && node.height <= maxLeafSize)
        {
            leafNodes.Add(node);
            return;
        }

        // Area yang terlalu lebar dipotong vertikal, terlalu tinggi dipotong horizontal, supaya Leaf tidak memanjang.
        bool splitHorizontal;

        if (node.width > node.height * 1.25f)
            splitHorizontal = false;
        else if (node.height > node.width * 1.25f)
            splitHorizontal = true;
        else
            splitHorizontal = random.Value > 0.5f;

        if (splitHorizontal)
            SplitHorizontal(node);
        else
            SplitVertical(node);
    }

    private void SplitHorizontal(BSPNode node)
    {
        if (node.height < minLeafSize * 2)
        {
            leafNodes.Add(node);
            return;
        }

        int split = random.Range(minLeafSize, node.height - minLeafSize);

        node.left = new BSPNode(node.x, node.y, node.width, split);
        node.right = new BSPNode(node.x, node.y + split, node.width, node.height - split);

        SplitNode(node.left);
        SplitNode(node.right);
    }

    private void SplitVertical(BSPNode node)
    {
        if (node.width < minLeafSize * 2)
        {
            leafNodes.Add(node);
            return;
        }

        int split = random.Range(minLeafSize, node.width - minLeafSize);

        node.left = new BSPNode(node.x, node.y, split, node.height);
        node.right = new BSPNode(node.x + split, node.y, node.width - split, node.height);

        SplitNode(node.left);
        SplitNode(node.right);
    }

    private void CreateRooms()
    {
        rooms.Clear();

        foreach (BSPNode leaf in leafNodes)
        {
            int maxWidth = leaf.width - roomMargin * 2;
            int maxHeight = leaf.height - roomMargin * 2;

            if (maxWidth < minRoomSize || maxHeight < minRoomSize)
                continue;

            int roomWidth = random.Range(minRoomSize, maxWidth + 1);
            int roomHeight = random.Range(minRoomSize, maxHeight + 1);

            int maxXOffset = leaf.width - roomWidth - roomMargin;
            int maxYOffset = leaf.height - roomHeight - roomMargin;

            int roomX = leaf.x + random.Range(roomMargin, maxXOffset + 1);
            int roomY = leaf.y + random.Range(roomMargin, maxYOffset + 1);

            DungeonRoom room = new DungeonRoom(roomX, roomY, roomWidth, roomHeight);

            leaf.room = room;
            rooms.Add(room);

            CarveRoom(room);
        }
    }

    private void CarveRoom(DungeonRoom room)
    {
        for (int x = room.x; x < room.x + room.width; x++)
        {
            for (int y = room.y; y < room.y + room.height; y++)
                Carve(x, y);
        }
    }

    // Urutan Leaf dari BSP berdekatan secara ruang, jadi menyambung room berurutan sudah membentuk jalur ke semua room.
    private void CreateCorridors()
    {
        for (int i = 0; i < rooms.Count - 1; i++)
            CreateLCorridor(rooms[i].Center, rooms[i + 1].Center);
    }

    private void CreateLCorridor(Vector2Int from, Vector2Int to)
    {
        bool horizontalFirst = random.Value > 0.5f;

        if (horizontalFirst)
        {
            CarveHorizontal(from.x, to.x, from.y);
            CarveVertical(from.y, to.y, to.x);
        }
        else
        {
            CarveVertical(from.y, to.y, from.x);
            CarveHorizontal(from.x, to.x, to.y);
        }
    }

    // Corridor selebar Corridor Width, berpusat di garis antar titik tengah room.
    private void CarveHorizontal(int x1, int x2, int y)
    {
        int start = Mathf.Min(x1, x2);
        int end = Mathf.Max(x1, x2);
        int offset = (corridorWidth - 1) / 2;

        for (int x = start - offset; x <= end + corridorWidth - 1 - offset; x++)
        {
            for (int w = 0; w < corridorWidth; w++)
                Carve(x, y - offset + w);
        }
    }

    private void CarveVertical(int y1, int y2, int x)
    {
        int start = Mathf.Min(y1, y2);
        int end = Mathf.Max(y1, y2);
        int offset = (corridorWidth - 1) / 2;

        for (int y = start - offset; y <= end + corridorWidth - 1 - offset; y++)
        {
            for (int w = 0; w < corridorWidth; w++)
                Carve(x - offset + w, y);
        }
    }

    // Tepi grid selalu dibiarkan Wall supaya dungeon tertutup.
    private void Carve(int x, int y)
    {
        if (x >= 1 && x < mapWidth - 1 && y >= 1 && y < mapHeight - 1)
            grid[x, y] = TileType.Floor;
    }

    // Start = room pertama. Goal = room yang jarak jalannya (BFS) paling jauh dari Start, bukan jarak garis lurus,
    // jadi Goal benar-benar di ujung dungeon walau ada room yang dekat secara garis lurus tapi jauh lewat corridor.
    private void PlaceStartAndGoal()
    {
        StartRoom = rooms[0];
        StartCell = StartRoom.Center;

        RunBfs(StartCell);

        GoalRoom = null;
        int farthest = -1;

        foreach (DungeonRoom room in rooms)
        {
            int distance = bfsDistance[room.Center.x, room.Center.y];

            if (room != StartRoom && distance > farthest)
            {
                farthest = distance;
                GoalRoom = room;
            }
        }

        // Tidak ada room lain yang terjangkau: pakai jarak garis lurus (versi modul), validasi akan menolaknya.
        if (GoalRoom == null)
            GoalRoom = FindFarthestRoom(StartCell);

        GoalCell = GoalRoom.Center;
        PathLength = bfsDistance[GoalCell.x, GoalCell.y];
        BuildGoalPath();

        grid[StartCell.x, StartCell.y] = TileType.Start;
        grid[GoalCell.x, GoalCell.y] = TileType.Goal;
    }

    private static readonly Vector2Int[] Neighbours =
    {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
    };

    // Breadth-First Search dari Start ke 4 arah lewat cell yang bisa dilewati.
    // bfsDistance[x, y] = jumlah langkah terpendek dari Start, -1 = tidak terjangkau.
    private void RunBfs(Vector2Int start)
    {
        bfsDistance = ComputeBfs(start, null);
    }

    // Cell di 'blocked' dianggap Wall, mis. pintu vault yang masih tertutup.
    private int[,] ComputeBfs(Vector2Int start, HashSet<Vector2Int> blocked)
    {
        int[,] distance = new int[mapWidth, mapHeight];

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
                distance[x, y] = -1;
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        distance[start.x, start.y] = 0;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int next = distance[cell.x, cell.y] + 1;

            for (int i = 0; i < Neighbours.Length; i++)
            {
                Vector2Int n = cell + Neighbours[i];

                if (!IsInsideGrid(n.x, n.y) || !IsWalkable(grid[n.x, n.y]) || distance[n.x, n.y] >= 0)
                    continue;

                if (blocked != null && blocked.Contains(n))
                    continue;

                distance[n.x, n.y] = next;
                queue.Enqueue(n);
            }
        }

        return distance;
    }

    // Jalur terpendek Start ke Goal: dari Goal selalu melangkah ke tetangga yang jaraknya satu lebih kecil, lalu dibalik.
    private void BuildGoalPath()
    {
        goalPath.Clear();

        if (PathLength < 0)
            return;

        Vector2Int cell = GoalCell;
        goalPath.Add(cell);

        while (bfsDistance[cell.x, cell.y] > 0)
        {
            int current = bfsDistance[cell.x, cell.y];

            for (int i = 0; i < Neighbours.Length; i++)
            {
                Vector2Int n = cell + Neighbours[i];

                if (IsInsideGrid(n.x, n.y) && bfsDistance[n.x, n.y] == current - 1)
                {
                    cell = n;
                    break;
                }
            }

            goalPath.Add(cell);
        }

        goalPath.Reverse();
    }

    // Hard constraint dungeon. Pesan berisi alasan pertama yang gagal.
    private bool Validate(out string message)
    {
        ReachableRooms = 0;

        if (rooms.Count < minRoomCount)
        {
            message = "room " + rooms.Count + " < " + minRoomCount;
            return false;
        }

        foreach (DungeonRoom room in rooms)
        {
            if (bfsDistance[room.Center.x, room.Center.y] >= 0)
                ReachableRooms++;
        }

        if (GoalRoom == StartRoom || PathLength <= 0)
        {
            message = "Start dan Goal tidak terhubung";
            return false;
        }

        if (ReachableRooms < rooms.Count)
        {
            message = (rooms.Count - ReachableRooms) + " room tidak terjangkau dari Start";
            return false;
        }

        message = "Start-Goal terhubung, semua room terjangkau";
        return true;
    }

    private DungeonRoom FindFarthestRoom(Vector2Int position)
    {
        DungeonRoom farthest = rooms[0];
        float farthestDistance = 0f;

        foreach (DungeonRoom room in rooms)
        {
            float distance = Vector2Int.Distance(position, room.Center);

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthest = room;
            }
        }

        return farthest;
    }

    // ---------- Build ----------

    private void BuildDungeon()
    {
        CreateLevelRoot();

        builtObjects = 0;
        FloorCellCount = 0;
        AssignRoomThemes();
        PlanFloorVariation();

        int groundLayer = LayerMask.NameToLayer(GameLayers.Ground);
        int wallLayer = LayerMask.NameToLayer(GameLayers.NavBlock);

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (IsWalkable(grid[x, y]))
                    FloorCellCount++;
            }

            BuildRow(y, true, groundLayer);
            BuildRow(y, false, wallLayer);
        }

        BuildGoalMarker();
        BuildBossRoom();
        BuildVault();
        BuildTorches();
        BuildDecoration();
    }

    private void BuildVault()
    {
        Vault = null;

        if (VaultRoom == null || vaultDoorCells.Count == 0)
            return;

        GameObject group = new GameObject("Vault");
        group.transform.SetParent(levelRoot, false);

        DungeonVault vault = group.AddComponent<DungeonVault>();
        vault.Setup(VaultRoom, vaultMinRarity, vaultRewardSeed, CellToWorld(vaultKeyCell));

        VaultDoor door = BuildVaultDoor(group.transform, vault);
        VaultChest chest = BuildVaultChest(group.transform, vault, door.transform.position);
        VaultKey key = BuildVaultKey(group.transform, vault);

        vault.AttachParts(door, chest, key);
        Vault = vault;
    }

    private VaultDoor BuildVaultDoor(Transform parent, DungeonVault vault)
    {
        Vector3 center = Vector3.zero;

        foreach (Vector2Int cell in vaultDoorCells)
            center += CellToWorld(cell);

        center /= vaultDoorCells.Count;

        // Bukaan di sisi bawah atau atas room memanjang di sumbu X, di sisi kiri atau kanan memanjang di sumbu Z.
        Vector2Int first = vaultDoorCells[0];
        bool alongX = first.y < VaultRoom.y || first.y >= VaultRoom.y + VaultRoom.height;
        Quaternion rotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

        float length = vaultDoorCells.Count * tileSize;
        float thickness = tileSize * 0.4f;
        float blockHeight = Mathf.Max(doorBlockHeight, wallHeight);
        int wallLayer = LayerMask.NameToLayer(GameLayers.NavBlock);

        GameObject root = new GameObject("VaultDoor");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(center, rotation);

        if (wallLayer >= 0)
            root.layer = wallLayer;

        Transform visualRoot = new GameObject("Visual").transform;
        visualRoot.SetParent(root.transform, false);
        List<Transform> openParts = new List<Transform>();

        if (doorPrefab != null)
        {
            // Satu pintu di tengah bukaan, di antara cell-cell bukaan.
            GameObject piece = Instantiate(doorPrefab, center + doorPrefabOffset, rotation * Quaternion.Euler(doorPrefabRotation), visualRoot);
            Vector3 scale = doorPrefabScale;
            scale.x *= vaultDoorCells.Count / (float)Mathf.Max(1, corridorWidth);
            piece.transform.localScale = Vector3.Scale(doorPrefab.transform.localScale, scale);
            StripPhysics(piece);
            SetLayerRecursive(piece, wallLayer);
            openParts = GatePortalGlow.FindGateParts(piece.transform, doorOpenPartPrefix);
            builtObjects++;
        }
        else
        {
            float visualHeight = wallHeight + 0.15f;
            GameObject slab = CreateVisualCube("Slab", visualRoot, new Vector3(length, visualHeight, thickness), Vector3.up * visualHeight * 0.5f, DoorMaterial);
            SetLayerRecursive(slab, wallLayer);
            builtObjects++;
        }

        CreateGlow("Glow", root.transform, Vector3.up * 1.1f, GateRarityStyle.Color(vaultMinRarity), tileSize * 2.5f, 1.2f);

        VaultDoor door = root.AddComponent<VaultDoor>();
        door.Setup(vault, visualRoot, openParts, new Vector3(length, blockHeight, thickness), length * 0.5f + tileSize);
        return door;
    }

    private VaultChest BuildVaultChest(Transform parent, DungeonVault vault, Vector3 doorPosition)
    {
        Vector3 position = CellToWorld(VaultRoom.Center);
        Vector3 toDoor = doorPosition - position;
        toDoor.y = 0f;

        GameObject root = new GameObject("VaultChest");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, toDoor.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toDoor) : Quaternion.identity);

        Transform visualRoot = new GameObject("Visual").transform;
        visualRoot.SetParent(root.transform, false);
        Transform lid = null;

        if (chestPrefab != null)
        {
            GameObject instance = Instantiate(chestPrefab, visualRoot);
            instance.transform.localPosition = chestPrefabOffset;
            instance.transform.localRotation = Quaternion.Euler(chestPrefabRotation);
            SetLayerRecursive(instance, 0);
        }
        else
        {
            float size = Mathf.Clamp(tileSize, 0.8f, 1.6f);
            CreateVisualCube("Body", visualRoot, new Vector3(0.9f, 0.55f, 0.6f) * size, Vector3.up * 0.275f * size, ChestMaterial);

            // Engsel tutup di sisi belakang, tutup memanjang ke depan (arah pintu).
            lid = new GameObject("LidPivot").transform;
            lid.SetParent(visualRoot, false);
            lid.localPosition = new Vector3(0f, 0.55f * size, -0.3f * size);
            CreateVisualCube("Lid", lid, new Vector3(0.94f, 0.16f, 0.64f) * size, new Vector3(0f, 0.08f * size, 0.32f * size), GoldMaterial);
        }

        builtObjects++;

        Light glow = CreateGlow("Glow", root.transform, Vector3.up * 1.2f, GateRarityStyle.Color(vaultMinRarity), tileSize * 3f, 1.2f);

        VaultChest chest = root.AddComponent<VaultChest>();
        chest.Setup(vault, visualRoot, lid, glow, tileSize * 0.9f + 0.4f);
        return chest;
    }

    private VaultKey BuildVaultKey(Transform parent, DungeonVault vault)
    {
        GameObject root = new GameObject("VaultKey");
        root.transform.SetParent(parent, false);
        root.transform.position = CellToWorld(vaultKeyCell);

        // Child pertama = visual yang diputar dan dinaik-turunkan PickupItem.
        Transform visualRoot = new GameObject("Visual").transform;
        visualRoot.SetParent(root.transform, false);

        if (keyPrefab != null)
        {
            GameObject instance = Instantiate(keyPrefab, visualRoot);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(keyPrefabRotation);
            instance.transform.localScale *= keyPrefabScale;
            StripPhysics(instance);
            SetLayerRecursive(instance, 0);
        }
        else
        {
            CreateVisualCube("Shaft", visualRoot, new Vector3(0.1f, 0.1f, 0.5f), Vector3.zero, GoldMaterial);
            CreateVisualCube("Bit", visualRoot, new Vector3(0.1f, 0.18f, 0.1f), new Vector3(0f, -0.09f, -0.18f), GoldMaterial);
            CreateVisualCube("Bow", visualRoot, new Vector3(0.28f, 0.28f, 0.08f), new Vector3(0f, 0f, 0.28f), GoldMaterial);
        }

        builtObjects++;

        CreateGlow("Glow", root.transform, Vector3.up * 0.7f, new Color(1f, 0.82f, 0.3f), tileSize * 2.5f, 1.4f);

        VaultKey key = root.AddComponent<VaultKey>();
        key.Bind(vault);
        return key;
    }

    private static GameObject CreateVisualCube(string name, Transform parent, Vector3 scale, Vector3 localPosition, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        DestroyImmediateSafe(cube.GetComponent<Collider>());
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = scale;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    private static Light CreateGlow(string name, Transform parent, Vector3 localPosition, Color color, float range, float intensity)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = localPosition;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        return light;
    }

    // Item dan visual pintu tidak boleh punya collider sendiri. Dihapus langsung supaya tidak ikut terbaca bake NavMesh di frame ini.
    private static void StripPhysics(GameObject instance)
    {
        foreach (Collider col in instance.GetComponentsInChildren<Collider>(true))
            DestroyImmediateSafe(col);

        foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
            DestroyImmediateSafe(body);
    }

    private static void SetLayerRecursive(GameObject target, int layer)
    {
        if (layer < 0)
            return;

        foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }

    // Komponen objek yang baru dibuat aman dihapus langsung, juga saat Play.
    private static void DestroyImmediateSafe(UnityEngine.Object target)
    {
        if (target != null)
            DestroyImmediate(target);
    }

    // Obor dinding. Room: jumlah obor dari keliling room, lalu disebar dengan farthest-point sampling (obor pertama acak,
    // berikutnya selalu tempat yang paling jauh dari obor yang sudah ada). Koridor: tempat diacak, lalu diterima jika
    // cukup jauh dari semua obor lain. Obor hanya visual dan lampu tanpa collider, jadi tidak mengubah jalur dan BFS.
    private void BuildTorches()
    {
        torchPositions.Clear();
        torchCells.Clear();

        if (!torches || rooms.Count == 0)
            return;

        SeededRandom decor = new SeededRandom(SeededRandom.Derive(BuiltSeed, 1, PcgStream.Decor));

        GameObject group = new GameObject("Torches");
        group.transform.SetParent(levelRoot, false);

        List<Vector2Int> placed = new List<Vector2Int>();

        foreach (DungeonRoom room in rooms)
        {
            List<TorchSlot> slots = RoomTorchSlots(room);

            if (slots.Count == 0)
                continue;

            int count = Mathf.Max(1, Mathf.RoundToInt(2f * (room.width + room.height) / roomTorchSpacing));

            if (room == GoalRoom)
                count += goalRoomExtraTorches;

            foreach (TorchSlot slot in SpreadTorchSlots(slots, count, decor))
            {
                SpawnTorch(group.transform, slot, decor);
                placed.Add(slot.cell);
            }
        }

        if (corridorTorchSpacing <= 0)
            return;

        List<TorchSlot> corridor = CorridorTorchSlots();

        // Fisher-Yates dari seed supaya urutan penerimaan tidak condong ke kiri bawah map.
        for (int i = corridor.Count - 1; i > 0; i--)
        {
            int j = decor.Range(0, i + 1);
            TorchSlot swap = corridor[i];
            corridor[i] = corridor[j];
            corridor[j] = swap;
        }

        foreach (TorchSlot slot in corridor)
        {
            if (!FarFromAll(slot.cell, placed, corridorTorchSpacing))
                continue;

            SpawnTorch(group.transform, slot, decor);
            placed.Add(slot.cell);
        }
    }

    // Cell tepi room yang di luarnya dinding (bukan bukaan). Cell portal, kunci, dan chest dilewati.
    private List<TorchSlot> RoomTorchSlots(DungeonRoom room)
    {
        List<TorchSlot> slots = new List<TorchSlot>();

        for (int x = room.x; x < room.x + room.width; x++)
        {
            AddTorchSlot(slots, new Vector2Int(x, room.y), Vector2Int.down);
            AddTorchSlot(slots, new Vector2Int(x, room.y + room.height - 1), Vector2Int.up);
        }

        for (int y = room.y; y < room.y + room.height; y++)
        {
            AddTorchSlot(slots, new Vector2Int(room.x, y), Vector2Int.left);
            AddTorchSlot(slots, new Vector2Int(room.x + room.width - 1, y), Vector2Int.right);
        }

        return slots;
    }

    private List<TorchSlot> CorridorTorchSlots()
    {
        List<TorchSlot> slots = new List<TorchSlot>();
        Vector2Int[] directions = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);

                if (!IsWalkable(grid[x, y]) || IsInsideAnyRoom(cell) || vaultDoorCells.Contains(cell))
                    continue;

                foreach (Vector2Int direction in directions)
                    AddTorchSlot(slots, cell, direction);
            }
        }

        return slots;
    }

    private void AddTorchSlot(List<TorchSlot> slots, Vector2Int cell, Vector2Int toWall)
    {
        Vector2Int wall = cell + toWall;

        if (wall.x < 0 || wall.y < 0 || wall.x >= mapWidth || wall.y >= mapHeight || grid[wall.x, wall.y] != TileType.Wall)
            return;

        if (!IsWalkable(grid[cell.x, cell.y]) || cell == GoalCell || cell == StartCell)
            return;

        if (VaultRoom != null && (cell == vaultKeyCell || cell == VaultRoom.Center))
            return;

        slots.Add(new TorchSlot { cell = cell, toWall = toWall });
    }

    private static List<TorchSlot> SpreadTorchSlots(List<TorchSlot> slots, int count, SeededRandom decor)
    {
        List<TorchSlot> chosen = new List<TorchSlot>();
        List<int> chosenIndex = new List<int>();

        int first = decor.Range(0, slots.Count);
        chosen.Add(slots[first]);
        chosenIndex.Add(first);

        while (chosen.Count < count && chosen.Count < slots.Count)
        {
            int best = -1;
            int bestDistance = -1;

            for (int i = 0; i < slots.Count; i++)
            {
                if (chosenIndex.Contains(i))
                    continue;

                int nearest = int.MaxValue;

                foreach (TorchSlot other in chosen)
                    nearest = Mathf.Min(nearest, Manhattan(slots[i].cell, other.cell));

                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = i;
                }
            }

            if (best < 0)
                break;

            chosen.Add(slots[best]);
            chosenIndex.Add(best);
        }

        return chosen;
    }

    private static bool FarFromAll(Vector2Int cell, List<Vector2Int> others, int spacing)
    {
        foreach (Vector2Int other in others)
        {
            if (Manhattan(cell, other) < spacing)
                return false;
        }

        return true;
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private void SpawnTorch(Transform parent, TorchSlot slot, SeededRandom decor)
    {
        Vector3 toWall = new Vector3(slot.toWall.x, 0f, slot.toWall.y);
        Vector3 position = CellToWorld(slot.cell) + toWall * (tileSize * 0.5f - torchWallOffset) + Vector3.up * torchHeight;

        GameObject root = new GameObject("Torch");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-toWall));

        Transform flame = null;

        if (torchPrefab != null)
        {
            GameObject instance = Instantiate(torchPrefab, root.transform);
            instance.transform.localPosition = torchPrefabOffset;
            instance.transform.localRotation = Quaternion.Euler(torchPrefabRotation);
            StripPhysics(instance);
            SetLayerRecursive(instance, 0);
        }
        else
        {
            float size = Mathf.Clamp(tileSize, 0.8f, 1.6f);
            CreateVisualCube("Handle", root.transform, new Vector3(0.07f, 0.3f, 0.07f) * size, new Vector3(0f, 0f, 0.06f) * size, TorchHandleMaterial);
            flame = CreateVisualCube("Flame", root.transform, new Vector3(0.11f, 0.15f, 0.11f) * size, new Vector3(0f, 0.2f, 0.06f) * size, FlameMaterial).transform;
        }

        // Lampu sedikit di depan dinding supaya menerangi lantai room, bukan hanya dinding di belakangnya.
        Light light = CreateGlow("Light", root.transform, new Vector3(0f, 0.25f, tileSize * 0.35f), torchColor, Mathf.Max(torchRange, tileSize * 2f), torchIntensity);
        root.AddComponent<TorchFlicker>().Setup(light, flame, torchFlicker, torchFlickerSpeed, decor.Range(0f, 100f));

        torchPositions.Add(position);
        torchCells.Add(slot.cell);
        builtObjects++;
    }

    // Titik khusus Boss Room di sekitar portal Goal. Spawner wajib memakainya untuk role boss (SpawnPoint Always Use),
    // jadi Guardian hanya muncul di room Goal. Titik disebar di dalam room, tidak tepat di portal.
    private void BuildBossRoom()
    {
        bossPoints.Clear();

        if (!bossRoom || GoalRoom == null)
            return;

        GameObject group = new GameObject("BossRoom");
        group.transform.SetParent(levelRoot, false);

        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int x = GoalRoom.x + 1; x < GoalRoom.x + GoalRoom.width - 1; x++)
        {
            for (int y = GoalRoom.y + 1; y < GoalRoom.y + GoalRoom.height - 1; y++)
            {
                if (Mathf.Abs(x - GoalCell.x) + Mathf.Abs(y - GoalCell.y) >= 2)
                    candidates.Add(new Vector2Int(x, y));
            }
        }

        for (int i = 0; i < bossSpawnPoints && candidates.Count > 0; i++)
        {
            Vector2Int cell = candidates[random.Range(0, candidates.Count)];

            // Titik berikutnya tidak menempel titik ini.
            candidates.RemoveAll(c => Mathf.Abs(c.x - cell.x) <= 1 && Mathf.Abs(c.y - cell.y) <= 1);

            Vector3 position = CellToWorld(cell);
            Vector3 toEntrance = StartWorld - position;
            toEntrance.y = 0f;

            GameObject point = new GameObject("BossSpawn_" + i);
            point.transform.SetParent(group.transform, false);
            point.transform.position = position;
            point.transform.rotation = toEntrance.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toEntrance) : Quaternion.identity;
            point.AddComponent<SpawnPoint>().Configure(bossRole, true);

            bossPoints.Add(position);
        }
    }

    // Satu baris grid. Cell berurutan dengan jenis yang sama digabung menjadi satu blok jika Merge Tiles aktif.
    private void BuildRow(int y, bool floor, int layer)
    {
        GameObject prefab = floor ? floorPrefab : wallPrefab;
        int x = 0;

        while (x < mapWidth)
        {
            if (!NeedsBlock(x, y, floor))
            {
                x++;
                continue;
            }

            Vector3 position = CellToWorld(new Vector2Int(x, y));
            int variant = floor ? floorChoice[x, y] : -1;

            if (variant >= 0)
            {
                FloorVariant chosen = floorVariants[variant];
                SpawnPrefab(chosen.prefab, position + floorPrefabOffset + chosen.offset, layer, floorTurns[x, y]);
                x++;
                continue;
            }

            if (prefab != null)
            {
                SpawnPrefab(prefab, position + (floor ? floorPrefabOffset : wallPrefabOffset), layer, floor ? floorTurns[x, y] : 0);
                x++;
                continue;
            }

            // Blok bawaan: cell polos berurutan digabung, berhenti di cell varian.
            int runStart = x;
            x++;

            if (mergeTiles)
            {
                while (x < mapWidth && NeedsBlock(x, y, floor) && (!floor || floorChoice[x, y] < 0))
                    x++;
            }

            SpawnBlock(runStart, y, x - runStart, floor, layer);
        }
    }

    // Menentukan prefab dan putaran setiap cell lantai sebelum dibangun, dari stream Decor seed dungeon.
    // Peluang varian = Floor Variant Chance, dikali pola Perlin noise (kelompok area usang), ditambah bonus koridor
    // dan tepi dinding. Setiap cell selalu mengambil tiga angka acak, jadi mengubah peluang atau jumlah varian
    // tidak menggeser hasil cell lain.
    private void PlanFloorVariation()
    {
        floorChoice = new int[mapWidth, mapHeight];
        floorTurns = new int[mapWidth, mapHeight];
        FloorVariantCells = 0;

        SeededRandom decor = new SeededRandom(SeededRandom.Derive(BuiltSeed, 0, PcgStream.Decor));
        Vector2 noiseOffset = new Vector2(decor.Range(0f, 1000f), decor.Range(0f, 1000f));

        float totalWeight = 0f;
        int usable = 0;

        if (floorVariants != null)
        {
            foreach (FloorVariant variant in floorVariants)
            {
                if (variant == null || variant.prefab == null)
                    continue;

                totalWeight += Mathf.Max(0f, variant.weight);
                usable++;
            }
        }

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float roll = decor.Value;
                float pick = decor.Value;
                int turns = decor.Range(0, 4);

                floorChoice[x, y] = -1;

                if (!IsWalkable(grid[x, y]))
                    continue;

                floorTurns[x, y] = randomFloorRotation ? turns : 0;

                if (usable == 0 || roll >= FloorVariantChance(x, y, noiseOffset))
                    continue;

                floorChoice[x, y] = PickFloorVariant(pick, totalWeight);
                FloorVariantCells++;
            }
        }
    }

    private float FloorVariantChance(int x, int y, Vector2 noiseOffset)
    {
        float chance = floorVariantChance;

        if (floorClusterStrength > 0f)
        {
            float size = Mathf.Max(1f, floorClusterSize);
            float noise = Mathf.PerlinNoise(noiseOffset.x + x / size, noiseOffset.y + y / size);

            // Perlin jarang mendekati 0 atau 1, jadi kontrasnya dinaikkan supaya kelompoknya terlihat.
            noise = Mathf.Clamp01((noise - 0.5f) * 2.5f + 0.5f);

            // Rata-rata 2 x noise = 1, jadi rata-rata peluang tetap Floor Variant Chance.
            chance *= Mathf.Lerp(1f, 2f * noise, floorClusterStrength);
        }

        Vector2Int cell = new Vector2Int(x, y);

        if (!IsInsideAnyRoom(cell))
        {
            chance += corridorVariantBonus;
        }
        else
        {
            if (TouchesWall(x, y))
                chance += wallEdgeVariantBonus;

            chance += ThemeFloorBonus(cell);
        }

        return Mathf.Clamp01(chance);
    }

    // Undian berbobot. Semua bobot 0 = semua varian berpeluang sama.
    private int PickFloorVariant(float pick, float totalWeight)
    {
        bool equal = totalWeight <= 0f;
        float target = pick * (equal ? CountUsableFloorVariants() : totalWeight);
        int last = -1;

        for (int i = 0; i < floorVariants.Length; i++)
        {
            FloorVariant variant = floorVariants[i];

            if (variant == null || variant.prefab == null)
                continue;

            last = i;
            target -= equal ? 1f : Mathf.Max(0f, variant.weight);

            if (target < 0f)
                return i;
        }

        return last;
    }

    private int CountUsableFloorVariants()
    {
        int count = 0;

        foreach (FloorVariant variant in floorVariants)
        {
            if (variant != null && variant.prefab != null)
                count++;
        }

        return count;
    }

    private bool TouchesWall(int x, int y)
    {
        return IsWallOrOutside(x + 1, y) || IsWallOrOutside(x - 1, y) || IsWallOrOutside(x, y + 1) || IsWallOrOutside(x, y - 1);
    }

    private bool IsWallOrOutside(int x, int y)
    {
        return x < 0 || y < 0 || x >= mapWidth || y >= mapHeight || grid[x, y] == TileType.Wall;
    }

    private bool NeedsBlock(int x, int y, bool floor)
    {
        TileType tile = grid[x, y];
        return floor ? IsWalkable(tile) : tile == TileType.Wall && IsWallVisible(x, y);
    }

    private void SpawnBlock(int x, int y, int length, bool floor, int layer)
    {
        float height = floor ? floorThickness : wallHeight;
        float width = length * tileSize;

        Vector3 position = transform.position + new Vector3(x * tileSize + width * 0.5f, floor ? -floorThickness * 0.5f : wallHeight * 0.5f, (y + 0.5f) * tileSize);

        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = (floor ? "Floor_" : "Wall_") + y + "_" + x;
        block.transform.SetParent(levelRoot, false);
        block.transform.position = position;
        block.transform.localScale = new Vector3(width, height, tileSize);
        block.GetComponent<MeshRenderer>().sharedMaterial = floor ? FloorMaterial : WallMaterial;

        if (layer >= 0)
            block.layer = layer;

        builtObjects++;
    }

    private void SpawnPrefab(GameObject prefab, Vector3 position, int layer, int quarterTurns = 0)
    {
        GameObject instance = Instantiate(prefab, position, Quaternion.identity, levelRoot);

        // Lantai butuh collider untuk raycast spawn dan NavMesh, dinding untuk line of sight dan proyektil.
        if (instance.GetComponentInChildren<Collider>() == null)
            AddBoundsCollider(instance);

        if (layer >= 0)
        {
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        // Diputar di tengah model, bukan di pivot, supaya tile tetap menutup cell-nya walau pivot prefab tidak di tengah.
        if (quarterTurns % 4 != 0)
        {
            Vector3 pivot = position;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();

            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;

                foreach (Renderer target in renderers)
                    bounds.Encapsulate(target.bounds);

                pivot = new Vector3(bounds.center.x, position.y, bounds.center.z);
            }

            instance.transform.RotateAround(pivot, Vector3.up, quarterTurns * 90f);
        }

        builtObjects++;
    }

    private static void AddBoundsCollider(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;

        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);

        BoxCollider box = instance.AddComponent<BoxCollider>();
        Vector3 scale = instance.transform.lossyScale;
        box.center = instance.transform.InverseTransformPoint(bounds.center);
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }

    // NavMeshSurface dipasang di root hasil generate, jadi datanya ikut hilang saat dungeon dihapus.
    private void BakeNavMesh()
    {
        LastBakeMilliseconds = 0f;

        if (!bakeNavMesh || levelRoot == null)
            return;

        // Collider baru harus terdaftar di physics sebelum NavMesh mengumpulkan geometri.
        Physics.SyncTransforms();

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        NavMeshSurface surface = levelRoot.gameObject.AddComponent<NavMeshSurface>();
        surface.agentTypeID = agentTypeId;
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = GameLayers.GroundMask;
        surface.defaultArea = 0;
        surface.BuildNavMesh();

        watch.Stop();
        LastBakeMilliseconds = (float)watch.Elapsed.TotalMilliseconds;

        if (surface.navMeshData == null)
            Debug.LogWarning("DungeonGenerator: NavMesh dungeon kosong. Pastikan lantai punya collider di layer Ground.", this);
    }

    // Mode Dungeon: titik masuk MC di Start menghadap Goal, dan area spawn PCG menutupi seluruh grid.
    private void SyncArena()
    {
        if (playerEntry == null)
        {
            Transform existing = transform.Find("PlayerEntry");

            if (existing == null)
            {
                existing = new GameObject("PlayerEntry").transform;
                existing.SetParent(transform, false);
            }

            playerEntry = existing;
        }

        Vector3 start = StartWorld;

        // Titik Start diusahakan tepat di NavMesh supaya MC tidak berdiri di tepi yang terkikis.
        if (NavMesh.SamplePosition(start, out NavMeshHit hit, tileSize, NavMesh.AllAreas))
            start = hit.position;

        Vector3 toGoal = GoalWorld - start;
        toGoal.y = 0f;

        playerEntry.position = start;
        playerEntry.rotation = toGoal.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toGoal) : Quaternion.identity;

        Arena arena = GetComponent<Arena>();

        if (arena != null)
        {
            arena.SetPlayerEntry(playerEntry);
            arena.RefreshSpawnPoints();
        }

        PcgSpawnArea area = GetComponent<PcgSpawnArea>();

        if (area != null)
        {
            area.Configure(WorldCenter, WorldSize);
            // Musuh dan item tidak ditempatkan di safe room dan di dalam vault.
            List<Bounds> exclusions = new List<Bounds>();

            if (safeStartRoom && StartRoom != null)
                exclusions.Add(RoomBounds(StartRoom));

            if (VaultRoom != null)
                exclusions.Add(RoomBounds(VaultRoom));

            area.SetExclusions(exclusions);
        }
    }

    // Portal Goal: prefab Goal (atau cakram bawaan) + komponen portal yang membuka jalan keluar setelah ronde bersih.
    private void BuildGoalMarker()
    {
        GameObject marker;

        if (goalPrefab != null)
        {
            marker = Instantiate(goalPrefab, GoalWorld + goalPrefabOffset, Quaternion.identity, levelRoot);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.transform.SetParent(levelRoot, false);
            marker.transform.position = GoalWorld + Vector3.up * 0.03f;
            marker.transform.localScale = new Vector3(tileSize * 1.2f, 0.03f, tileSize * 1.2f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = GoalMaterial;
            DestroySafe(marker.GetComponent<Collider>());
        }

        marker.name = "Goal";
        builtObjects++;

        GoalPortal = marker.AddComponent<DungeonGoalPortal>();
        GoalPortal.Build(tileSize);
    }

    // Wall yang tidak bersebelahan dengan Floor tidak terlihat dan tidak berguna, jadi tidak dibangun (optimasi modul).
    private bool IsWallVisible(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;

                if (IsInsideGrid(nx, ny) && grid[nx, ny] != TileType.Wall)
                    return true;
            }
        }

        return false;
    }

    private void CreateLevelRoot()
    {
        GameObject root = new GameObject(LevelRootName);
        root.transform.SetParent(transform, false);

        // Hasil preview Editor tidak ikut tersimpan ke file scene.
        if (!Application.isPlaying)
            root.hideFlags = HideFlags.DontSave;

        levelRoot = root.transform;
    }

    [ContextMenu("Clear Dungeon")]
    public void ClearDungeon()
    {
        // Referensi bisa hilang setelah compile, jadi child dengan nama yang sama juga dicari.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (child.name != LevelRootName)
                continue;

            // Destroy saat Play baru terjadi di akhir frame. Dinonaktifkan dan dilepas dulu supaya NavMesh, collider,
            // dan titik Boss dungeon lama tidak ikut terbaca saat dungeon baru dibangun di frame yang sama.
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            DestroySafe(child.gameObject);
        }

        levelRoot = null;
        grid = null;
        bfsDistance = null;
        goalPath.Clear();
        bossPoints.Clear();
        GoalPortal = null;
        deadEndRooms.Clear();
        vaultDoorCells.Clear();
        torchPositions.Clear();
        ClearDecoration();
        VaultRoom = null;
        Vault = null;
        VaultKeyDistance = -1;
        VaultMessage = string.Empty;
        PathLength = -1;
        IsValid = false;
        rooms.Clear();
        leafNodes.Clear();
        builtObjects = 0;
        FloorCellCount = 0;
        FloorVariantCells = 0;
    }

    private static void DestroySafe(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    // ---------- Grid utility ----------

    // Batas room di dunia (XZ), ditambah setengah cell supaya mulut corridor di tepi room ikut tercakup.
    public Bounds RoomBounds(DungeonRoom room)
    {
        Vector3 min = transform.position + new Vector3((room.x - 0.5f) * tileSize, -1f, (room.y - 0.5f) * tileSize);
        Vector3 max = transform.position + new Vector3((room.x + room.width + 0.5f) * tileSize, 3f, (room.y + room.height + 0.5f) * tileSize);
        Bounds bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    public bool IsInsideGrid(int x, int y)
    {
        return x >= 0 && x < mapWidth && y >= 0 && y < mapHeight;
    }

    public static bool IsWalkable(TileType tile)
    {
        return tile != TileType.Wall;
    }

    public bool IsWalkable(Vector2Int cell)
    {
        return grid != null && IsInsideGrid(cell.x, cell.y) && IsWalkable(grid[cell.x, cell.y]);
    }

    // Titik tengah cell di permukaan lantai.
    public Vector3 CellToWorld(Vector2Int cell)
    {
        return transform.position + new Vector3((cell.x + 0.5f) * tileSize, 0f, (cell.y + 0.5f) * tileSize);
    }

    public Vector2Int WorldToCell(Vector3 world)
    {
        Vector3 local = world - transform.position;
        return new Vector2Int(Mathf.FloorToInt(local.x / tileSize), Mathf.FloorToInt(local.z / tileSize));
    }

    // ---------- Material bawaan ----------

    private Material FloorMaterial => floorMaterial != null ? floorMaterial : DefaultMaterial(ref defaultFloorMaterial, new Color(0.36f, 0.38f, 0.44f), 0f);

    private Material WallMaterial => wallMaterial != null ? wallMaterial : DefaultMaterial(ref defaultWallMaterial, new Color(0.2f, 0.22f, 0.3f), 0f);

    private static Material GoalMaterial => DefaultMaterial(ref defaultGoalMaterial, new Color(1f, 0.8f, 0.25f), 2f);

    private static Material DoorMaterial => DefaultMaterial(ref defaultDoorMaterial, new Color(0.22f, 0.36f, 0.62f), 0.6f);

    private static Material ChestMaterial => DefaultMaterial(ref defaultChestMaterial, new Color(0.42f, 0.26f, 0.14f), 0f);

    private static Material GoldMaterial => DefaultMaterial(ref defaultGoldMaterial, new Color(1f, 0.78f, 0.25f), 1.5f);

    private static Material FlameMaterial => DefaultMaterial(ref defaultFlameMaterial, new Color(1f, 0.55f, 0.15f), 4f);

    private static Material TorchHandleMaterial => DefaultMaterial(ref defaultTorchHandleMaterial, new Color(0.17f, 0.11f, 0.07f), 0f);

    private static Material DefaultMaterial(ref Material cache, Color color, float emission)
    {
        if (cache != null)
            return cache;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        cache = new Material(shader) { hideFlags = HideFlags.DontSave, color = color };
        cache.SetColor("_BaseColor", color);

        if (emission > 0f)
        {
            cache.EnableKeyword("_EMISSION");
            cache.SetColor("_EmissionColor", color * emission);
        }

        return cache;
    }

    // ---------- Editor preview ----------

    [ContextMenu("Generate Preview")]
    private void GeneratePreview()
    {
        Generate(previewSeed);
    }

    // Eksperimen 1 dan tugas modul: satu baris tabel per seed, siap disalin ke laporan.
    [ContextMenu("Run Seed Experiment")]
    private void RunSeedExperiment()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("DungeonGenerator: eksperimen dijalankan di Edit Mode saja, supaya dungeon ronde yang sedang dimainkan tidak tertimpa.", this);
            return;
        }

        StringBuilderLog log = new StringBuilderLog("DungeonGenerator eksperimen seed (map " + mapWidth + "x" + mapHeight + ", minLeaf " + minLeafSize + ", margin " + roomMargin + ")");
        log.Line(DungeonMetrics.TableHeader);
        log.Line(DungeonMetrics.TableDivider);

        bool previousBake = bakeNavMesh;
        bool previousLog = logGeneration;
        bakeNavMesh = false;
        logGeneration = false;

        try
        {
            foreach (int seed in experimentSeeds)
            {
                Generate(seed);
                log.Line(Metrics.TableRow());
            }
        }
        finally
        {
            bakeNavMesh = previousBake;
            logGeneration = previousLog;
            Generate(previewSeed);
        }

        log.Line("A = room terakhir, B = terjauh garis lurus, C = terjauh jarak jalan BFS (dipakai). Kunci = jarak BFS Start ke kunci vault. Tanpa bake NavMesh.");
        Debug.Log(log.ToString(), this);
    }

    // Eksperimen 2-4: Min Leaf Size, Room Margin, dan ukuran map. Setiap nilai dirata-rata atas semua Experiment Seeds.
    [ContextMenu("Run BSP Parameter Experiment")]
    private void RunParameterExperiment()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("DungeonGenerator: eksperimen dijalankan di Edit Mode saja, supaya dungeon ronde yang sedang dimainkan tidak tertimpa.", this);
            return;
        }

        int originalLeaf = minLeafSize;
        int originalMaxLeaf = maxLeafSize;
        int originalMargin = roomMargin;
        int originalWidth = mapWidth;
        int originalHeight = mapHeight;
        bool previousBake = bakeNavMesh;
        bool previousLog = logGeneration;

        StringBuilderLog log = new StringBuilderLog("DungeonGenerator eksperimen parameter BSP (rata-rata " + experimentSeeds.Length + " seed, tanpa bake NavMesh)");
        log.Line("| Variabel | Nilai | Valid | Leaf | Room | Luas room rata-rata | Floor % | Jalur Start-Goal | Dead-end | Vault | Objek | Generate + Build ms |");
        log.Line("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        bakeNavMesh = false;
        logGeneration = false;

        try
        {
            foreach (int leaf in experimentLeafSizes)
            {
                minLeafSize = leaf;
                maxLeafSize = Mathf.Max(originalMaxLeaf, leaf);
                log.Line(AverageRow("minLeafSize", leaf.ToString()));
            }

            minLeafSize = originalLeaf;
            maxLeafSize = originalMaxLeaf;

            foreach (int margin in experimentRoomMargins)
            {
                roomMargin = margin;
                log.Line(AverageRow("roomMargin", margin.ToString()));
            }

            roomMargin = originalMargin;

            foreach (Vector2Int size in experimentMapSizes)
            {
                mapWidth = size.x;
                mapHeight = size.y;
                log.Line(AverageRow("map", size.x + " x " + size.y));
            }
        }
        finally
        {
            minLeafSize = originalLeaf;
            maxLeafSize = originalMaxLeaf;
            roomMargin = originalMargin;
            mapWidth = originalWidth;
            mapHeight = originalHeight;
            bakeNavMesh = previousBake;
            logGeneration = previousLog;
            Generate(previewSeed);
        }

        Debug.Log(log.ToString(), this);
    }

    private string AverageRow(string variable, string value)
    {
        int valid = 0;
        float leaves = 0f, roomsSum = 0f, area = 0f, floorPercent = 0f, path = 0f, objects = 0f, time = 0f, deadEnds = 0f;
        int count = 0;
        int vaults = 0;

        foreach (int seed in experimentSeeds)
        {
            Generate(seed);

            DungeonMetrics m = Metrics;
            count++;
            valid += m.valid ? 1 : 0;
            leaves += m.leafCount;
            roomsSum += m.roomCount;
            area += m.averageRoomArea;
            floorPercent += m.FloorPercent;
            path += Mathf.Max(0, m.pathBfs);
            objects += m.builtObjects;
            time += m.generateMs + m.buildMs;
            deadEnds += m.deadEndCount;
            vaults += m.hasVault ? 1 : 0;
        }

        if (count == 0)
            return "| " + variable + " | " + value + " | - |";

        return "| " + variable + " | " + value + " | " + valid + "/" + count +
               " | " + (leaves / count).ToString("0.0") + " | " + (roomsSum / count).ToString("0.0") +
               " | " + (area / count).ToString("0") + " cell | " + (floorPercent / count).ToString("0.0") +
               " | " + (path / count).ToString("0.0") + " cell | " + (deadEnds / count).ToString("0.0") + " | " + vaults + "/" + count +
               " | " + (objects / count).ToString("0") +
               " | " + (time / count).ToString("0.0") + " |";
    }

    private class StringBuilderLog
    {
        private readonly System.Text.StringBuilder builder = new System.Text.StringBuilder();

        public StringBuilderLog(string title)
        {
            builder.Append(title);
        }

        public void Line(string line)
        {
            builder.Append('\n').Append(line);
        }

        public override string ToString()
        {
            return builder.ToString();
        }
    }

    [ContextMenu("Generate Next Seed")]
    private void GenerateNextSeed()
    {
        previewSeed++;
        Generate(previewSeed);
    }

    private void OnValidate()
    {
        maxLeafSize = Mathf.Max(maxLeafSize, minLeafSize);

        if (minRoomSize + roomMargin * 2 > minLeafSize)
            Debug.LogWarning("DungeonGenerator: Min Room Size + 2 x Room Margin lebih besar dari Min Leaf Size, sebagian Leaf tidak akan berisi room.", this);
    }

    // Kuning = Leaf BSP, hijau = room, biru = Start, merah = Goal, putih = batas grid, emas = vault.
    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        DrawCellRect(0, 0, mapWidth, mapHeight, 0.1f);

        if (!HasLayout)
            return;

        if (drawLeaves)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);

            foreach (BSPNode leaf in leafNodes)
                DrawCellRect(leaf.x, leaf.y, leaf.width, leaf.height, wallHeight + 0.15f);
        }

        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);

        foreach (DungeonRoom room in rooms)
            DrawCellRect(room.x, room.y, room.width, room.height, 0.12f);

        Gizmos.color = new Color(0.3f, 0.6f, 1f, 1f);
        Gizmos.DrawSphere(StartWorld + Vector3.up * 0.6f, tileSize * 0.35f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 1f);
        Gizmos.DrawSphere(GoalWorld + Vector3.up * 0.6f, tileSize * 0.35f);

        // Jalur terpendek BFS Start ke Goal (cyan).
        Gizmos.color = new Color(0.3f, 0.95f, 1f, 1f);

        for (int i = 1; i < goalPath.Count; i++)
            Gizmos.DrawLine(CellToWorld(goalPath[i - 1]) + Vector3.up * 0.3f, CellToWorld(goalPath[i]) + Vector3.up * 0.3f);

        // Safe room (biru) dan titik Boss Room (warna role boss).
        if (safeStartRoom && StartRoom != null)
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.9f);
            DrawCellRect(StartRoom.x, StartRoom.y, StartRoom.width, StartRoom.height, 0.2f);
        }

        Gizmos.color = RoleColors.Get(bossRole);

        foreach (Vector3 point in bossPoints)
            Gizmos.DrawWireSphere(point + Vector3.up * 0.5f, tileSize * 0.3f);

        // Room dead-end (oranye tipis), vault (emas), pintu (biru), dan kunci (emas).
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);

        foreach (DungeonRoom room in deadEndRooms)
            DrawCellRect(room.x, room.y, room.width, room.height, 0.25f);

        Gizmos.color = new Color(1f, 0.56f, 0.22f, 1f);

        foreach (Vector3 torch in torchPositions)
            Gizmos.DrawWireSphere(torch, tileSize * 0.15f);

        DrawDecorGizmos();

        if (VaultRoom == null)
            return;

        Gizmos.color = new Color(1f, 0.78f, 0.25f, 1f);
        DrawCellRect(VaultRoom.x, VaultRoom.y, VaultRoom.width, VaultRoom.height, 0.3f);
        Gizmos.DrawWireCube(CellToWorld(vaultKeyCell) + Vector3.up * 0.5f, Vector3.one * tileSize * 0.4f);

        Gizmos.color = new Color(0.31f, 0.64f, 1f, 1f);

        foreach (Vector2Int cell in vaultDoorCells)
            Gizmos.DrawWireCube(CellToWorld(cell) + Vector3.up * 0.5f, new Vector3(tileSize, 1f, tileSize));
    }

    private void DrawCellRect(int x, int y, int width, int height, float lift)
    {
        Vector3 origin = transform.position + Vector3.up * lift;
        Vector3 a = origin + new Vector3(x * tileSize, 0f, y * tileSize);
        Vector3 b = origin + new Vector3((x + width) * tileSize, 0f, y * tileSize);
        Vector3 c = origin + new Vector3((x + width) * tileSize, 0f, (y + height) * tileSize);
        Vector3 d = origin + new Vector3(x * tileSize, 0f, (y + height) * tileSize);

        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }
}
