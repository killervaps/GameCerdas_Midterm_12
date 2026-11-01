using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Memunculkan musuh satu ronde dengan Procedural Content Generation.
// Komposisi (role dan jumlah) sudah dibuat RunManager di Safezone. Di sini hanya posisinya, saat ronde mulai:
//   1. Titik khusus role (SpawnPoint dengan Accept Any Role mati), mis. puncak menara untuk Shooter.
//      Dipakai dengan peluang Anchor Chance role itu, titiknya dipilih acak ber-seed di antara yang masih kosong.
//   2. Selain itu generate-and-test di PcgSpawnArea arena (PcgPlacement): acak, validasi aturan, pakai atau ulangi.
// Seed run yang sama selalu menghasilkan posisi yang sama.
public class EnemySpawner : MonoBehaviour
{
    [Serializable]
    public struct RolePrefab
    {
        public EnemyRole role;
        public EnemyHealth prefab;
    }

    [Serializable]
    public struct RoleAnchorChance
    {
        public EnemyRole role;
        [Range(0f, 1f)] public float chance;
    }

    [Header("Prefabs")]
    // Stat per role dibaca dari EnemyRoleData di EnemyController pada prefab masing-masing.
    [SerializeField] private RolePrefab[] rolePrefabs;

    [Header("PCG Placement")]
    // Peluang musuh role ini memakai titik khusus role-nya jika arena punya. Role yang tidak ada di daftar = 0.
    // Shooter 1 = selalu naik menara selama masih ada menara kosong, Shooter berikutnya ke lantai lewat PCG.
    [SerializeField] private RoleAnchorChance[] anchorChances =
    {
        new RoleAnchorChance { role = EnemyRole.Shooter, chance = 1f }
    };
    // Mati = memakai Default Rules di PcgDirector.
    [SerializeField] private bool overrideRules;
    [SerializeField] private PcgSpawnRules rules = new PcgSpawnRules();
    // Jika satu musuh tetap gagal setelah Max Attempts, dicoba sekali lagi dengan jarak minimum dibuat setengah,
    // supaya jumlah musuh sama dengan preview di Safezone.
    [SerializeField] private bool relaxOnFailure = true;
    // Jarak maksimal pencarian NavMesh untuk titik khusus (puncak menara).
    [SerializeField] private float anchorSnapDistance = 2f;

    [Header("Debug")]
    [SerializeField] private bool logSpawns = true;
    [SerializeField] private bool drawLastPlacement = true;

    private readonly List<EnemyHealth> spawned = new List<EnemyHealth>();
    private readonly Dictionary<EnemyHealth, EnemyRole> roles = new Dictionary<EnemyHealth, EnemyRole>();
    private readonly List<SpawnPoint> usedAnchors = new List<SpawnPoint>();
    private readonly List<Vector3> occupied = new List<Vector3>();
    private readonly List<Vector3> anchorPositions = new List<Vector3>();
    private readonly PcgPlacementReport lastReport = new PcgPlacementReport();

    private Arena arena;
    private PcgSpawnArea area;
    private int counter;

    // Semua musuh dari wave terakhir, termasuk yang sudah mati.
    public IReadOnlyList<EnemyHealth> Spawned => spawned;

    // Hasil generate-and-test ronde terakhir (hanya musuh di lantai), untuk debug.
    public PcgPlacementReport LastReport => lastReport;

    // Posisi musuh yang memakai titik khusus pada ronde terakhir.
    public IReadOnlyList<Vector3> AnchorPositions => anchorPositions;

    public int LastSeed { get; private set; }

    // Posisi semua musuh ronde ini. ItemSpawner memakainya supaya item tidak muncul menempel musuh.
    public IReadOnlyList<Vector3> Occupied => occupied;

    public PcgSpawnArea CurrentArea => area;

    private PcgSpawnRules ActiveRules =>
        overrideRules || PcgDirector.Current == null ? rules : PcgDirector.Current.DefaultRules;

    // Dikirim setelah satu wave selesai dimunculkan. Didengar BattleManager dan TacticalCoordinator.
    public event Action<IReadOnlyList<EnemyHealth>> WaveSpawned;

    private void Awake()
    {
        DisableSceneTemplates();
    }

