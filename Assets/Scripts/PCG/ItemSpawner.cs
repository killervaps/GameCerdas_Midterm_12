using System;
using System.Collections.Generic;
using UnityEngine;

// Satu baris tabel item.
[Serializable]
public class ItemSpawnEntry : IWeightedEntry
{
    public PickupItem prefab;
    // Bobot weighted random terhadap item lain di tabel.
    [Min(0)] public int weight = 1;
    // Batas jumlah item ini dalam satu ronde.
    [Min(0)] public int maxPerRound = 2;

    [NonSerialized] public int count;

    public int Weight => prefab != null ? weight : 0;

    public bool IsSelectable => prefab != null && count < maxPerRound;
}

// PCG item, alurnya sama dengan musuh dan modul praktikum:
//   1. jumlah item = acak di [Min Items, Max Items]
//   2. tiap item: pilih jenisnya dengan weighted random dari tabel (item yang sudah Max Per Round dilewati)
//   3. cari posisi dengan generate-and-test di PcgSpawnArea arena (PcgPlacement)
// Dijalankan tepat setelah musuh muncul. Posisi musuh ikut dihitung sebagai objek lain (Rule 3),
// jadi item tidak menempel musuh. Item yang tersisa dihapus saat kembali ke Safezone.
public class ItemSpawner : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private EnemySpawner enemySpawner;

    [Header("Jumlah per Ronde")]
    [SerializeField, Min(0)] private int minItems = 1;
    [SerializeField, Min(0)] private int maxItems = 2;

    [Header("Tabel Item")]
    [SerializeField] private ItemSpawnEntry[] items = Array.Empty<ItemSpawnEntry>();

    [Header("Rules")]
    // Mati = memakai Default Rules di PcgDirector. Item boleh lebih dekat ke MC daripada musuh.
    [SerializeField] private bool overrideRules = true;
    [SerializeField] private PcgSpawnRules rules = new PcgSpawnRules
    {
        minDistanceFromPlayer = 3f,
        checkRadius = 0.3f,
        minimumSpacing = 1.5f
    };

    [Header("Debug")]
    [SerializeField] private bool logSpawns = true;
    [SerializeField] private bool drawLastPlacement = true;

    private readonly List<PickupItem> spawned = new List<PickupItem>();
    private readonly List<Vector3> occupied = new List<Vector3>();
    private readonly PcgPlacementReport lastReport = new PcgPlacementReport();

    public IReadOnlyList<PickupItem> Spawned => spawned;

    public PcgPlacementReport LastReport => lastReport;

    private PcgSpawnRules ActiveRules =>
        overrideRules || PcgDirector.Current == null ? rules : PcgDirector.Current.DefaultRules;

    private void Awake()
    {
        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();

        if (enemySpawner == null)
            enemySpawner = FindAnyObjectByType<EnemySpawner>();

        if (runManager == null || enemySpawner == null)
            Debug.LogError("ItemSpawner: RunManager atau EnemySpawner tidak ditemukan.", this);
    }

    private void OnEnable()
    {
        if (enemySpawner != null)
            enemySpawner.WaveSpawned += HandleWaveSpawned;

        if (runManager != null)
            runManager.StateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (enemySpawner != null)
            enemySpawner.WaveSpawned -= HandleWaveSpawned;

        if (runManager != null)
            runManager.StateChanged -= HandleStateChanged;
    }

    private void HandleWaveSpawned(IReadOnlyList<EnemyHealth> enemies)
    {
        SpawnItems(runManager != null ? runManager.CurrentRound : 1);
    }

    // Item hanya hidup selama pertempuran dan jeda setelah ronde bersih.
    private void HandleStateChanged(RunState state)
    {
        if (state != RunState.Battle && state != RunState.RoundCleared)
            ClearItems();
    }

    public void SpawnItems(int round)
    {
        ClearItems();

        PcgSpawnArea area = enemySpawner != null ? enemySpawner.CurrentArea : null;

        if (area == null)
        {
            Debug.LogWarning("ItemSpawner: arena ini belum punya PcgSpawnArea, item tidak dimunculkan.", this);
            return;
        }

        SeededRandom selection = PcgDirector.Stream(round, PcgStream.ItemSelection);
        SeededRandom placement = PcgDirector.Stream(round, PcgStream.ItemPlacement);
        PcgSpawnRules activeRules = ActiveRules;
        Vector3? player = area.PlayerEntryPoint;

        foreach (ItemSpawnEntry entry in items)
        {
            if (entry != null)
                entry.count = 0;
        }

        // Posisi musuh ikut dijaga jaraknya, tapi tidak dihitung sebagai item yang diletakkan.
        if (enemySpawner != null)
            occupied.AddRange(enemySpawner.Occupied);

        int count = selection.Range(minItems, Mathf.Max(minItems, maxItems) + 1);
        lastReport.Requested = count;

        for (int i = 0; i < count; i++)
        {
            int index = WeightedSelection.PickIndex(items, selection);

            if (index < 0)
            {
                Debug.LogWarning("ItemSpawner: tabel item kosong atau semua item sudah mencapai Max Per Round.", this);
                break;
            }

            ItemSpawnEntry entry = items[index];

            if (!PcgPlacement.TryPlace(area, activeRules, placement, player, occupied, lastReport, out Vector3 position))
                continue;

            entry.count++;
            Spawn(entry.prefab, position);
        }

        if (logSpawns)
            Debug.Log("ItemSpawner: ronde " + round + ", " + Describe() + ". " + lastReport.Summary(), this);
    }

    public void ClearItems()
    {
        foreach (PickupItem item in spawned)
        {
            if (item != null)
            {
                item.PickedUp -= HandlePickedUp;
                Destroy(item.gameObject);
            }
        }

        spawned.Clear();
        occupied.Clear();
        lastReport.Clear();
    }

    private void Spawn(PickupItem prefab, Vector3 position)
    {
        PickupItem item = Instantiate(prefab, position, Quaternion.identity, transform);
        item.name = prefab.name + "_" + spawned.Count;
        item.PickedUp += HandlePickedUp;
        spawned.Add(item);
    }

    private void HandlePickedUp(PickupItem item)
    {
        item.PickedUp -= HandlePickedUp;
        spawned.Remove(item);

        if (logSpawns)
            Debug.Log("ItemSpawner: " + item.DisplayName + " diambil MC.", this);
    }

    private string Describe()
    {
        if (spawned.Count == 0)
            return "tidak ada item";

        System.Text.StringBuilder builder = new System.Text.StringBuilder();

        foreach (ItemSpawnEntry entry in items)
        {
            if (entry == null || entry.count <= 0)
                continue;

            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(entry.count).Append(' ').Append(entry.prefab.DisplayName);
        }

        return builder.ToString();
    }

    // Saat Play: bola biru muda = posisi item ronde ini, silang = kandidat ditolak.
    private void OnDrawGizmos()
    {
        if (!drawLastPlacement || !Application.isPlaying)
            return;

        float radius = ActiveRules.checkRadius;

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);

        foreach (Vector3 point in lastReport.placed)
            Gizmos.DrawWireSphere(point + Vector3.up * radius, radius);

        foreach (PcgRejectedCandidate candidate in lastReport.rejected)
        {
            Gizmos.color = PcgDirector.RejectColor(candidate.reason);
            Vector3 c = candidate.position + Vector3.up * 0.05f;
            Gizmos.DrawLine(c + new Vector3(-0.1f, 0f, -0.1f), c + new Vector3(0.1f, 0f, 0.1f));
            Gizmos.DrawLine(c + new Vector3(-0.1f, 0f, 0.1f), c + new Vector3(0.1f, 0f, -0.1f));
        }
    }
}
