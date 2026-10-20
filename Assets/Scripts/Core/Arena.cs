using System.Collections.Generic;
using UnityEngine;

// Satu arena di scene. Dipasang di root arena (mis. arena_1, arena_2_str).
// Menyimpan titik masuk MC dan semua SpawnPoint di dalam arena, termasuk titik khusus di atas menara.
public class Arena : MonoBehaviour
{
    [SerializeField] private string displayName;
    // Tempat MC muncul setelah memilih gate.
    [SerializeField] private Transform playerEntry;
    // Kosongkan untuk memakai semua SpawnPoint yang menjadi child arena ini (termasuk di dalam menara).
    // Slot yang masih None diabaikan. Jika semua slot None, child arena dipakai otomatis.
    [SerializeField] private SpawnPoint[] spawnPoints;
    // Bobot dasar saat pemilihan arena. 1 = peluang normal, 0 = tidak pernah terpilih.
    [SerializeField, Min(0f)] private float baseWeight = 1f;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    public Transform PlayerEntry => playerEntry;

    public float BaseWeight => baseWeight;

    private SpawnPoint[] resolved;

    public SpawnPoint[] SpawnPoints
    {
        get
        {
            if (resolved == null)
                resolved = ResolveSpawnPoints();

            return resolved;
        }
    }

    private SpawnPoint[] ResolveSpawnPoints()
    {
        List<SpawnPoint> valid = new List<SpawnPoint>();

        if (spawnPoints != null)
        {
            foreach (SpawnPoint point in spawnPoints)
            {
                if (point != null)
                    valid.Add(point);
            }

            if (valid.Count < spawnPoints.Length)
                Debug.LogWarning("Arena " + name + ": " + (spawnPoints.Length - valid.Count) + " slot Spawn Points masih None dan diabaikan.", this);
        }

        if (valid.Count == 0)
            valid.AddRange(GetComponentsInChildren<SpawnPoint>(true));

        if (valid.Count == 0)
            Debug.LogError("Arena " + name + ": tidak ada SpawnPoint sama sekali.", this);

        return valid.ToArray();
    }

    private void OnValidate()
    {
        resolved = null;

        if (playerEntry == null)
            Debug.LogWarning("Arena " + name + ": Player Entry belum diisi.", this);
    }
}
