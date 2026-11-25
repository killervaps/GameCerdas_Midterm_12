using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Minimap Mode Dungeon (utara tetap, fog of war).
//   - Peta digambar dari DungeonGenerator.Grid ke Texture2D, 1 piksel per sel dengan filter Point, lalu dibesarkan di UI.
//     Tekstur dibuat ulang setiap dungeon baru (grid baru).
//   - Peta lokal di kanan atas: kotak tetap berisi sekitar Cells Across sel di sekitar MC. Peta bergeser supaya MC di
//     tengah, tidak berputar (+Z dunia selalu ke atas). Tombol M membuka peta penuh di tengah layar beserta legenda.
//   - Fog of war: room terbuka seluruhnya (beserta dinding di sekelilingnya) saat MC masuk, sel koridor terbuka
//     dalam radius Corridor Reveal Radius dari MC. Saat AI Debug Mode (F1) aktif, fog dimatikan untuk demo dan debug.
//   - Ikon: MC (panah sesuai arah hadap + kerucut arah kamera), musuh dalam Enemy Radius (warna role), musuh yang
//     sedang di-reveal (merah), kunci dan vault (setelah selnya terbuka), Goal yang terbuka (selalu, ditempel di
//     tepi jika di luar peta lokal), dan zona Hold Zone.
// Penanda tepi layar milik waypoint lain menghindari kotak minimap lewat WaypointProjection.SetReserved.
public class Minimap : UIScreen
{
    [Header("Peta lokal")]
    [SerializeField] private float mapSize = 200f;
    [SerializeField, Min(8f)] private float cellsAcross = 26f;
    [SerializeField] private Vector2 screenMargin = new Vector2(28f, 28f);

    [Header("Fog of war")]
    [SerializeField, Min(1)] private int corridorRevealRadius = 6;
    [SerializeField, Min(0.02f)] private float fogInterval = 0.1f;

    [Header("Ikon")]
    [SerializeField, Min(1f)] private float enemyRadius = 15f;
    [SerializeField] private float playerIconSize = 18f;
    [SerializeField] private float enemyIconSize = 10f;
    // Panjang kerucut arah kamera, dalam sel.
    [SerializeField] private float coneLengthCells = 8f;

    [Header("Peta penuh (M)")]
    [SerializeField] private Vector2 expandedSize = new Vector2(760f, 600f);

    [Header("Orientasi")]
    // Peta diputar mengikuti yaw kamera top-down supaya atas peta = atas layar. Dipakai jika TopDownCameraFollow
    // tidak ditemukan. Di scene arena kamera top-down ber-yaw 180 (melihat ke -Z dunia).
    [SerializeField] private float fallbackYaw = 180f;

    private const float FramePadding = 6f;

    // Warna piksel peta.
    private static readonly Color32 FogColor = new Color32(10, 12, 16, 255);
    private static readonly Color32 WallColor = new Color32(43, 51, 74, 255);
    private static readonly Color32 RoomColor = new Color32(143, 151, 170, 255);
    private static readonly Color32 CorridorColor = new Color32(110, 119, 144, 255);

    private static readonly Color RevealColor = new Color(1f, 0.33f, 0.33f);

    private enum IconKind
    {
        Player,
        Cone,
        Enemy,
        Revealed,
        Key,
        Vault,
        Goal,
        Zone
    }

    private struct Icon
    {
        public IconKind kind;
        public Vector2 cell;
        public Color color;
        public float angle;
        // Untuk zona: radius dalam sel.
        public float radiusCells;
        // Goal ditempel di tepi peta lokal jika di luar jangkauan.
        public bool pinToEdge;
    }

    private const int KindCount = 8;

    // Urutan lapisan ikon dari bawah ke atas. Jenis yang lebih belakang digambar di atas.
    private static readonly IconKind[] LayerOrder =
    {
        IconKind.Zone, IconKind.Vault, IconKind.Key, IconKind.Goal, IconKind.Cone, IconKind.Enemy, IconKind.Revealed, IconKind.Player
    };

