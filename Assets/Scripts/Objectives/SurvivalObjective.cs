using System;
using System.Collections.Generic;
using UnityEngine;

// Angka Survival, diatur di Inspector ObjectiveRunner.
[Serializable]
public class SurvivalSettings
{
    [Min(1f)] public float duration = 75f;

    [Header("Ritme Spawn")]
    // Naik: 0 sampai Ramp End, satu spawn tiap Ramp Interval.
    [Min(0f)] public float rampEnd = 20f;
    [Min(0.1f)] public float rampInterval = 6f;
    // Puncak: Ramp End sampai Peak End, satu spawn tiap Peak Interval. Sesudahnya Reda, tanpa spawn baru.
    [Min(0f)] public float peakEnd = 60f;
    [Min(0.1f)] public float peakInterval = 3f;
    // Spawn pertama setelah ronde mulai.
    [Min(0f)] public float firstSpawnDelay = 1f;
    // Jeda coba lagi saat batas musuh hidup penuh atau tidak ada posisi valid.
    [Min(0.1f)] public float retryDelay = 1f;
    // Batas musuh hidup = round(nilai ini x faktor DDA).
    [Min(1f)] public float baseMaxAlive = 5f;

    [Header("Lokasi Spawn")]
    // Room spawn berjarak Min Hop sampai Max Hop dari room MC. Tidak ada kandidat = dilebarkan sampai Fallback Max Hop.
    [Min(1)] public int minHop = 1;
    [Min(1)] public int maxHop = 2;
    [Min(1)] public int fallbackMaxHop = 3;
    [Min(0f)] public float minSpawnDistance = 10f;
    [Min(1)] public int placementAttempts = 12;

    [Header("AI")]
    // Musuh yang tidak tahu posisi MC diberi tahu ulang tiap interval ini.
    [Min(0.5f)] public float realertInterval = 5f;
}

// Objective Survival: bertahan sampai timer habis. Musuh tidak muncul sekaligus di awal, tetapi datang bertahap
// dari room tetangga yang tidak terlihat dari MC.
//   Ritme      Naik (interval panjang) -> Puncak (interval pendek) -> Reda (tanpa spawn). Interval dibagi faktor DDA.
//   Batas      musuh hidup paling banyak round(Base Max Alive x faktor DDA).
//   Role       weighted random dari komposisi wave ronde ini (jumlah per role = bobot), stream PCG Objective.
//   Lokasi     room dengan hop 1-2 dari room MC (graf ruangan), pusatnya minimal Min Spawn Distance dari MC dan
//              terhalang dinding (raycast NavBlock). Posisi di dalam room diuji dengan aturan PcgPlacement.Validate,
//              dan titiknya sendiri juga harus tidak terlihat dari MC. Tidak ada kandidat = hop dilebarkan.
//   AI         setelah muncul, musuh diberi tahu posisi MC (Blackboard.Alert). Tiap Realert Interval, musuh yang
//              kehilangan jejak diberi tahu ulang. Brain tidak diubah.
// Selesai saat timer habis. Musuh sisa di-despawn oleh ObjectiveRunner.
public class SurvivalObjective : IWaveObjective
{
    private readonly SurvivalSettings settings;
    private readonly List<EnemyRole> roles = new List<EnemyRole>();
    private readonly List<int> roleWeights = new List<int>();
    private readonly List<int> candidates = new List<int>();
    private readonly List<Vector3> occupied = new List<Vector3>();

    private ObjectiveContext context;
    private float elapsed;
    private float nextSpawn;
    private float nextRealert;
    private float factor = 1f;

    public SurvivalObjective(SurvivalSettings settings)
    {
        this.settings = settings ?? new SurvivalSettings();
    }

    public ObjectiveType Type => ObjectiveType.Survival;

    public bool IsComplete => elapsed >= settings.duration;

    public float Progress01 => Mathf.Clamp01(elapsed / settings.duration);

    public float Remaining => Mathf.Max(0f, settings.duration - elapsed);

    public string HudText => "Bertahan " + UIKit.FormatTime(Remaining);

    public int MaxAlive => Mathf.Max(1, Mathf.RoundToInt(settings.baseMaxAlive * factor));

    public int Spawned { get; private set; }

    public int Failed { get; private set; }

    // Untuk overlay debug: room kandidat terakhir dan titik spawn terakhir.
    public IReadOnlyList<int> LastCandidates => candidates;

    public bool HasLastSpawn { get; private set; }

    public Vector3 LastSpawn { get; private set; }

    public int LastSpawnRoom { get; private set; } = -1;

    public string PhaseLabel => elapsed < settings.rampEnd ? "naik" : elapsed < settings.peakEnd ? "puncak" : "reda";

    public float CurrentInterval =>
        (elapsed < settings.rampEnd ? settings.rampInterval : settings.peakInterval) / Mathf.Max(0.1f, factor);

