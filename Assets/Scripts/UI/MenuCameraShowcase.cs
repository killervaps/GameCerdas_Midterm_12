using UnityEngine;

// Kamera "showcase" untuk menu utama: close-up perspektif setinggi mata di depan MC yang berdiri di Safezone,
// dengan MC di sisi kanan layar (kolom menu ada di kiri) dan ayunan pelan supaya terasa hidup.
// Saat menu ditutup, kamera meluncur halus kembali ke pose TopDownCameraFollow lalu follow diaktifkan lagi.
//
// Ditambahkan otomatis oleh GameUI ke kamera utama. Nilai di bawah bisa diatur di Inspector saat Play.
[DefaultExecutionOrder(100)]
public class MenuCameraShowcase : MonoBehaviour
{
    [Header("Framing")]
    [SerializeField, Range(15f, 60f)] private float fieldOfView = 30f;
    // Tinggi area yang terlihat secara vertikal, dalam kelipatan tinggi karakter.
    [SerializeField] private float framingHeight = 1.25f;
    // Titik fokus vertikal (0 = kaki, 1 = puncak kepala).
    [SerializeField, Range(0f, 1f)] private float focusHeight = 0.58f;
    // Geser karakter ke kanan layar, dalam fraksi lebar layar (0 = tengah).
    [SerializeField, Range(-0.4f, 0.4f)] private float screenOffset = 0.2f;
    // Kamera sedikit di atas titik fokus dan menunduk sedikit.
    [SerializeField] private float cameraLift = 0.12f;
    // Sudut pandang 3/4: kamera diputar sekian derajat dari arah depan karakter.
    [SerializeField] private float yawFromFront = 18f;

    [Header("Motion")]
    [SerializeField] private float swayAngle = 5f;
    [SerializeField] private float swayPeriod = 9f;
    [SerializeField] private float blendInDuration = 0.6f;
    [SerializeField] private float blendOutDuration = 1.1f;

    [Header("Obstacles")]
    // Arah kamera dicari yang tidak tertutup dinding. Karakter diputar menghadap kamera.
    [SerializeField] private bool avoidWalls = true;
    [SerializeField] private float clearanceRadius = 0.25f;

    private enum Mode
    {
        Off,
        Showcase,
        BlendOut
    }

    private Camera cam;
    private TopDownCameraFollow follow;
    private Transform target;

    private Mode mode = Mode.Off;
    private Vector3 baseDirection = Vector3.forward;
    private float characterHeight = 1.8f;
    private float blendTime;
    private Vector3 fromPosition;
    private Quaternion fromRotation;
    private float fromFov;
    private float showcaseTime;