    // Satu tampilan peta (lokal atau penuh): viewport bermasker, gambar peta, lalu satu lapisan per jenis ikon
    // dengan pool yang dipakai ulang. Array diindeks (int)IconKind supaya tidak ada alokasi per frame.
    private class MapView
    {
        public RectTransform viewport;
        // Wadah peta + ikon yang diputar sesuai yaw kamera top-down.
        public RectTransform rotator;
        // Token "U" di tepi viewport yang menunjuk utara dunia (+Z).
        public RectTransform north;
        public RawImage map;
        public readonly RectTransform[] layers = new RectTransform[KindCount];
        public readonly List<RectTransform>[] pools = new List<RectTransform>[KindCount];
        public readonly int[] used = new int[KindCount];
    }

    public static Minimap Current { get; private set; }

    public bool IsExpanded { get; private set; }

    public int RoomCount => roomRevealed != null ? roomRevealed.Length : 0;

    public int RevealedRooms { get; private set; }

    private RectTransform frame;
    private MapView localView;
    private MapView fullView;
    private RectTransform expandedRoot;
    private CanvasGroup expandedGroup;

    private DungeonGenerator dungeon;
    private TileType[,] builtGrid;
    private Texture2D texture;
    private Color32[] pixels;
    private bool[] revealed;
    private bool[] roomRevealed;
    private int width;
    private int height;
    private bool textureDirty;
    private bool fogOff;
    private float nextFog;
    private TopDownCameraFollow cameraRig;
    private bool cameraRigSearched;

    private readonly List<Icon> iconList = new List<Icon>();

    private static Sprite coneSprite;

    protected override void Build()
    {
        Current = this;

        // ---------- Peta lokal (kanan atas) ----------
        Image panel = UIKit.CreatePanel("Frame", rect, UIKit.Panel);
        frame = panel.rectTransform;
        float frameSize = mapSize + FramePadding * 2f;
        UIKit.Place(frame, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-screenMargin.x, -screenMargin.y), new Vector2(frameSize, frameSize));

        localView = CreateView(frame, new Vector2(mapSize, mapSize), 26f);

        Image tab = UIKit.CreateFreeStamp("Tab", frame, "Peta", 18f, UIKit.Cream, -4f, out _);
        PlaceTab(tab.rectTransform, new Vector2(0f, 1f), new Vector2(-8f, 4f));

        Image hint = UIKit.CreateFreeStamp("Hint", frame, "M perbesar", 14f, UIKit.PanelLight, 0f, out TextMeshProUGUI hintLabel);
        hintLabel.color = UIKit.Text;
        PlaceTab(hint.rectTransform, new Vector2(1f, 1f), new Vector2(8f, 4f));

