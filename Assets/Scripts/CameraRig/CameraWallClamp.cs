using Unity.Cinemachine;
using UnityEngine;

// Penahan tembok untuk kamera orang ketiga, dipasang di ThirdPersonCamera
// (ThirdPersonCameraRig menambahkannya sendiri saat Play jika belum ada).
// Satu-satunya pemecah tabrakan kamera TPP: koreksi komponen lain (Deoccluder) diganti hasil hitungan di sini,
// supaya hasilnya konsisten setiap frame.
//
// Perilaku (dilihat dari samping):
//   - Hanya saat tembok ada di antara MC dan posisi orbit kamera: kamera berhenti di depan tembok.
//     Kamera hanya digeser mendatar, tidak ditarik turun ke badan MC dan tidak dinaikkan.
//     Begitu MC menjauh dari tembok, kamera kembali halus ke posisi orbit.
//   - Kamera TIDAK PERNAH melewati tembok: batas tembok dihitung ulang setiap frame dan langsung dipakai (tanpa damping).
//     Damping hanya dipakai saat kamera kembali menjauh.
//   - Tinggi kamera tetap setinggi orbit, tapi tidak lebih tinggi dari puncak tembok yang menahannya.
//   - Yaw kamera selalu mengikuti yaw orbit (stabil, karena arah gerak MC memakai yaw kamera); hanya pitch yang
//     dihitung untuk memandang MC. Saat sangat dekat MC, kamera naik sedikit (tidak melebihi puncak tembok).
//   - Kamera memandang titik fokus di atas MC. Begitu tembok mulai menahan kamera, titik pandang turun bertahap ke
//     badan MC (kamera makin menunduk makin dekat). Pengaman terakhir: kaki MC selalu dijaga di atas HUD bawah (bar HP).
//   - Tembok dianggap menghalangi hanya jika garis tipis benar-benar terpotong (setinggi lutut MC -> kamera, atau
//     dada MC -> kamera). Tembok/pilar yang hanya terserempet saat MC berjalan menyusuri tembok tidak menarik kamera
//     maju; lensa kamera cukup digeser menyamping menjauhi permukaannya.
//   - Selama kamera ditahan tembok dekat MC, ThirdPersonCameraRig memperlambat putaran ke arah tembok
//     (lihat SensitivityScale dan YawMovesIntoWall).
public class CameraWallClamp : CinemachineExtension
{
    [Header("Gaya")]
    [Tooltip("Aktif = kamera hanya meluncur maju di sepanjang garis pandangnya sendiri ke MC saat ditahan tembok. " +
             "Sudut kamera tidak berubah: tidak dinaikkan, tidak diturunkan ke puncak tembok, dan tidak menunduk ke badan MC. " +
             "MC dibuat tembus pandang sebagian oleh ThirdPersonCameraRig saat kamera dekat. " +
             "Mati = perilaku lama (kamera naik dan menunduk saat dekat tembok).")]
    [SerializeField] private bool keepViewAngle = true;

