using System.Collections.Generic;
using UnityEngine;

// Area spawn procedural satu arena. Dipasang di root arena (objek yang sama dengan komponen Arena) atau child-nya.
// Titik acak diambil di dalam persegi ini, lalu ditembakkan raycast ke bawah untuk menemukan permukaan lantai,
// sehingga posisi spawn selalu menempel di lantai walau arena tidak rata.
// Menu titik tiga komponen > Fit To Arena Ground menyesuaikan ukuran dengan lantai arena secara otomatis.
[DisallowMultipleComponent]
public class PcgSpawnArea : MonoBehaviour
{
    [Header("Area")]
    // Pusat area relatif terhadap objek ini (dalam satuan dunia, mengikuti rotasi Y objek).
    // Diisi otomatis oleh Fit To Arena Ground. Transform objek tidak pernah diubah, jadi aman dipasang di root arena.
    [SerializeField] private Vector3 center = Vector3.zero;
    // Lebar (X) dan panjang (Z) area, dalam satuan dunia.
    [SerializeField] private Vector2 size = new Vector2(12f, 12f);
    // Jarak aman dari tepi lantai saat memakai Fit To Arena Ground.
    [SerializeField, Min(0f)] private float fitMargin = 0.5f;

    [Header("Enemy Composition Bias")]
    // Tema arena ini terhadap komposisi musuh, mis. arena menara: Shooter x2.
    // Kosong = komposisi mengikuti profil ronde di RunManager apa adanya.
    [SerializeField] private ArenaRoleBias[] roleBias = System.Array.Empty<ArenaRoleBias>();

    [Header("Ground Probe")]
    // Raycast dimulai setinggi ini di atas area, lalu turun mencari lantai.
    [SerializeField, Min(0.5f)] private float probeHeight = 10f;
    [SerializeField, Min(0.5f)] private float probeDepth = 15f;
    // Layer lantai. Nothing = layer Ground.
    [SerializeField] private LayerMask groundMask;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private Arena arena;

    public Vector2 Size => size;

    public IReadOnlyList<ArenaRoleBias> RoleBias => roleBias;

    public Arena Arena
    {
        get
        {
            if (arena == null)
                arena = GetComponentInParent<Arena>();

            return arena;
        }
    }

    // Titik yang harus dijauhi musuh (Rule 1): tempat MC muncul di arena ini.
    public Vector3? PlayerEntryPoint =>
        Arena != null && Arena.PlayerEntry != null ? Arena.PlayerEntry.position : (Vector3?)null;

    private int GroundMask => groundMask.value != 0 ? groundMask.value : GameLayers.GroundMask.value;

    // Mengambil satu titik acak di area dan mencari lantai di bawahnya.
    // Selalu memakai dua angka acak, berhasil atau tidak, supaya urutan acak tetap reproducible.
    public bool TrySampleCandidate(SeededRandom random, out Vector3 point)
    {
        Vector2 offset = random.InsideRect(size);
        Vector3 flat = WorldCenter + YawRotation * new Vector3(offset.x, 0f, offset.y);

        if (Physics.Raycast(flat + Vector3.up * probeHeight, Vector3.down, out RaycastHit hit,
                probeHeight + probeDepth, GroundMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        point = flat;
        return false;
    }

    public bool Contains(Vector3 worldPoint)
    {
        Vector3 local = Quaternion.Inverse(YawRotation) * (worldPoint - WorldCenter);
        return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f;
    }

    // Pusat area di dunia.
    public Vector3 WorldCenter => transform.position + YawRotation * center;

    // Hanya rotasi Y yang dipakai, supaya area tetap datar walau objek induk miring.
    private Quaternion YawRotation => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

    // Menyesuaikan pusat dan ukuran area dengan gabungan collider lantai (layer Ground) di arena ini.
    // Hanya field Center dan Size yang berubah, posisi arena tidak disentuh.
    [ContextMenu("Fit To Arena Ground")]
    private void FitToArenaGround()
    {
        Transform root = Arena != null ? Arena.transform : transform.parent != null ? transform.parent : transform;
        List<Collider> floors = new List<Collider>();
        int groundLayer = LayerMask.NameToLayer(GameLayers.Ground);

        foreach (Collider candidate in root.GetComponentsInChildren<Collider>(true))
        {
            if (candidate.gameObject.layer == groundLayer && !candidate.isTrigger)
                floors.Add(candidate);
        }

        if (floors.Count == 0)
        {
            Debug.LogWarning(name + ": tidak ada collider layer Ground di bawah " + root.name + ".", this);
            return;
        }

        Bounds bounds = floors[0].bounds;

        foreach (Collider floor in floors)
            bounds.Encapsulate(floor.bounds);

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Fit PCG Spawn Area");
#endif

        // Bounds collider sejajar sumbu dunia. Area diputar mengikuti rotasi Y objek, jadi ukuran dihitung
        // di ruang area: sudut-sudut bounds diproyeksikan ke sumbu area supaya area tetap menutupi lantai.
        Quaternion inverse = Quaternion.Inverse(YawRotation);
        Vector3 worldCenter = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        Vector3 extents = bounds.extents;
        float halfX = 0f;
        float halfZ = 0f;

        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 corner = inverse * new Vector3(extents.x * sx, 0f, extents.z * sz);
                halfX = Mathf.Max(halfX, Mathf.Abs(corner.x));
                halfZ = Mathf.Max(halfZ, Mathf.Abs(corner.z));
            }
        }

        center = inverse * (worldCenter - transform.position);
        size = new Vector2(Mathf.Max(1f, halfX * 2f - fitMargin * 2f), Mathf.Max(1f, halfZ * 2f - fitMargin * 2f));

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        Debug.Log(name + ": area PCG disesuaikan dengan " + floors.Count + " collider lantai, ukuran " + size.x.ToString("0.#") + " x " + size.y.ToString("0.#") + ".", this);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        Gizmos.matrix = Matrix4x4.TRS(WorldCenter + Vector3.up * 0.05f, YawRotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.02f, size.y));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