        BuildExpanded();
    }

    private static void PlaceTab(RectTransform tab, Vector2 corner, Vector2 offset)
    {
        tab.anchorMin = tab.anchorMax = corner;
        tab.pivot = new Vector2(corner.x, 0.5f);
        tab.anchoredPosition = new Vector2(corner.x > 0.5f ? -offset.x : offset.x, offset.y);
    }

    // Peta penuh di tengah layar: panel besar, peta seluruh dungeon, dan legenda ikon di bawahnya.
    private void BuildExpanded()
    {
        Image panel = UIKit.CreatePanel("Expanded", rect, UIKit.Panel);
        expandedRoot = panel.rectTransform;
        UIKit.Place(expandedRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), expandedSize);
        expandedGroup = panel.gameObject.AddComponent<CanvasGroup>();
        expandedGroup.blocksRaycasts = false;
        expandedGroup.interactable = false;

        Image tab = UIKit.CreateFreeStamp("Tab", expandedRoot, "Peta dungeon", 26f, UIKit.Cream, -3f, out _);
        UIKit.SetShadowDistance(tab, new Vector2(5f, -6f));
        PlaceTab(tab.rectTransform, new Vector2(0f, 1f), new Vector2(40f, 2f));

        const float legendHeight = 64f;
        Vector2 viewSize = expandedSize - new Vector2(56f, 56f + legendHeight);
        fullView = CreateView(expandedRoot, viewSize, 32f);
        fullView.viewport.anchoredPosition = new Vector2(0f, legendHeight * 0.5f);

        TextMeshProUGUI legend = UIKit.CreateText("Legend", expandedRoot,
            "<color=#f4f1e8>▲</color> kamu   " +
            "<color=#" + ColorUtility.ToHtmlStringRGB(RoleColors.Get(EnemyRole.Charger)) + ">●</color> musuh (warna role)   " +
            "<color=#ff5555>●</color> musuh di-reveal   " +
            "<color=" + UIKit.AccentHex + ">◊</color> kunci   " +
            "<color=#f4f1e8>■</color> vault   " +
            "<color=" + UIKit.AccentHex + ">○</color> Goal   " +
            "<color=#ffcc33>○</color> zona\n" +
            "<color=" + UIKit.MutedHex + ">U = utara. Atas peta = atas layar. Area gelap belum dijelajahi.   M atau Esc untuk menutup.</color>",
            17f, UIKit.Text, TextAlignmentOptions.Center);
        UIKit.Place(legend.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(expandedSize.x - 56f, legendHeight));

        SetExpandedImmediate(false);
    }

    private MapView CreateView(RectTransform parent, Vector2 size, float northSize)
    {
        MapView view = new MapView();

        view.viewport = UIKit.CreateRect("Viewport", parent);
        UIKit.Place(view.viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        view.viewport.gameObject.AddComponent<RectMask2D>();

        // Latar fog untuk area di luar tekstur.
        Image back = UIKit.CreateImage("Back", view.viewport, new Color32(10, 12, 16, 255), false);
        UIKit.Stretch(back.rectTransform);

        view.rotator = UIKit.CreateRect("Rotator", view.viewport);
        view.rotator.anchorMin = view.rotator.anchorMax = new Vector2(0.5f, 0.5f);
        view.rotator.sizeDelta = Vector2.zero;

        // Token utara di luar viewport (saudara viewport, jadi tidak terpotong masker).
        Image north = UIKit.CreateToken("North", parent, northSize, UIKit.PanelLight, "U", out TextMeshProUGUI northLabel);
        northLabel.color = UIKit.Text;
        view.north = north.rectTransform;
        view.north.anchorMin = view.north.anchorMax = new Vector2(0.5f, 0.5f);
        view.north.sizeDelta = new Vector2(northSize, northSize);

        RectTransform mapRect = UIKit.CreateRect("Map", view.rotator);
        mapRect.anchorMin = mapRect.anchorMax = new Vector2(0.5f, 0.5f);
        mapRect.pivot = Vector2.zero;
        view.map = mapRect.gameObject.AddComponent<RawImage>();
        view.map.raycastTarget = false;

        foreach (IconKind kind in LayerOrder)
        {
            RectTransform layer = UIKit.CreateRect(kind + "Icons", view.rotator);
            layer.anchorMin = layer.anchorMax = new Vector2(0.5f, 0.5f);
            layer.sizeDelta = Vector2.zero;
            view.layers[(int)kind] = layer;
            view.pools[(int)kind] = new List<RectTransform>();
        }

        return view;
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;

        if (texture != null)
            Destroy(texture);
    }

    protected override void OnHide()
    {
        WaypointProjection.ClearReserved();
        SetExpandedImmediate(false);
    }

    public void SetExpanded(bool expanded)
    {
        IsExpanded = expanded;
    }

    private void SetExpandedImmediate(bool expanded)
    {
        IsExpanded = expanded;

        if (expandedGroup != null)
            expandedGroup.alpha = expanded ? 1f : 0f;
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame && (ui == null || !ui.IsPaused))
            IsExpanded = !IsExpanded;

        expandedGroup.alpha = Mathf.MoveTowards(expandedGroup.alpha, IsExpanded ? 1f : 0f, 10f * Time.unscaledDeltaTime);

        UpdateReservedArea();

        if (!EnsureTexture())
            return;

        if (Time.unscaledTime >= nextFog)
        {
            nextFog = Time.unscaledTime + fogInterval;
            UpdateFog();
        }

        if (textureDirty)
        {
            textureDirty = false;
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        PlayerHealth player = PlayerHealth.Current;
        Vector2 center = player != null ? WorldToCellF(player.transform.position) : new Vector2(width * 0.5f, height * 0.5f);

        CollectIcons(player);

        float yaw = MapYaw();
        float localScale = mapSize / cellsAcross;
        DrawView(localView, center, localScale, mapSize * 0.5f - 8f, yaw);

        if (expandedGroup.alpha > 0.001f)
        {
            // Yaw 90/270 menukar lebar dan tinggi peta di layar.
            bool sideways = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.7f;
            Vector2 viewSize = fullView.viewport.rect.size;
            float mapW = sideways ? height : width;
            float mapH = sideways ? width : height;
            float fullScale = Mathf.Min(viewSize.x / mapW, viewSize.y / mapH);
            DrawView(fullView, new Vector2(width * 0.5f, height * 0.5f), fullScale, -1f, yaw);
        }
    }

    // Kotak minimap dalam koordinat layar ini (titik tengah canvas = 0,0), supaya penanda tepi waypoint lain menghindar.
    private void UpdateReservedArea()
    {
        Vector2 half = rect.rect.size * 0.5f;
        float size = frame.rect.width;
        const float padding = 16f;
        Rect area = new Rect(half.x - screenMargin.x - size - padding, half.y - screenMargin.y - size - padding,
            size + padding * 2f, size + padding * 2f);
        WaypointProjection.SetReserved(area);
    }

    // ---------- Tekstur dan fog ----------

    // Membuat ulang tekstur saat dungeon baru (grid baru). False jika belum ada dungeon.
    private bool EnsureTexture()
    {
        if (dungeon == null)
            dungeon = ui != null && ui.Run != null ? ui.Run.Dungeon : null;

        TileType[,] grid = dungeon != null ? dungeon.Grid : null;

        if (grid == null)
            return false;

        if (grid == builtGrid && texture != null)
            return true;

        builtGrid = grid;
        width = grid.GetLength(0);
        height = grid.GetLength(1);

        if (texture == null || texture.width != width || texture.height != height)
        {
            if (texture != null)
                Destroy(texture);

            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Minimap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            pixels = new Color32[width * height];
            revealed = new bool[width * height];
        }

        System.Array.Clear(revealed, 0, revealed.Length);
        roomRevealed = new bool[dungeon.Rooms.Count];
        RevealedRooms = 0;
        fogOff = false;

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = FogColor;

        textureDirty = true;
        localView.map.texture = texture;
        fullView.map.texture = texture;
        return true;
    }

    private void UpdateFog()
    {
        bool debugAll = AIDebugSettings.Enabled;

        if (debugAll != fogOff)
        {
            fogOff = debugAll;
            RepaintAll();
        }

        PlayerHealth player = PlayerHealth.Current;
        DungeonRoomGraph graph = dungeon.RoomGraph;

        if (player == null || graph == null)
            return;

        Vector3 position = player.transform.position;
        int room = graph.RoomOf(position);

        if (room >= 0 && room < roomRevealed.Length && !roomRevealed[room])
        {
            roomRevealed[room] = true;
            RevealedRooms++;
            DungeonRoom data = graph.Room(room);
            RevealRect(data.x - 1, data.y - 1, data.x + data.width, data.y + data.height);
        }

        // Koridor dalam radius dari MC. Sel room tidak dibuka di sini, room hanya terbuka saat dimasuki.
        Vector2Int center = dungeon.WorldToCell(position);
        int radius = corridorRevealRadius;
        int radiusSquared = radius * radius;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radiusSquared)
                    continue;

                int x = center.x + dx;
                int y = center.y + dy;

                if (!Inside(x, y) || builtGrid[x, y] == TileType.Wall || graph.RoomAt(new Vector2Int(x, y)) >= 0)
                    continue;

                // Sel koridor dan dinding di sekelilingnya.
                RevealRect(x - 1, y - 1, x + 1, y + 1);
            }
        }
    }

    // Membuka semua sel dalam persegi [x0..x1] x [y0..y1] (inklusif).
    private void RevealRect(int x0, int y0, int x1, int y1)
    {
        for (int y = Mathf.Max(0, y0); y <= Mathf.Min(height - 1, y1); y++)
        {
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(width - 1, x1); x++)
            {
                int index = x + y * width;

                if (revealed[index])
                    continue;

                revealed[index] = true;

                if (!fogOff)
                {
                    pixels[index] = CellColor(x, y);
                    textureDirty = true;
                }
            }
        }
    }

    private void RepaintAll()
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = x + y * width;
                pixels[index] = fogOff || revealed[index] ? CellColor(x, y) : FogColor;
            }
        }

        textureDirty = true;
    }

    // Dinding hanya digambar jika bersebelahan dengan lantai, supaya batu padat di luar dungeon tetap gelap.
    private Color32 CellColor(int x, int y)
    {
        if (builtGrid[x, y] != TileType.Wall)
            return dungeon.RoomGraph != null && dungeon.RoomGraph.RoomAt(new Vector2Int(x, y)) >= 0 ? RoomColor : CorridorColor;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;

                if (Inside(nx, ny) && builtGrid[nx, ny] != TileType.Wall)
                    return WallColor;
            }
        }

        return FogColor;
    }

    private bool Inside(int x, int y)
    {
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    private bool IsRevealed(Vector2 cell)
    {
        if (fogOff)
            return true;

        int x = Mathf.FloorToInt(cell.x);
        int y = Mathf.FloorToInt(cell.y);
        return Inside(x, y) && revealed[x + y * width];
    }

    // Posisi dunia ke koordinat sel pecahan (0,0 = sudut kiri bawah grid).
    private Vector2 WorldToCellF(Vector3 world)
    {
        Vector3 local = world - dungeon.transform.position;
        return new Vector2(local.x / dungeon.TileSize, local.z / dungeon.TileSize);
    }

    // ---------- Ikon ----------

    private void CollectIcons(PlayerHealth player)
    {
        iconList.Clear();

        // Hold Zone paling bawah supaya ikon lain tetap terlihat di atas cincinnya.
        ObjectiveRunner runner = ObjectiveRunner.Current;

        if (runner != null && runner.Active is HoldZoneObjective holdZone && holdZone.Zone != null)
        {
            iconList.Add(new Icon
            {
                kind = IconKind.Zone,
                cell = WorldToCellF(holdZone.Zone.transform.position),
                color = UIKit.ThreatColor(ThreatLevel.Medium),
                radiusCells = holdZone.Zone.Radius / dungeon.TileSize
            });
        }

        DungeonVault vault = DungeonVault.Active;

        if (vault != null && vault.State != VaultState.Looted && vault.Room != null)
        {
            Vector2 vaultCell = (Vector2)vault.Room.Center + new Vector2(0.5f, 0.5f);

            if (IsRevealed(vaultCell))
                iconList.Add(new Icon { kind = IconKind.Vault, cell = vaultCell, color = UIKit.Cream });

            if (vault.State == VaultState.Locked)
            {
                Vector2 keyCell = WorldToCellF(vault.KeyPosition);

                if (IsRevealed(keyCell))
                    iconList.Add(new Icon { kind = IconKind.Key, cell = keyCell, color = UIKit.Accent });
            }
        }

        DungeonGoalPortal portal = ui != null && ui.Run != null ? ui.Run.OpenPortal : null;

        if (portal != null)
            iconList.Add(new Icon { kind = IconKind.Goal, cell = WorldToCellF(portal.transform.position), color = UIKit.Accent, pinToEdge = true });

        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        float radiusSquared = enemyRadius * enemyRadius;
        IReadOnlyList<EnemyController> enemies = EnemyController.Active;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyController enemy = enemies[i];

            if (enemy == null || enemy.Health == null || enemy.Health.IsDead || player == null)
                continue;

            Vector3 offset = enemy.transform.position - origin;
            offset.y = 0f;

            if (offset.sqrMagnitude > radiusSquared)
                continue;

            iconList.Add(new Icon { kind = IconKind.Enemy, cell = WorldToCellF(enemy.transform.position), color = RoleColors.Get(enemy.Role) });
        }

        EnemyGroupWaypoint waypoint = EnemyGroupWaypoint.Current;
        IReadOnlyList<EnemyHealth> revealedEnemies = waypoint != null ? waypoint.RevealedEnemies : null;

        if (revealedEnemies != null)
        {
            for (int i = 0; i < revealedEnemies.Count; i++)
            {
                EnemyHealth enemy = revealedEnemies[i];

                if (enemy != null && !enemy.IsDead)
                    iconList.Add(new Icon { kind = IconKind.Revealed, cell = WorldToCellF(enemy.transform.position), color = RevealColor });
            }
        }

        if (player == null)
            return;

        Vector2 playerCell = WorldToCellF(player.transform.position);
        Camera cam = Camera.main;
        TopDownCameraFollow rig = CameraRig();

        // Kerucut arah kamera hanya untuk kamera third-person. Kamera top-down tidak berputar, jadi kerucutnya
        // selalu menunjuk ke atas peta dan tidak memberi informasi.
        if (cam != null && rig != null && rig.Mode == GameplayCameraMode.ThirdPerson)
        {
            Vector3 look = cam.transform.forward;
            look.y = 0f;

            if (look.sqrMagnitude > 0.0001f)
                iconList.Add(new Icon { kind = IconKind.Cone, cell = playerCell, color = new Color(UIKit.Cream.r, UIKit.Cream.g, UIKit.Cream.b, 0.22f), angle = UpAngle(look) });
        }

        Vector3 facing = player.transform.forward;
        facing.y = 0f;
        iconList.Add(new Icon { kind = IconKind.Player, cell = playerCell, color = UIKit.Cream, angle = UpAngle(facing) });
    }

    // Sudut rotasi UI (derajat, berlawanan jarum jam) supaya sprite yang menunjuk ke atas menunjuk ke arah dunia ini.
    // Utara tetap: +Z dunia = atas peta, +X dunia = kanan peta.
    private static float UpAngle(Vector3 direction)
    {
        return Mathf.Atan2(-direction.x, direction.z) * Mathf.Rad2Deg;
    }

    // Yaw kamera top-down (derajat). Peta diputar sebesar ini supaya arah pandang kamera top-down = atas peta.
    private float MapYaw()
    {
        TopDownCameraFollow rig = CameraRig();
        return rig != null ? rig.Yaw : fallbackYaw;
    }

    private TopDownCameraFollow CameraRig()
    {
        if (cameraRig == null && !cameraRigSearched)
        {
            cameraRigSearched = true;
            cameraRig = FindAnyObjectByType<TopDownCameraFollow>();
        }

        return cameraRig;
    }

    private void DrawView(MapView view, Vector2 centerCell, float cellPixels, float edgeLimit, float yaw)
    {
        // Peta dan ikon digambar dalam koordinat dunia (+Z ke atas), lalu wadahnya diputar sebesar yaw
        // (berlawanan jarum jam) supaya arah pandang kamera top-down jatuh di atas peta.
        view.rotator.localRotation = Quaternion.Euler(0f, 0f, yaw);
        view.map.rectTransform.sizeDelta = new Vector2(width * cellPixels, height * cellPixels);
        view.map.rectTransform.anchoredPosition = -centerCell * cellPixels;

        // Token U di tepi viewport, searah utara dunia (+Z) setelah diputar.
        float radians = yaw * Mathf.Deg2Rad;
        Vector2 northDirection = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
        Vector2 half = view.viewport.rect.size * 0.5f;
        float edge = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(northDirection.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(northDirection.y)));
        view.north.anchoredPosition = view.viewport.anchoredPosition + northDirection * edge;

        System.Array.Clear(view.used, 0, KindCount);

        for (int i = 0; i < iconList.Count; i++)
        {
            Icon icon = iconList[i];
            Vector2 position = (icon.cell - centerCell) * cellPixels;

            if (icon.pinToEdge && edgeLimit > 0f)
            {
                float overflow = Mathf.Max(Mathf.Abs(position.x), Mathf.Abs(position.y)) / edgeLimit;

                if (overflow > 1f)
                    position /= overflow;
            }

            RectTransform target = Take(view, icon.kind);
            target.anchoredPosition = position;
            Style(target, icon, cellPixels);
        }

        for (int k = 0; k < KindCount; k++)
        {
            List<RectTransform> pool = view.pools[k];

            for (int i = view.used[k]; i < pool.Count; i++)
            {
                if (pool[i].gameObject.activeSelf)
                    pool[i].gameObject.SetActive(false);
            }
        }
    }

    private void Style(RectTransform target, Icon icon, float cellPixels)
    {
        Image fill = target.GetChild(target.childCount - 1).GetComponent<Image>();

        switch (icon.kind)
        {
            case IconKind.Player:
                target.sizeDelta = new Vector2(playerIconSize, playerIconSize);
                target.localRotation = Quaternion.Euler(0f, 0f, icon.angle);
                break;
            case IconKind.Cone:
                float length = coneLengthCells * cellPixels;
                target.sizeDelta = new Vector2(length * 2f, length * 2f);
                target.localRotation = Quaternion.Euler(0f, 0f, icon.angle);
                break;
            case IconKind.Zone:
                float diameter = Mathf.Max(16f, icon.radiusCells * 2f * cellPixels);
                target.sizeDelta = new Vector2(diameter, diameter);
                break;
            case IconKind.Revealed:
                target.sizeDelta = new Vector2(enemyIconSize + 2f, enemyIconSize + 2f);
                break;
            case IconKind.Enemy:
                target.sizeDelta = new Vector2(enemyIconSize, enemyIconSize);
                break;
            default:
                target.sizeDelta = new Vector2(14f, 14f);
                break;
        }

        fill.color = icon.color;
    }

    // Ikon berikutnya dari pool jenis ini, di lapisan jenisnya. Ikon dibuat sekali lalu dipakai ulang.
    private static RectTransform Take(MapView view, IconKind kind)
    {
        int k = (int)kind;
        List<RectTransform> pool = view.pools[k];
        int index = view.used[k]++;

        if (index < pool.Count)
        {
            if (!pool[index].gameObject.activeSelf)
                pool[index].gameObject.SetActive(true);

            return pool[index];
        }

        RectTransform created = CreateIcon(view.layers[k], kind);
        pool.Add(created);
        return created;
    }

    // Ikon = wadah berisi lapisan tinta (opsional) lalu lapisan warna sebagai anak terakhir.
    private static RectTransform CreateIcon(RectTransform parent, IconKind kind)
    {
        RectTransform root = UIKit.CreateRect(kind.ToString(), parent);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);

        switch (kind)
        {
            case IconKind.Player:
                Layer(root, ReticleSprites.TriangleInk, UIKit.Ink, 0f);
                Layer(root, ReticleSprites.Triangle, UIKit.Cream, 0f);
                // Sprite segitiga menunjuk ke bawah, diputar supaya menunjuk ke atas (rotasi ikon diatur per frame).
                foreach (Transform child in root)
                    child.localRotation = Quaternion.Euler(0f, 0f, 180f);
                break;
            case IconKind.Cone:
                Layer(root, ConeSprite, Color.white, 0f);
                break;
            case IconKind.Enemy:
            case IconKind.Revealed:
                Layer(root, UIKit.Circle, UIKit.Ink, -2f);
                Layer(root, UIKit.Circle, Color.white, 0f);
                break;
            case IconKind.Key:
                Layer(root, ReticleSprites.DiamondInk, UIKit.Ink, 0f);
                Layer(root, ReticleSprites.Diamond, Color.white, 0f);
                break;
            case IconKind.Vault:
                Image vaultImage = Layer(root, UIKit.InkSmall, Color.white, 0f);
                vaultImage.type = Image.Type.Sliced;
                break;
            case IconKind.Goal:
                Layer(root, UIKit.Circle, UIKit.Ink, -1f);
                Layer(root, UIKit.Ring, Color.white, 0f);
                break;
            case IconKind.Zone:
                Layer(root, ReticleSprites.LongDashRing, Color.white, 0f);
                break;
        }

        return root;
    }

    // inset negatif = lapisan lebih besar dari wadah (garis tinta di sekeliling titik).
    private static Image Layer(RectTransform root, Sprite sprite, Color color, float inset)
    {
        Image image = UIKit.CreateImage("Layer", root, color, false);
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        UIKit.Stretch(image.rectTransform, inset, inset, inset, inset);
        return image;
    }

    // Kerucut arah kamera: irisan lingkaran ±25° yang menunjuk ke atas dari titik tengah, memudar ke ujungnya.
    private static Sprite ConeSprite
    {
        get
        {
            if (coneSprite != null)
                return coneSprite;

            const int size = 128;
            Texture2D cone = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Minimap_Cone",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            Color32[] data = new Color32[size * size];
            float half = size * 0.5f;
            const float halfAngle = 25f * Mathf.Deg2Rad;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Abs(Mathf.Atan2(dx, dy));
                    float alpha = dy > 0f && radius <= 1f && angle <= halfAngle ? 1f - radius : 0f;
                    data[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            cone.SetPixels32(data);
            cone.Apply();
            coneSprite = Sprite.Create(cone, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            coneSprite.name = "Minimap_Cone";
            coneSprite.hideFlags = HideFlags.DontSave;
            return coneSprite;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }
}