    // Prefab boleh berupa objek di scene (template), bukan hanya asset di Project.
    // Template yang aktif ikut dihitung sebagai musuh, bisa ditembak, dan menjalankan AI, jadi dimatikan di awal.
    private void DisableSceneTemplates()
    {
        if (rolePrefabs == null)
            return;

        foreach (RolePrefab entry in rolePrefabs)
        {
            if (entry.prefab == null || !entry.prefab.gameObject.scene.IsValid())
                continue;

            if (entry.prefab.gameObject.activeSelf)
            {
                entry.prefab.gameObject.SetActive(false);

                Debug.Log(
                    "EnemySpawner: template " + entry.prefab.name + " (" + entry.role + ") ada di scene dan dinonaktifkan. " +
                    "Bisa juga dijadikan prefab asset supaya tidak perlu ada di scene.",
                    entry.prefab
                );
            }
        }
    }

    // Dipanggil RunManager saat arena berganti.
    public void UseArena(Arena next)
    {
        arena = next;
        area = arena != null ? arena.GetComponentInChildren<PcgSpawnArea>(true) : null;

        if (arena != null && area == null)
            Debug.LogError("EnemySpawner: arena " + arena.DisplayName + " belum punya PcgSpawnArea. Musuh tanpa titik khusus akan muncul di posisi spawner.", arena);
    }

    public bool TryGetRole(EnemyHealth enemy, out EnemyRole role)
    {
        return roles.TryGetValue(enemy, out role);
    }

    // Role Data dari prefab atau template role ini. Dipakai ThreatEstimator untuk preview wave.
    public bool TryGetRoleData(EnemyRole role, out EnemyRoleData data)
    {
        EnemyHealth prefab = GetPrefab(role);
        EnemyController controller = prefab != null ? prefab.GetComponent<EnemyController>() : null;

        data = controller != null ? controller.RoleData : null;
        return data != null;
    }

    public IReadOnlyList<EnemyHealth> SpawnWave(WaveData wave, int round)
    {
        ClearWave();

        if (wave == null)
        {
            Debug.LogWarning("EnemySpawner: WaveData kosong.", this);
            WaveSpawned?.Invoke(spawned);
            return spawned;
        }

        SeededRandom random = PcgDirector.Stream(round, PcgStream.EnemyPlacement);
        PcgSpawnRules activeRules = ActiveRules;
        Vector3? player = PlayerPoint();

        LastSeed = random.Seed;

        foreach (WaveEntry entry in wave.Entries)
        {
            EnemyHealth prefab = GetPrefab(entry.role);

            if (prefab == null)
            {
                Debug.LogError("EnemySpawner: prefab untuk role " + entry.role + " belum diisi.", this);
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                if (TryPickPosition(entry.role, random, activeRules, player, out Vector3 position, out Quaternion rotation))
                    Spawn(prefab, entry.role, position, rotation);
            }
        }

        if (logSpawns)
        {
            Debug.Log("EnemySpawner: " + wave.DisplayName + " muncul, " + spawned.Count + "/" + wave.TotalCount + " musuh (" + wave.Describe() + "), " +
                      anchorPositions.Count + " di titik khusus. Lantai: " + lastReport.Summary(), this);
        }

        WaveSpawned?.Invoke(spawned);

        return spawned;
    }

    // Menghapus semua musuh wave sebelumnya, mis. saat retry.
    public void ClearWave()
    {
        foreach (EnemyHealth enemy in spawned)
        {
            if (enemy != null)
                Destroy(enemy.gameObject);
        }

        spawned.Clear();
        roles.Clear();
        usedAnchors.Clear();
        occupied.Clear();
        anchorPositions.Clear();
        lastReport.Clear();
    }

    private bool TryPickPosition(EnemyRole role, SeededRandom random, PcgSpawnRules activeRules, Vector3? player,
        out Vector3 position, out Quaternion rotation)
    {
        // Angka ini selalu diambil walau arena tidak punya titik khusus, supaya urutan acak tetap stabil.
        float roll = random.Value;
        SpawnPoint anchor = roll < AnchorChance(role) ? PickFreeAnchor(role, random) : null;

        if (anchor != null)
        {
            position = anchor.transform.position;

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, anchorSnapDistance, NavMesh.AllAreas))
                position = hit.position;

