using Unity.Cinemachine;
using UnityEngine;

// Penjepit kamera ke dalam ruangan, dipasang di ThirdPersonCamera di samping CinemachineDeoccluder.
// Masalah: tembok project ini rendah (arena sekitar 1 unit, dungeon 1,8 unit) sedangkan kamera sekitar 3 unit di atas MC.
// Deoccluder hanya memeriksa garis miring MC -> kamera, dan garis itu sering lewat DI ATAS tembok,
// sehingga kamera dibiarkan berada di balik tembok (di luar arena).
// Solusi: pemeriksaan mendatar. Dilihat dari atas, apakah ada tembok di antara MC dan titik lantai di bawah kamera?
// SphereCast datar setinggi lutut dari MC ke arah kamera. Jika kena, kamera ditarik mendekat di sepanjang garis orbitnya
// sampai tepat di depan tembok. Masuk langsung (tanpa damping), keluar pelan.
// Memakai collider tembok yang sudah ada (layer NavBlock), jadi otomatis bekerja di arena, Safezone, dan dungeon hasil PCG.
public class CameraWallClamp : CinemachineExtension
{
    [Tooltip("Layer tembok. Nothing = layer NavBlock.")]
    [SerializeField] private LayerMask wallLayers;
    [Tooltip("Tinggi SphereCast dari kaki MC. Benda yang lebih pendek dari ini (barel, batu) tidak menarik kamera. " +
             "Harus di bawah tinggi tembok terendah (tembok arena sekitar 1 unit).")]
    [SerializeField] private float castHeight = 0.6f;
    [Tooltip("Radius SphereCast. Kira-kira setengah lebar lensa kamera.")]
    [SerializeField] private float castRadius = 0.25f;
    [Tooltip("Jarak kamera dari permukaan tembok setelah ditarik.")]
    [SerializeField] private float wallMargin = 0.3f;
    [Tooltip("Tinggi titik pusat tarikan dari kaki MC. Kamera ditarik ke arah titik ini, sama dengan Target Offset orbit.")]
    [SerializeField] private float pivotHeight = 1.1f;
    [Tooltip("Jarak datar minimum kamera dari MC setelah ditarik.")]
    [SerializeField] private float minimumDistance = 0.4f;
    [Tooltip("Waktu kembali ke jarak normal setelah tembok tidak menghalangi lagi.")]
    [SerializeField] private float releaseDamping = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private class ExtraState : VcamExtraStateBase
    {
        // Fraksi jarak orbit yang dipakai. 1 = jarak normal, kurang dari 1 = ditarik karena tembok.
        public float ratio = 1f;
        public Vector3 castOrigin;
        public Vector3 castEnd;
        public bool hit;
    }

    private ExtraState lastState;

    public bool IsClamping => lastState != null && lastState.ratio < 0.999f;

    private int Mask => wallLayers.value != 0 ? wallLayers.value : LayerMask.GetMask(GameLayers.NavBlock);

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body)
            return;

        Transform follow = vcam.Follow;

        if (follow == null)
            return;

        ExtraState extra = GetExtraState<ExtraState>(vcam);
        lastState = extra;

        Vector3 camera = state.GetCorrectedPosition();
        Vector3 feet = follow.position;

        Vector3 flat = camera - feet;
        flat.y = 0f;
        float flatDistance = flat.magnitude;

        float target = 1f;
        extra.hit = false;

        if (flatDistance > 0.01f)
        {
            Vector3 direction = flat / flatDistance;
            extra.castOrigin = feet + Vector3.up * castHeight;
            extra.castEnd = extra.castOrigin + direction * flatDistance;

            if (Physics.SphereCast(extra.castOrigin, castRadius, direction, out RaycastHit hit,
                    flatDistance + wallMargin, Mask, QueryTriggerInteraction.Ignore))
            {
                float allowed = Mathf.Max(minimumDistance, hit.distance - wallMargin);
                target = Mathf.Clamp01(allowed / flatDistance);
                extra.hit = true;
                extra.castEnd = extra.castOrigin + direction * hit.distance;
            }
        }

        // Ditarik masuk langsung, kembali keluar pelan. Reset tanpa damping setelah teleport.
        if (!vcam.PreviousStateIsValid || deltaTime < 0f || target < extra.ratio || releaseDamping <= 0f)
            extra.ratio = target;
        else
            extra.ratio = Mathf.Lerp(extra.ratio, target, 1f - Mathf.Exp(-deltaTime / releaseDamping));

        if (extra.ratio >= 0.999f)
            return;

        // Kamera digeser di sepanjang garis ke titik pusat (dada MC), jadi sudut pandang tetap sama, hanya lebih dekat.
        Vector3 pivot = feet + Vector3.up * pivotHeight;
        Vector3 clamped = pivot + (camera - pivot) * extra.ratio;

        state.PositionCorrection += clamped - camera;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || lastState == null || !Application.isPlaying)
            return;

        Gizmos.color = lastState.hit ? new Color(1f, 0.4f, 0.2f, 0.9f) : new Color(0.3f, 1f, 0.5f, 0.6f);
        Gizmos.DrawLine(lastState.castOrigin, lastState.castEnd);
        Gizmos.DrawWireSphere(lastState.castEnd, castRadius);
    }
}
