using UnityEngine;

// Satu arena di scene. Dipasang di root arena (mis. arena_1, arena_2_str).
// Menyimpan titik masuk MC dan semua SpawnPoint di dalam arena, termasuk titik khusus di atas menara.
public class Arena : MonoBehaviour
{
    [SerializeField] private string displayName;
    // Tempat MC muncul setelah memilih gate.
    [SerializeField] private Transform playerEntry;
    // Kosongkan untuk memakai semua SpawnPoint yang menjadi child arena ini (termasuk di dalam menara).
    [SerializeField] private SpawnPoint[] spawnPoints;
    // Bobot dasar saat pemilihan arena. 1 = peluang normal, 0 = tidak pernah terpilih.
    [SerializeField, Min(0f)] private float baseWeight = 1f;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    public Transform PlayerEntry => playerEntry;

    public float BaseWeight => baseWeight;

    public SpawnPoint[] SpawnPoints
    {
        get
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
                spawnPoints = GetComponentsInChildren<SpawnPoint>(true);

            return spawnPoints;
        }
    }

    private void OnValidate()
    {
        if (playerEntry == null)
            Debug.LogWarning("Arena " + name + ": Player Entry belum diisi.", this);
    }
}