            rotation = anchor.transform.rotation;
            usedAnchors.Add(anchor);
            occupied.Add(position);
            anchorPositions.Add(position);
            return true;
        }

        if (area == null)
        {
            position = transform.position;
            rotation = transform.rotation;
            return true;
        }

        lastReport.Requested++;

        if (PcgPlacement.TryPlace(area, activeRules, random, player, occupied, lastReport, out position))
        {
            rotation = FacePlayer(position, player);
            return true;
        }

        if (relaxOnFailure)
        {
            PcgSpawnRules relaxed = activeRules.Clone();
            relaxed.minimumSpacing *= 0.5f;
            relaxed.minDistanceFromPlayer *= 0.5f;

            if (PcgPlacement.TryPlace(area, relaxed, random, player, occupied, lastReport, out position))
            {
                Debug.LogWarning("EnemySpawner: " + role + " baru bisa ditempatkan setelah jarak minimum dilonggarkan. Area mungkin terlalu sempit.", this);
                rotation = FacePlayer(position, player);
                return true;
            }
        }

        Debug.LogWarning("EnemySpawner: " + role + " dilewati, tidak ada posisi valid setelah " + activeRules.maxAttemptsPerObject + " percobaan.", this);
        rotation = Quaternion.identity;
        return false;
    }

    private float AnchorChance(EnemyRole role)
    {
        if (anchorChances == null)
            return 0f;

        foreach (RoleAnchorChance entry in anchorChances)
        {
            if (entry.role == role)
                return entry.chance;
        }

        return 0f;
    }

    // Titik khusus role ini di arena sekarang yang belum dipakai, dipilih acak (reservoir sampling ber-seed).
    private SpawnPoint PickFreeAnchor(EnemyRole role, SeededRandom random)
    {
        if (arena == null)
            return null;

        SpawnPoint result = null;
        int seen = 0;

        foreach (SpawnPoint point in arena.SpawnPoints)
        {
            if (point == null || !point.IsDedicatedTo(role) || usedAnchors.Contains(point))
                continue;

            seen++;

            if (random.Range(0, seen) == 0)
                result = point;
        }

        return result;
    }

    private Vector3? PlayerPoint()
    {
        if (area != null)
            return area.PlayerEntryPoint;

        return arena != null && arena.PlayerEntry != null ? arena.PlayerEntry.position : (Vector3?)null;
    }

    private static Quaternion FacePlayer(Vector3 position, Vector3? player)
    {
        if (!player.HasValue)
            return Quaternion.identity;

        Vector3 direction = player.Value - position;
        direction.y = 0f;

        return direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
    }

    private void Spawn(EnemyHealth prefab, EnemyRole role, Vector3 position, Quaternion rotation)
    {
        EnemyHealth enemy = Instantiate(prefab, position, rotation);

        counter++;
        enemy.name = role + "_" + counter;

        // Salinan dari template scene yang nonaktif ikut nonaktif, jadi dihidupkan di sini.
        if (!enemy.gameObject.activeSelf)
            enemy.gameObject.SetActive(true);

        spawned.Add(enemy);
        roles[enemy] = role;
    }

    private EnemyHealth GetPrefab(EnemyRole role)
    {
        if (rolePrefabs == null)
            return null;

        foreach (RolePrefab entry in rolePrefabs)
        {
            if (entry.role == role)
                return entry.prefab;
        }

        return null;
    }

    // Saat Play: hasil ronde terakhir. Bola hijau = posisi lantai, bola ungu = titik khusus, silang = kandidat ditolak.
    private void OnDrawGizmos()
    {
        if (!drawLastPlacement || !Application.isPlaying)
            return;

        float radius = ActiveRules.checkRadius;

        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);

        foreach (Vector3 point in lastReport.placed)
            Gizmos.DrawSphere(point + Vector3.up * radius, radius);

        Gizmos.color = new Color(0.75f, 0.45f, 1f, 0.9f);

        foreach (Vector3 point in anchorPositions)
            Gizmos.DrawSphere(point + Vector3.up * radius, radius);

        foreach (PcgRejectedCandidate candidate in lastReport.rejected)
        {
            Gizmos.color = PcgDirector.RejectColor(candidate.reason);
            Vector3 c = candidate.position + Vector3.up * 0.05f;
            Gizmos.DrawLine(c + new Vector3(-0.15f, 0f, -0.15f), c + new Vector3(0.15f, 0f, 0.15f));
            Gizmos.DrawLine(c + new Vector3(-0.15f, 0f, 0.15f), c + new Vector3(0.15f, 0f, -0.15f));
        }
    }
}