    // Satu baris untuk panel debug.
    public string DebugStatus =>
        "fase " + PhaseLabel + ", hidup " + Alive + "/" + MaxAlive +
        (elapsed < settings.peakEnd ? ", interval " + CurrentInterval.ToString("0.0") + " s" : ", tanpa spawn") +
        ", muncul " + Spawned + (Failed > 0 ? ", gagal " + Failed : string.Empty) +
        (LastSpawnRoom >= 0 ? ", room terakhir " + LastSpawnRoom : string.Empty);

    private int Alive => context != null && context.Battle != null ? context.Battle.AliveCount : 0;

    public void Begin(ObjectiveContext objectiveContext)
    {
        context = objectiveContext;
        elapsed = 0f;
        nextSpawn = settings.firstSpawnDelay;
        nextRealert = settings.realertInterval;
        Spawned = 0;
        Failed = 0;
        HasLastSpawn = false;
        LastSpawnRoom = -1;

        DifficultyModel dda = DifficultyModel.Current;
        factor = dda != null && dda.Active ? Mathf.Max(0.1f, dda.Factor) : 1f;

        roles.Clear();
        roleWeights.Clear();

        if (context.Wave != null)
        {
            foreach (WaveEntry entry in context.Wave.Entries)
            {
                if (entry.count <= 0)
                    continue;

                roles.Add(entry.role);
                roleWeights.Add(entry.count);
            }
        }

        if (roles.Count == 0)
        {
            roles.Add(EnemyRole.Charger);
            roleWeights.Add(1);
        }
    }

    public void Tick(float deltaTime)
    {
        if (context == null)
            return;

        elapsed += deltaTime;

        if (elapsed < settings.peakEnd && elapsed >= nextSpawn)
        {
            if (Alive < MaxAlive && TrySpawn())
                nextSpawn = elapsed + CurrentInterval;
            else
                nextSpawn = elapsed + settings.retryDelay;
        }

        if (elapsed >= nextRealert)
        {
            nextRealert = elapsed + settings.realertInterval;
            RealertLostEnemies();
        }
    }

    public void End()
    {
        context = null;
    }

    // ---------- Spawn ----------

    private bool TrySpawn()
    {
        PlayerHealth player = PlayerHealth.Current;

        if (player == null || context.Spawner == null || context.Battle == null)
            return false;

        Vector3 playerPosition = player.transform.position;
        EnemyRole role = PickRole();

        if (!TryFindPosition(playerPosition, out Vector3 position, out int room))
        {
            Failed++;
            return false;
        }

        EnemyHealth enemy = context.Spawner.SpawnAt(role, position);

        if (enemy == null)
            return false;

        context.Battle.Track(enemy);

        EnemyController controller = enemy.GetComponent<EnemyController>();

        if (controller != null)
        {
            controller.Blackboard.Alert(playerPosition, player.gameObject);

            if (TacticalCoordinator.Current != null)
                TacticalCoordinator.Current.RegisterLate(controller);
        }

        Spawned++;
        HasLastSpawn = true;
        LastSpawn = position;
        LastSpawnRoom = room;
        return true;
    }

    private EnemyRole PickRole()
    {
        int total = 0;

        foreach (int weight in roleWeights)
            total += weight;

        int pick = context.Random.Range(0, Mathf.Max(1, total));

        for (int i = 0; i < roles.Count; i++)
        {
            pick -= roleWeights[i];

            if (pick < 0)
                return roles[i];
        }

        return roles[roles.Count - 1];
    }

    private bool TryFindPosition(Vector3 playerPosition, out Vector3 position, out int room)
    {
        position = Vector3.zero;
        room = -1;

        DungeonGenerator dungeon = context.Dungeon;
        DungeonRoomGraph graph = dungeon != null ? dungeon.RoomGraph : null;

        CollectOccupied();

        if (graph == null)
            return TryFindInArea(playerPosition, out position);

        int playerRoom = NearestRoom(graph, playerPosition);

        if (playerRoom < 0)
            return false;

        CollectCandidates(graph, playerRoom, playerPosition, settings.maxHop);

        if (candidates.Count == 0 && settings.fallbackMaxHop > settings.maxHop)
            CollectCandidates(graph, playerRoom, playerPosition, settings.fallbackMaxHop);

        // Urutan kandidat diacak ber-seed, lalu dicoba satu per satu.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = context.Random.Range(0, i + 1);
            int swap = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = swap;
        }

        foreach (int candidate in candidates)
        {
            if (TryPlaceInRoom(dungeon, graph.Room(candidate), playerPosition, out position))
            {
                room = candidate;
                return true;
            }
        }