    [Header("Tembok")]
    [Tooltip("Layer tembok. Nothing = layer NavBlock.")]
    [SerializeField] private LayerMask wallLayers;
    [Tooltip("Tinggi pemeriksaan mendatar dari kaki MC. Benda yang lebih pendek dari ini (barel, batu) tidak menahan kamera.")]
    [SerializeField] private float castHeight = 0.6f;
    [Tooltip("Radius lensa kamera. Jika lensa terlalu dekat ke permukaan tembok (misalnya saat berjalan menyusuri tembok), " +
             "kamera digeser MENYAMPING menjauhi tembok, bukan ditarik maju ke MC.")]
    [SerializeField] private float castRadius = 0.25f;
    [Tooltip("Jarak kamera dari permukaan tembok.")]
    [SerializeField] private float wallMargin = 0.2f;
    [Tooltip("Kamera tidak lebih tinggi dari puncak tembok yang menahannya.")]
    [SerializeField] private bool capAtWallTop = true;
    [Tooltip("Jarak kamera di bawah puncak tembok.")]
    [SerializeField] private float wallTopMargin = 0.05f;
    [Tooltip("Tinggi minimum kamera dari kaki MC saat dibatasi puncak tembok.")]
    [SerializeField] private float minimumHeight = 1.3f;
    [Tooltip("Waktu kembali ke posisi orbit normal setelah tembok tidak menahan lagi.")]
    [SerializeField] private float releaseDamping = 0.4f;
    [Tooltip("Waktu geser lensa menyamping menjauhi tembok yang terserempet (meredam getar).")]
    [SerializeField] private float lensPushDamping = 0.08f;
    [Tooltip("Saat tembok menarik kamera sedekat ini ke MC (jarak datar), kamera mulai naik sedikit supaya kepala MC " +
             "tidak memenuhi layar. Tetap tidak lebih tinggi dari puncak tembok.")]
    [SerializeField] private float closeLiftStartDistance = 1.2f;
    [Tooltip("Jarak datar di mana kamera sudah di Close Camera Height.")]
    [SerializeField] private float closeLiftFullDistance = 0.4f;
    [Tooltip("Tinggi kamera dari kaki MC saat paling dekat (dibatasi puncak tembok).")]
    [SerializeField] private float closeCameraHeight = 1.9f;
    [Header("Sensitivitas Dekat Tembok")]
    [Tooltip("Sensitivitas putar mulai dikurangi hanya jika tembok menahan kamera sedekat ini ke MC (jarak datar). " +
             "Jika kamera masih jauh dari MC (obstacle/tower jauh di belakang), sensitivitas normal.")]
    [SerializeField] private float holdDistance = 1.5f;
    [Tooltip("Jarak datar kamera-MC di mana sensitivitas paling rendah.")]
    [SerializeField] private float slowestDistance = 0.5f;
    [Tooltip("Pengali sensitivitas paling rendah (memutar kamera masuk ke tembok saat kamera menempel MC).")]
    [Range(0.05f, 1f)]
    [SerializeField] private float nearWallSensitivity = 0.2f;

