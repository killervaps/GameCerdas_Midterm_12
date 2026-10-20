using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Memunculkan musuh sesuai WaveData di SpawnPoint arena.
public class EnemySpawner : MonoBehaviour
{
    [Serializable]
    public struct RolePrefab
    {
        public EnemyRole role;
        public EnemyHealth prefab;
    }

    [Header("Prefabs")]
    // Stat per role dibaca dari EnemyRoleData di EnemyController pada prefab masing-masing.
    [SerializeField] private RolePrefab[] rolePrefabs;

    [Header("Spawn Points")]
    // Kosongkan untuk memakai semua SpawnPoint yang menjadi child objek ini.
    [SerializeField] private SpawnPoint[] spawnPoints;
    // Jarak maksimal pencarian titik NavMesh terdekat dari SpawnPoint.
    [SerializeField] private float navMeshSnapDistance = 2f;

    [Header("Debug")]
    [SerializeField] private bool logSpawns = true;

    private readonly List<EnemyHealth> spawned = new List<EnemyHealth>();
    private readonly List<SpawnPoint> usedPoints = new List<SpawnPoint>();
    private readonly Dictionary<EnemyHealth, EnemyRole> roles = new Dictionary<EnemyHealth, EnemyRole>();

    private int counter;

    // Semua musuh dari wave terakhir, termasuk yang sudah mati.
    public IReadOnlyList<EnemyHealth> Spawned => spawned;

    // Dikirim setelah satu wave selesai dimunculkan. Didengar BattleManager dan nanti TacticalCoordinator.
    public event Action<IReadOnlyList<EnemyHealth>> WaveSpawned;

    private void Awake()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            spawnPoints = GetComponentsInChildren<SpawnPoint>();

        if (spawnPoints.Length == 0)
            Debug.LogWarning("EnemySpawner: belum ada SpawnPoint. Musuh akan muncul di posisi spawner.", this);

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

    // Dipanggil RunManager saat arena berganti. Spawn berikutnya memakai titik-titik dari arena itu.
    public void UseSpawnPoints(SpawnPoint[] points)
    {
        spawnPoints = points ?? Array.Empty<SpawnPoint>();
        usedPoints.Clear();

        if (spawnPoints.Length == 0)
            Debug.LogWarning("EnemySpawner: arena ini belum punya SpawnPoint.", this);
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

    public IReadOnlyList<EnemyHealth> SpawnWave(WaveData wave)
    {
        ClearWave();

        if (wave == null)
        {
            Debug.LogWarning("EnemySpawner: WaveData kosong.", this);
            WaveSpawned?.Invoke(spawned);
            return spawned;
        }

        foreach (WaveEntry entry in wave.Entries)
        {
            EnemyHealth prefab = GetPrefab(entry.role);

            if (prefab == null)
            {
                Debug.LogError("EnemySpawner: prefab untuk role " + entry.role + " belum diisi.", this);
                continue;
            }

            for (int i = 0; i < entry.count; i++)
                Spawn(prefab, entry.role);
        }

        if (logSpawns)
            Debug.Log("EnemySpawner: " + wave.DisplayName + " muncul, " + spawned.Count + " musuh (" + wave.Describe() + ")", this);

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
        usedPoints.Clear();
        roles.Clear();
    }

    private void Spawn(EnemyHealth prefab, EnemyRole role)
    {
        SpawnPoint point = PickPoint(role);

        Vector3 position = point != null ? point.transform.position : transform.position;
        Quaternion rotation = point != null ? point.transform.rotation : transform.rotation;

        // Pastikan musuh berdiri di atas NavMesh supaya NavMeshAgent (M4) tidak gagal.
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSnapDistance, NavMesh.AllAreas))
            position = hit.position;

        EnemyHealth enemy = Instantiate(prefab, position, rotation);

        counter++;
        enemy.name = role + "_" + counter;

        // Salinan dari template scene yang nonaktif ikut nonaktif, jadi dihidupkan di sini.
        if (!enemy.gameObject.activeSelf)
            enemy.gameObject.SetActive(true);

        spawned.Add(enemy);
        roles[enemy] = role;
    }

    // Urutan prioritas, masing-masing dipilih acak di antara yang cocok:
    //   1. titik khusus role ini yang belum dipakai (mis. puncak menara untuk Shooter, 2 menara + 1 Shooter = 50/50)
    //   2. titik lain yang menerima role ini dan belum dipakai
    //   3. semua titik penuh: pakai ulang titik umum, baru titik khusus role ini
    // Titik khusus role lain tidak pernah dipakai, jadi Charger/Guardian tidak muncul di menara.
    private SpawnPoint PickPoint(EnemyRole role)
    {
        SpawnPoint chosen = PickRandom(p => p.IsDedicatedTo(role), true) ??
                            PickRandom(p => p.Accepts(role), true) ??
                            PickRandom(p => !p.IsDedicated, false) ??
                            PickRandom(p => p.Accepts(role), false);

        if (chosen != null)
            usedPoints.Add(chosen);

        return chosen;
    }

    private SpawnPoint PickRandom(Predicate<SpawnPoint> match, bool freeOnly)
    {
        SpawnPoint result = null;
        int seen = 0;

        foreach (SpawnPoint point in spawnPoints)
        {
            if (point == null || !match(point) || (freeOnly && usedPoints.Contains(point)))
                continue;

            // Reservoir sampling: tiap titik yang cocok punya peluang sama tanpa membuat list baru.
            seen++;

            if (UnityEngine.Random.Range(0, seen) == 0)
                result = point;
        }

        return result;
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
}