    public bool IsActive => mode != Mode.Off;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        follow = GetComponent<TopDownCameraFollow>();
    }

    public void Begin(Transform character, PlayerMotor motor)
    {
        if (character == null || cam == null)
            return;

        target = character;
        characterHeight = MeasureHeight(character);
        baseDirection = ChooseDirection(character);

        // Karakter menghadap kamera.
        Vector3 facing = Quaternion.Euler(0f, -yawFromFront, 0f) * baseDirection;
        Quaternion rotation = Quaternion.LookRotation(facing, Vector3.up);

        if (motor != null)
            motor.Teleport(character.position, rotation);
        else
            character.rotation = rotation;

        if (follow != null)
            follow.enabled = false;

        fromPosition = transform.position;
        fromRotation = transform.rotation;
        fromFov = cam.fieldOfView;
        blendTime = mode == Mode.Off ? 0f : blendInDuration;
        showcaseTime = 0f;
        mode = Mode.Showcase;
    }

    public void End()
    {
        if (mode != Mode.Showcase)
            return;

        fromPosition = transform.position;
        fromRotation = transform.rotation;
        fromFov = cam.fieldOfView;
        blendTime = 0f;
        mode = Mode.BlendOut;
    }

    private void LateUpdate()
    {
        if (mode == Mode.Off || cam == null)
            return;

        float dt = Time.unscaledDeltaTime;

        if (mode == Mode.Showcase)
        {
            if (target == null)
            {
                Finish();
                return;
            }

            showcaseTime += dt;
            GetShowcasePose(showcaseTime, out Vector3 position, out Quaternion rotation);

            blendTime += dt;
            float t = Smooth(blendInDuration > 0f ? blendTime / blendInDuration : 1f);

            transform.SetPositionAndRotation(
                Vector3.Lerp(fromPosition, position, t),
                Quaternion.Slerp(fromRotation, rotation, t));
            cam.fieldOfView = Mathf.Lerp(fromFov, fieldOfView, t);
            return;
        }

        // BlendOut: menuju pose kamera gameplay.
        blendTime += dt;
        float k = Smooth(blendOutDuration > 0f ? blendTime / blendOutDuration : 1f);

        Vector3 toPosition = follow != null ? follow.DesiredPosition : fromPosition;
        Quaternion toRotation = follow != null ? follow.DesiredRotation : fromRotation;
        float toFov = follow != null ? follow.FieldOfView : fromFov;

        transform.SetPositionAndRotation(
            Vector3.Lerp(fromPosition, toPosition, k),
            Quaternion.Slerp(fromRotation, toRotation, k));
        cam.fieldOfView = Mathf.Lerp(fromFov, toFov, k);

        if (k >= 1f)
            Finish();
    }

    private void Finish()
    {
        mode = Mode.Off;

        if (follow != null)
        {
            follow.enabled = true;
            follow.SnapToTarget();
        }
    }

    private void GetShowcasePose(float time, out Vector3 position, out Quaternion rotation)
    {
        float sway = swayPeriod > 0f ? Mathf.Sin(time * Mathf.PI * 2f / swayPeriod) * swayAngle : 0f;
        Vector3 direction = Quaternion.Euler(0f, sway, 0f) * baseDirection;

        Vector3 focus = target.position + Vector3.up * (characterHeight * focusHeight);
        float halfFov = fieldOfView * 0.5f * Mathf.Deg2Rad;
        float distance = characterHeight * framingHeight * 0.5f / Mathf.Tan(halfFov);

        Vector3 eye = focus + direction * distance + Vector3.up * (characterHeight * cameraLift);
        rotation = Quaternion.LookRotation(focus - eye, Vector3.up);

        // Geser kamera ke kiri (arah pandang tetap) supaya karakter tampil di kanan layar.
        float viewWidth = 2f * distance * Mathf.Tan(halfFov) * cam.aspect;
        position = eye - (rotation * Vector3.right) * (screenOffset * viewWidth);
    }

    // Arah dari karakter ke kamera. Utamakan depan karakter, cari arah lain jika tertutup dinding.
    private Vector3 ChooseDirection(Transform character)
    {
        Vector3 forward = character.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.back;

        // Default: menghadap kamera gameplay (ke bawah layar top-down), seperti pose di Safezone.
        if (follow != null)
        {
            Vector3 towardCamera = follow.DesiredRotation * Vector3.back;
            towardCamera.y = 0f;

            if (towardCamera.sqrMagnitude > 0.001f)
                forward = towardCamera.normalized;
        }

        if (!avoidWalls)
            return forward;

        float halfFov = fieldOfView * 0.5f * Mathf.Deg2Rad;
        float needed = characterHeight * framingHeight * 0.5f / Mathf.Tan(halfFov) + 0.5f;
        Vector3 origin = character.position + Vector3.up * (characterHeight * focusHeight);

        Vector3 best = forward;
        float bestFree = -1f;
        float[] angles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };

        foreach (float angle in angles)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
            float free = needed;

            if (Physics.SphereCast(origin, clearanceRadius, direction, out RaycastHit hit, needed,
                    GameLayers.ObstacleMask | LayerMask.GetMask(GameLayers.Ground), QueryTriggerInteraction.Ignore))
                free = hit.distance;

            if (free >= needed - 0.01f)
                return direction;

            if (free > bestFree)
            {
                bestFree = free;
                best = direction;
            }
        }

        return best;
    }

    private static float MeasureHeight(Transform character)
    {
        Bounds bounds = new Bounds(character.position, Vector3.zero);
        bool found = false;

        foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>())
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        CharacterController controller = character.GetComponent<CharacterController>();
        float capsule = controller != null ? controller.height * character.lossyScale.y : 1.8f;

        // Renderer bisa ikut menghitung efek/teks yang menempel di MC, jadi hasilnya dibatasi terhadap kapsul.
        if (found)
        {
            float height = bounds.max.y - character.position.y;

            if (height > capsule * 0.5f && height < capsule * 1.6f)
                return height;
        }

        return capsule;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