    [Header("Arah Pandang")]
    [Tooltip("Tinggi titik fokus dari kaki MC, sama dengan Target Offset orbit.")]
    [SerializeField] private float pivotHeight = 1.1f;
    [Tooltip("Kamera selalu memandang titik fokus MC setelah posisinya dikoreksi (tanpa jeda damping).")]
    [SerializeField] private bool lockAimToTarget = true;
    [Tooltip("Tinggi titik pandang dari kaki MC saat kamera paling dekat karena tembok (kira-kira tengah badan MC).")]
    [SerializeField] private float closeAimHeight = 0.45f;
    [Tooltip("Titik pandang mulai turun saat jarak datar kamera tinggal fraksi ini dari jarak orbit normal.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float lowerAimStartRatio = 0.9f;
    [Tooltip("Titik pandang sudah di Close Aim Height saat jarak datar kamera tinggal fraksi ini dari jarak orbit normal.")]
    [Range(0f, 0.9f)]
    [SerializeField] private float lowerAimFullRatio = 0.35f;
    [Tooltip("Kaki MC dijaga tetap di atas batas ini (fraksi tinggi layar dari bawah). Atur sedikit di atas bar HP.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float feetMinViewportY = 0.15f;
    [Tooltip("Sudut tunduk maksimum kamera (derajat).")]
    [Range(30f, 89f)]
    [SerializeField] private float maxPitch = 80f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private class ExtraState : VcamExtraStateBase
    {
        // Jarak datar kamera-MC yang dipakai (sudah dibatasi tembok, dengan damping saat kembali).
        public float flat = -1f;
        // Jarak datar orbit normal (tanpa tembok).
        public float rawFlat;
        // Selisih tinggi kamera dari tinggi orbit: negatif = diturunkan ke puncak tembok, positif = dinaikkan saat dekat MC.
        public float heightOffset;
        // Geseran lensa menyamping menjauhi tembok yang terserempet (diredam).
        public Vector3 push;
        // Arah datar dari kamera ke MC menurut orbit. Yaw kamera selalu mengikuti arah ini (stabil),
        // karena PlayerMotor memakai yaw kamera sebagai arah maju MC.
        public Vector3 orbitForward = Vector3.forward;
        public bool clamping;
        // Tembok benar-benar menahan kamera frame ini (bukan sekadar sisa damping saat kembali).
        public bool holding;
        // Jarak datar aman kamera-MC frame ini (sudah dibatasi tembok).
        public float allowed;
        public Vector3 feet;
        public Vector3 pivot;
        public Vector3 castOrigin;
        public Vector3 castEnd;
        public bool hit;
    }

    private ExtraState lastState;

    public bool IsClamping => lastState != null && lastState.clamping;

    // True hanya selama tembok benar-benar menahan kamera cukup dalam frame ini. Dipakai untuk mengunci rotasi kamera.
    // Tidak termasuk masa kembali (damping) setelah MC menjauh, supaya kontrol langsung bisa dipakai lagi.
    public bool IsHoldingCamera => lastState != null && lastState.holding;

    // Pengali sensitivitas putar saat kamera ditahan tembok dekat MC. 1 = normal.
    // Turun bertahap dari Hold Distance sampai Slowest Distance.
    public float SensitivityScale
    {
        get
        {
            if (lastState == null || !lastState.holding)
                return 1f;

            float t = Mathf.InverseLerp(slowestDistance, holdDistance, lastState.allowed);
            return Mathf.Lerp(nearWallSensitivity, 1f, t);
        }
    }

    // Apakah memutar yaw orbit dari Yaw sebesar Delta membuat kamera makin terdesak tembok (jarak aman makin pendek)?
    // Dipakai rig supaya putaran KE tembok diperlambat, tapi putaran keluar dari tembok tetap normal
    // (kamera tidak tersangkut di atas kepala MC).
    public bool YawMovesIntoWall(float yaw, float delta)
    {
        if (lastState == null || !lastState.holding || Mathf.Abs(delta) < 0.0001f || lastState.rawFlat < 0.01f)
            return false;

        // Langkah uji minimal 2 derajat supaya hasilnya stabil untuk gerakan mouse yang sangat kecil.
        float step = Mathf.Sign(delta) * Mathf.Max(Mathf.Abs(delta), 2f);
        Vector3 origin = lastState.feet + Vector3.up * castHeight;
        float now = AllowedFlatDistance(origin, YawDirection(yaw), lastState.rawFlat, out _, out _);
        float next = AllowedFlatDistance(origin, YawDirection(yaw + step), lastState.rawFlat, out _, out _);
        return next < now - 0.001f;
    }

    // Arah datar dari MC ke kamera untuk yaw orbit tertentu (Orbital Follow: kamera di belakang arah yaw).
    private static Vector3 YawDirection(float yaw)
    {
        return Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
    }

    private int Mask => wallLayers.value != 0 ? wallLayers.value : LayerMask.GetMask(GameLayers.NavBlock);

    // Dipanggil ThirdPersonCameraRig saat komponen ini ditambahkan lewat kode.
    public void Configure(float pivot)
    {
        pivotHeight = pivot;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        Transform follow = vcam.Follow;

        if (follow == null)
            return;

        if (stage == CinemachineCore.Stage.Body)
            SolvePosition(vcam, follow, ref state, deltaTime);
        else if (stage == CinemachineCore.Stage.Aim && lockAimToTarget)
            SolveAim(vcam, ref state);
    }

    private void SolvePosition(CinemachineVirtualCameraBase vcam, Transform follow, ref CameraState state, float deltaTime)
    {
        ExtraState extra = GetExtraState<ExtraState>(vcam);
        lastState = extra;

        // Posisi orbit asli (sudah termasuk damping orbit), sebelum koreksi komponen lain.
        Vector3 raw = state.RawPosition;
        Vector3 feet = follow.position;
        extra.feet = feet;
        extra.pivot = feet + Vector3.up * pivotHeight;

        Vector3 flatOffset = new Vector3(raw.x - feet.x, 0f, raw.z - feet.z);
        float flatDistance = flatOffset.magnitude;
        extra.rawFlat = flatDistance;

        if (flatDistance < 0.01f)
        {
            extra.flat = flatDistance;
            extra.clamping = false;
            extra.holding = false;
            extra.heightOffset = 0f;
            extra.push = Vector3.zero;
            state.PositionCorrection = Vector3.zero;
            return;
        }

        Vector3 direction = flatOffset / flatDistance;
        extra.orbitForward = -direction;

        // Batas tembok frame ini, hanya dari garis tipis yang benar-benar terpotong:
        //   1. mendatar setinggi lutut MC ke arah kamera (kamera berada di balik tembok),
        //   2. garis pandang dada MC -> kamera (tembok/pinggiran atas tembok menutupi pandangan).
        extra.castOrigin = feet + Vector3.up * castHeight;
        float allowed = AllowedFlatDistance(extra.castOrigin, direction, flatDistance, out RaycastHit wallHit, out bool hitLow);
        bool hitSight = false;
        Vector3 toRaw = raw - extra.pivot;
        float rawDistance = toRaw.magnitude;

        if (rawDistance > 0.01f &&
            WallRaycast(extra.pivot, toRaw / rawDistance, out RaycastHit sight, rawDistance + wallMargin))
        {
            float sightAllowed = Mathf.Clamp(flatDistance * (sight.distance - wallMargin) / rawDistance, 0f, flatDistance);

            if (sightAllowed < allowed)
            {
                allowed = sightAllowed;
                wallHit = sight;
            }

            hitSight = true;
        }

        extra.hit = hitLow || hitSight;
        extra.castEnd = extra.castOrigin + direction * Mathf.Min(flatDistance, allowed + wallMargin);

        // Batas tinggi: puncak tembok yang menahan kamera (tidak dibatasi jika tidak ada tembok).
        float capY = float.PositiveInfinity;

        if (!keepViewAngle && extra.hit && capAtWallTop && TryGetWallTop(wallHit, feet, out float wallTop))
            capY = Mathf.Max(wallTop - wallTopMargin, feet.y + minimumHeight);

        bool reset = !vcam.PreviousStateIsValid || deltaTime < 0f || extra.flat < 0f;
        float k = releaseDamping > 0f && !reset ? 1f - Mathf.Exp(-deltaTime / releaseDamping) : 1f;

        // Kembali menjauh dengan damping, tapi tidak pernah melewati batas tembok frame ini (tanpa damping).
        extra.flat = reset ? allowed : Mathf.Min(Mathf.Lerp(extra.flat, allowed, k), allowed);

        // Damping eksponensial tidak pernah benar-benar sampai: selesaikan saat sudah cukup dekat.
        if (allowed - extra.flat < 0.02f)
            extra.flat = allowed;

        // Tinggi: naik sedikit saat kamera sangat dekat MC, tapi tidak melebihi puncak tembok.
        float liftT = keepViewAngle
            ? 0f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(closeLiftStartDistance, closeLiftFullDistance, extra.flat));
        float targetY = Mathf.Max(raw.y, Mathf.Lerp(raw.y, feet.y + closeCameraHeight, liftT));
        targetY = Mathf.Min(targetY, capY);
        float targetOffset = targetY - raw.y;
        extra.heightOffset = Mathf.Min(reset ? targetOffset : Mathf.Lerp(extra.heightOffset, targetOffset, k),
            capY - raw.y);

        if (Mathf.Abs(extra.heightOffset - targetOffset) < 0.01f)
            extra.heightOffset = targetOffset;

        extra.allowed = allowed;
        extra.holding = extra.hit && allowed < flatDistance - 0.05f && allowed < holdDistance;
        extra.clamping = extra.flat < flatDistance - 0.001f || Mathf.Abs(extra.heightOffset) > 0.001f;

        Vector3 solved = raw;

        if (extra.clamping)
        {
            if (keepViewAngle)
            {
                // Meluncur di garis titik fokus -> posisi orbit: jarak mendatar jadi Flat, sudut pandang tetap sama.
                solved = extra.pivot + (raw - extra.pivot) * (extra.flat / flatDistance);
            }
            else
            {
                // Digeser mendatar: kamera di depan tembok pada tinggi orbitnya (diatur heightOffset).
                solved = feet + direction * extra.flat;
                solved.y = raw.y + extra.heightOffset;
            }

            // Pengaman: garis dari titik fokus ke kamera tidak boleh menembus tembok.
            Vector3 toSolved = solved - extra.pivot;
            float solvedDistance = toSolved.magnitude;

            if (solvedDistance > 0.01f &&
                WallRaycast(extra.pivot, toSolved / solvedDistance, out RaycastHit block, solvedDistance + wallMargin))
            {
                solved = extra.pivot + toSolved / solvedDistance * Mathf.Max(0f, block.distance - wallMargin);
            }
        }

        // Geser lensa menjauhi tembok yang terserempet, diredam supaya tidak bergetar.
        Vector3 targetPush = PushLensOffWalls(solved, extra.pivot) - solved;
        float pushK = reset || lensPushDamping <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / lensPushDamping);
        extra.push = Vector3.Lerp(extra.push, targetPush, pushK);

        if (extra.push.sqrMagnitude < 0.000001f)
            extra.push = Vector3.zero;

        solved += extra.push;
        state.PositionCorrection = solved - raw;
    }

    private readonly Collider[] lensOverlaps = new Collider[8];
    private readonly RaycastHit[] rayHits = new RaycastHit[16];

    // Seperti Physics.Raycast ke layer tembok, tapi prop yang boleh ditembus kamera (CameraSeeThroughProp) dilewati.
    // Prop itu dipudarkan CameraOccluderFade, jadi kamera tidak perlu maju-mundur setiap melewati prop.
    private bool WallRaycast(Vector3 origin, Vector3 direction, out RaycastHit nearest, float length)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, rayHits, length, Mask, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        bool found = false;
        nearest = default;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];

            if (CameraSeeThroughProp.IsSeeThrough(hit.collider) || hit.distance >= best)
                continue;

            best = hit.distance;
            nearest = hit;
            found = true;
        }

