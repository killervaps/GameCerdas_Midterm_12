using System.Collections.Generic;
using UnityEngine;

// Spawn musuh di dalam room dungeon untuk objective (Survival, Hold Zone). Dipakai bersama supaya aturannya sama:
//   - titik acak ber-seed di cell lantai room (tepi room dihindari jika room cukup besar)
//   - dipindah ke NavMesh, lalu diuji aturan PcgPlacement.Validate (jarak MC, obstacle, jarak antar musuh, jalur)
//   - opsional: minimal sekian meter dari MC dan tidak terlihat dari MC (garis ke NavBlock terhalang)
// Setelah muncul, musuh didaftarkan ke BattleManager dan TacticalCoordinator.
public static class RoomSpawner
{
    private static readonly List<Vector3> occupied = new List<Vector3>();

    // Posisi musuh hidup ronde ini, supaya musuh baru tidak menempel musuh lain (Rule 3).
    public static List<Vector3> CollectOccupied(BattleManager battle)
    {
        occupied.Clear();

        if (battle == null)
            return occupied;

        foreach (EnemyHealth enemy in battle.Alive)
        {
            if (enemy != null)
                occupied.Add(enemy.transform.position);
        }

        return occupied;
    }

    public static bool TryPlaceInRoom(ObjectiveContext context, DungeonRoom room, Vector3 playerPosition,
        float minDistance, bool requireHidden, int attempts, List<Vector3> taken, out Vector3 position)
    {
        position = Vector3.zero;

        DungeonGenerator dungeon = context.Dungeon;

        if (dungeon == null || context.Spawner == null)
            return false;

        PcgSpawnRules rules = context.Spawner.ActiveRules;
        Vector3 eye = playerPosition + Vector3.up * 1.2f;
        float minSqr = minDistance * minDistance;
        int inset = room.width > 2 && room.height > 2 ? 1 : 0;

        for (int attempt = 0; attempt < attempts; attempt++)
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

            if (offset.sqrMagnitude < minSqr)
                continue;

            if (requireHidden && !Hidden(eye, candidate + Vector3.up))
                continue;

            if (PcgPlacement.Validate(candidate, rules, playerPosition, taken) != PcgReject.None)
                continue;

            position = candidate;
            return true;
        }

        return false;
    }

    // Memunculkan, mendaftarkan, dan (opsional) memberi tahu posisi MC.
    public static EnemyController Spawn(ObjectiveContext context, EnemyRole role, Vector3 position, bool alert, List<Vector3> taken)
    {
        EnemyHealth enemy = context.Spawner.SpawnAt(role, position);

        if (enemy == null)
            return null;

        taken?.Add(position);
        context.Battle.Track(enemy);

        EnemyController controller = enemy.GetComponent<EnemyController>();

        if (controller == null)
            return null;

        PlayerHealth player = PlayerHealth.Current;

        if (alert && player != null)
            controller.Blackboard.Alert(player.transform.position, player.gameObject);

        if (TacticalCoordinator.Current != null)
            TacticalCoordinator.Current.RegisterLate(controller);

        return controller;
    }

    // Garis dari mata MC ke titik terhalang dinding atau props penghalang (layer NavBlock).
    public static bool Hidden(Vector3 eye, Vector3 target)
    {
        Vector3 lifted = new Vector3(target.x, Mathf.Max(target.y, eye.y - 0.2f), target.z);
        return Physics.Linecast(eye, lifted, GameLayers.ObstacleMask, QueryTriggerInteraction.Ignore);
    }

    // Room tempat posisi ini berada. Di koridor dipakai room terdekat (jarak datar ke pusat room).
    public static int NearestRoom(DungeonRoomGraph graph, Vector3 position)
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

    // Vault yang masih terkunci tidak bisa ditinggalkan musuh, jadi tidak dipakai sebagai room spawn.
    public static bool IsLockedVault(DungeonRoomGraph graph, int room)
    {
        DungeonVault vault = DungeonVault.Active;
        bool closed = vault != null && (vault.Door == null || !vault.Door.IsOpen);
        return closed && graph.Room(room) == graph.Dungeon.VaultRoom;
    }

    // Weighted random role dari komposisi wave (jumlah per role = bobot).
    public static EnemyRole PickRole(SeededRandom random, IReadOnlyList<EnemyRole> roles, IReadOnlyList<int> weights)
    {
        int total = 0;

        foreach (int weight in weights)
            total += weight;

        int pick = random.Range(0, Mathf.Max(1, total));

        for (int i = 0; i < roles.Count; i++)
        {
            pick -= weights[i];

            if (pick < 0)
                return roles[i];
        }

        return roles.Count > 0 ? roles[roles.Count - 1] : EnemyRole.Charger;
    }

    // Komposisi wave sebagai tabel role + bobot. Wave kosong = Charger saja.
    public static void ReadRoles(WaveData wave, List<EnemyRole> roles, List<int> weights)
    {
        roles.Clear();
        weights.Clear();

        if (wave != null)
        {
            foreach (WaveEntry entry in wave.Entries)
            {
                if (entry.count <= 0)
                    continue;

                roles.Add(entry.role);
                weights.Add(entry.count);
            }
        }

        if (roles.Count == 0)
        {
            roles.Add(EnemyRole.Charger);
            weights.Add(1);
        }
    }

    // Batas musuh hidup = round(nilai dasar x faktor DDA).
    public static float DdaFactor()
    {
        DifficultyModel dda = DifficultyModel.Current;
        return dda != null && dda.Active ? Mathf.Max(0.1f, dda.Factor) : 1f;
    }
}
