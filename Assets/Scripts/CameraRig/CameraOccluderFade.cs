using System.Collections.Generic;
using UnityEngine;

// Memudarkan prop (CameraSeeThroughProp) yang berada di antara kamera orang ketiga dan MC.
// Kamera tidak lagi ditarik maju oleh prop (CameraWallClamp mengabaikannya), jadi kamera bisa berada di dalam
// atau di balik prop. Prop itu dipudarkan sampai Min Opacity supaya MC tetap terlihat, lalu kembali normal.
// Deteksi memakai bounds renderer prop (tanpa physics), jadi dekorasi tanpa collider juga ikut.
// Props dungeon (grup Decoration hasil DungeonGenerator) ditandai otomatis setiap dungeon dibuat.
// Hanya bekerja saat kamera orang ketiga aktif.
[RequireComponent(typeof(ThirdPersonCameraRig))]
public class CameraOccluderFade : MonoBehaviour
{
    [SerializeField] private bool fadeOccluders = true;
    // Opasitas prop yang menghalangi. 0 = hilang total, 1 = tidak memudar. Prop bisa menimpa nilai ini sendiri.
    [SerializeField, Range(0f, 1f)] private float minOpacity = 0.3f;
    // Waktu memudar dan kembali, dalam detik.
    [SerializeField, Min(0.01f)] private float fadeOutTime = 0.15f;
    [SerializeField, Min(0.01f)] private float fadeInTime = 0.3f;
    // Tebal garis pemeriksaan kamera -> MC. Lebih besar = prop yang hampir menghalangi ikut memudar.
    [SerializeField, Min(0f)] private float castRadius = 0.25f;
    // Prop yang menempel di lensa kamera (kamera di dalam prop) ikut memudar dalam radius ini.
    [SerializeField, Min(0f)] private float cameraRadius = 0.4f;
    // Ujung garis sepanjang ini di dekat MC diabaikan, supaya prop yang hanya menempel di samping/belakang MC
    // tidak ikut memudar. Naikkan jika prop di sebelah MC masih memudar, turunkan jika prop tepat di depan MC tidak memudar.
    [SerializeField, Min(0f)] private float focusMargin = 0.35f;

    private readonly HashSet<CameraSeeThroughProp> current = new HashSet<CameraSeeThroughProp>();
    private readonly HashSet<CameraSeeThroughProp> previous = new HashSet<CameraSeeThroughProp>();

    // Tandai otomatis props dungeon hasil generate sebagai see-through.
    [SerializeField] private bool markDungeonProps = true;

    private ThirdPersonCameraRig rig;
    private DungeonGenerator[] dungeons;

    private void Awake()
    {
        rig = GetComponent<ThirdPersonCameraRig>();
        dungeons = FindObjectsByType<DungeonGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (DungeonGenerator dungeon in dungeons)
        {
            dungeon.Generated += MarkDungeonProps;

            if (dungeon.LevelRoot != null)
                MarkDungeonProps(dungeon);
        }
    }

    private void OnDestroy()
    {
        if (dungeons == null)
            return;

        foreach (DungeonGenerator dungeon in dungeons)
        {
            if (dungeon != null)
                dungeon.Generated -= MarkDungeonProps;
        }
    }

    private void MarkDungeonProps(DungeonGenerator dungeon)
    {
        if (!markDungeonProps || dungeon == null || dungeon.LevelRoot == null)
            return;

        Transform decoration = dungeon.LevelRoot.Find("Decoration");

        if (decoration == null)
            return;

        foreach (Transform prop in decoration)
        {
            if (prop.GetComponent<CameraSeeThroughProp>() == null && prop.GetComponentInChildren<Renderer>() != null)
                prop.gameObject.AddComponent<CameraSeeThroughProp>();
        }
    }

    private void OnDisable()
    {
        ReleaseAll();
    }

    private void LateUpdate()
    {
        Transform cameraTransform = rig.OutputCamera;

        if (!fadeOccluders || cameraTransform == null)
        {
            ReleaseAll();
            return;
        }

        current.Clear();

        Vector3 from = cameraTransform.position;
        Vector3 to = rig.FocusPoint;
        IReadOnlyList<CameraSeeThroughProp> props = CameraSeeThroughProp.All;

        for (int i = 0; i < props.Count; i++)
        {
            CameraSeeThroughProp prop = props[i];

            if (prop != null && prop.ScanByCamera && prop.Blocks(from, to, castRadius, cameraRadius, focusMargin))
                current.Add(prop);
        }

        float outSpeed = 1f / fadeOutTime;
        float inSpeed = 1f / fadeInTime;

        foreach (CameraSeeThroughProp prop in current)
            prop.RequestFade(prop.MinOpacity(minOpacity), outSpeed);

        foreach (CameraSeeThroughProp prop in previous)
        {
            if (prop != null && !current.Contains(prop))
                prop.ReleaseFade(inSpeed);
        }

        previous.Clear();
        previous.UnionWith(current);
    }

    private void ReleaseAll()
    {
        foreach (CameraSeeThroughProp prop in previous)
        {
            if (prop != null)
                prop.ReleaseFade(1f / fadeInTime);
        }

        previous.Clear();
        current.Clear();
    }
}