        return found;
    }

    // Lensa terlalu dekat ke permukaan tembok (terserempet, bukan terhalang): geser menyamping menjauhi permukaan.
    // Hanya mendatar, dan dibatalkan jika pergeseran itu malah membuat garis pandang ke MC terpotong.
    private Vector3 PushLensOffWalls(Vector3 camera, Vector3 pivot)
    {
        int count = Physics.OverlapSphereNonAlloc(camera, castRadius, lensOverlaps, Mask, QueryTriggerInteraction.Ignore);
        Vector3 pushed = camera;

        for (int i = 0; i < count; i++)
        {
            Collider wall = lensOverlaps[i];

            if (wall is MeshCollider mesh && !mesh.convex || wall is TerrainCollider)
                continue;

            if (CameraSeeThroughProp.IsSeeThrough(wall))
                continue;

            Vector3 closest = wall.ClosestPoint(pushed);
            Vector3 away = pushed - closest;
            away.y = 0f;
            float distance = away.magnitude;

            // Pusat lensa sudah di dalam collider: biarkan pemeriksaan garis yang menanganinya.
            if (distance < 0.0001f || distance >= castRadius)
                continue;

            pushed += away / distance * (castRadius - distance);
        }

        if (pushed == camera)
            return camera;

        Vector3 toPushed = pushed - pivot;
        float length = toPushed.magnitude;

        if (length > 0.01f &&
            WallRaycast(pivot, toPushed / length, out _, length))
        {
            return camera;
        }

        return pushed;
    }

    // Jarak datar terjauh yang aman dari origin ke arah kamera. Hanya raycast tipis: SphereCast menabrak tembok/pilar
    // yang hanya terserempet saat MC berjalan menyusuri tembok, lalu menarik kamera maju ke MC tanpa alasan.
    // Jarak lensa ke tembok ditangani Wall Margin dan PushLensOffWalls.
    private float AllowedFlatDistance(Vector3 origin, Vector3 direction, float distance, out RaycastHit wall, out bool hit)
    {
        float allowed = distance;
        float length = distance + wallMargin;
        hit = false;
        wall = default;

        if (WallRaycast(origin, direction, out RaycastHit ray, length))
        {
            allowed = Mathf.Min(allowed, ray.distance - wallMargin);
            wall = ray;
            hit = true;
        }

        return Mathf.Clamp(allowed, 0f, distance);
    }

    // Puncak tembok: raycast ke bawah sedikit di dalam tembok, di titik yang ditabrak.
    private bool TryGetWallTop(RaycastHit hit, Vector3 feet, out float top)
    {
        top = 0f;

        if (hit.collider == null)
            return false;

        Vector3 inside = hit.point - hit.normal * 0.05f;
        Vector3 from = new Vector3(inside.x, feet.y + 30f, inside.z);

        if (WallRaycast(from, Vector3.down, out RaycastHit topHit, 40f) &&
            topHit.collider == hit.collider)
        {
            top = topHit.point.y;
            return true;
        }

        top = hit.collider.bounds.max.y;
        return true;
    }

    private void SolveAim(CinemachineVirtualCameraBase vcam, ref CameraState state)
    {
        ExtraState extra = GetExtraState<ExtraState>(vcam);
        Vector3 camera = state.GetCorrectedPosition();

        // Titik pandang turun bertahap dari titik fokus ke badan MC sesuai seberapa jauh tembok menarik kamera.
        float aimHeight = pivotHeight;

        if (!keepViewAngle && extra.clamping && extra.rawFlat > 0.01f)
        {
            float ratio = extra.flat / extra.rawFlat;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lowerAimStartRatio, lowerAimFullRatio, ratio));
            aimHeight = Mathf.Lerp(pivotHeight, closeAimHeight, t);
        }

        Vector3 toAim = extra.feet + Vector3.up * aimHeight - camera;

        // Yaw kamera = yaw orbit (stabil). Jangan pakai LookRotation ke MC: saat kamera hampir tepat di atas MC,
        // yaw hasil LookRotation berbalik-balik mengikuti geseran kecil MC. PlayerMotor memakai yaw kamera sebagai
        // arah maju, jadi MC ikut berbalik arah (stuck) dan kamera bergetar.
        Vector3 forward = extra.orbitForward;

        if (forward.sqrMagnitude < 0.0001f)
            return;

        // Pitch saja yang dihitung untuk memandang titik fokus, dibatasi Max Pitch.
        float along = Vector3.Dot(toAim, forward);
        float pitch = Mathf.Atan2(-toAim.y, Mathf.Max(0.01f, along)) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, -80f, maxPitch);

        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
        // Gaya sudut tetap: kamera tidak menunduk tiba-tiba untuk menjaga kaki MC di atas HUD.
        if (!keepViewAngle)
            rotation = KeepFeetAboveHud(rotation, camera, extra.feet, state.Lens.FieldOfView);

        state.OrientationCorrection = Quaternion.Inverse(state.RawOrientation) * rotation;
    }

    // Jika kaki MC berada di bawah batas HUD, kamera menunduk secukupnya. Kaki MC diutamakan.
    private Quaternion KeepFeetAboveHud(Quaternion rotation, Vector3 camera, Vector3 feet, float verticalFov)
    {
        float tanHalf = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
        float bottomAngle = Mathf.Atan(tanHalf * (2f * feetMinViewportY - 1f)) * Mathf.Rad2Deg;

        Quaternion inverse = Quaternion.Inverse(rotation);
        float feetAngle = VerticalAngle(inverse * (feet - camera));

        if (feetAngle >= bottomAngle)
            return rotation;

        float delta = bottomAngle - feetAngle;

        // Sudut tunduk sekarang (positif = menunduk), dibatasi Max Pitch.
        float currentPitch = -Mathf.Asin(Mathf.Clamp((rotation * Vector3.forward).y, -1f, 1f)) * Mathf.Rad2Deg;
        delta = Mathf.Min(delta, Mathf.Max(0f, maxPitch - currentPitch));

        // Putar di sumbu kanan kamera: sudut positif = menunduk.
        return delta > 0f ? rotation * Quaternion.Euler(delta, 0f, 0f) : rotation;
    }

    // Sudut vertikal titik relatif arah pandang kamera (derajat, negatif = di bawah tengah layar).
    private static float VerticalAngle(Vector3 local)
    {
        return Mathf.Atan2(local.y, Mathf.Max(0.0001f, local.z)) * Mathf.Rad2Deg;
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