        return false;
    }

    private void CollectCandidates(DungeonRoomGraph graph, int playerRoom, Vector3 playerPosition, int maxHop)
    {
        candidates.Clear();

        DungeonGenerator dungeon = graph.Dungeon;
        DungeonVault vault = DungeonVault.Active;
        bool vaultClosed = vault != null && (vault.Door == null || !vault.Door.IsOpen);
        float minSqr = settings.minSpawnDistance * settings.minSpawnDistance;
        Vector3 eye = playerPosition + Vector3.up * 1.2f;

        for (int r = 0; r < graph.RoomCount; r++)
        {
            int hop = graph.HopDistance(playerRoom, r);

            if (hop < settings.minHop || hop > maxHop)
                continue;

            // Vault yang masih terkunci tidak bisa ditinggalkan musuh.
            if (vaultClosed && graph.Room(r) == dungeon.VaultRoom)
                continue;

            Vector3 center = graph.Center(r);
            Vector3 offset = center - playerPosition;
            offset.y = 0f;

            if (offset.sqrMagnitude < minSqr)
                continue;

            if (!Hidden(eye, center))
                continue;

            candidates.Add(r);
        }
    }

    private bool TryPlaceInRoom(DungeonGenerator dungeon, DungeonRoom room, Vector3 playerPosition, out Vector3 position)
    {
        position = Vector3.zero;

        PcgSpawnRules rules = context.Spawner.ActiveRules;
        Vector3 eye = playerPosition + Vector3.up * 1.2f;
        float minSqr = settings.minSpawnDistance * settings.minSpawnDistance;
        // Cell di tepi room dihindari jika room cukup besar, supaya musuh tidak muncul menempel dinding.
        int inset = room.width > 2 && room.height > 2 ? 1 : 0;

        for (int attempt = 0; attempt < settings.placementAttempts; attempt++)
        {
            Vector2Int cell = new Vector2Int(
                context.Random.Range(room.x + inset, room.x + room.width - inset),
                context.Random.Range(room.y + inset, room.y + room.height - inset));

            if (!dungeon.IsWalkable(cell))
                continue;

            float jitter = dungeon.TileSize * 0.35f;
            Vector3 candidate = dungeon.CellToWorld(cell) + new Vector3(context.Random.Range(-jitter, jitter), 0f, context.Random.Range(-jitter, jitter));

            if (!PcgPlacement.TrySnapToNavMesh(candidate, rules, out candidate))
                continue;

            Vector3 offset = candidate - playerPosition;
            offset.y = 0f;

            if (offset.sqrMagnitude < minSqr || !Hidden(eye, candidate + Vector3.up * 1f))
                continue;

            if (PcgPlacement.Validate(candidate, rules, playerPosition, occupied) != PcgReject.None)
                continue;

            position = candidate;
            return true;
        }

        return false;
    }

    // Mode Arena (tanpa graf): generate-and-test di area spawn, titik harus jauh dan tidak terlihat dari MC.
    private bool TryFindInArea(Vector3 playerPosition, out Vector3 position)
    {
        position = Vector3.zero;
        PcgSpawnArea area = context.Spawner.CurrentArea;

        if (area == null)
            return false;

        PcgSpawnRules rules = context.Spawner.ActiveRules;
        PcgPlacementReport report = new PcgPlacementReport();
        Vector3 eye = playerPosition + Vector3.up * 1.2f;

        for (int attempt = 0; attempt < settings.placementAttempts; attempt++)
        {
            if (!PcgPlacement.TryPlace(area, rules, context.Random, playerPosition, occupied, report, out Vector3 candidate))
                return false;

            Vector3 offset = candidate - playerPosition;
            offset.y = 0f;

            if (offset.magnitude >= settings.minSpawnDistance && Hidden(eye, candidate + Vector3.up * 1f))
            {
                position = candidate;
                return true;
            }

            occupied.Remove(candidate);
        }

        return false;
    }

    // Room MC. Di koridor dipakai room terdekat (jarak datar ke pusat room).
    private static int NearestRoom(DungeonRoomGraph graph, Vector3 position)
    {
        int room = graph.RoomOf(position);

        if (room >= 0)
            return room;

        float best = float.MaxValue;

        for (int r = 0; r < graph.RoomCount; r++)
        {
            Vector3 offset = graph.Center(r) - position;
            offset.y = 0f;

            if (offset.sqrMagnitude < best)
            {
                best = offset.sqrMagnitude;
                room = r;
            }
        }

        return room;
    }

    // Garis dari mata MC ke titik terhalang dinding atau props penghalang (layer NavBlock).
    private static bool Hidden(Vector3 eye, Vector3 target)
    {
        Vector3 lifted = new Vector3(target.x, Mathf.Max(target.y, eye.y - 0.2f), target.z);
        return Physics.Linecast(eye, lifted, GameLayers.ObstacleMask, QueryTriggerInteraction.Ignore);
    }

    private void CollectOccupied()
    {
        occupied.Clear();

        if (context.Battle == null)
            return;

        foreach (EnemyHealth enemy in context.Battle.Alive)
        {
            if (enemy != null)
                occupied.Add(enemy.transform.position);
        }
    }

    // ---------- AI ----------

    private void RealertLostEnemies()
    {
        PlayerHealth player = PlayerHealth.Current;

        if (player == null || context.Battle == null)
            return;

        foreach (EnemyHealth enemy in context.Battle.Alive)
        {
            EnemyController controller = enemy != null ? enemy.GetComponent<EnemyController>() : null;

            if (controller != null && !controller.Blackboard.IsAware)
                controller.Blackboard.Alert(player.transform.position, player.gameObject);
        }
    }
}
