using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public enum GameplayCameraMode
{
    TopDown,
    ThirdPerson
}

// Kamera gameplay di kamera utama. Dua mode:
//   Top Down      kamera miring yang mengikuti MC, yaw tetap supaya arah WASD konsisten (bawaan).
//   Third Person  kamera orang ketiga Cinemachine (ThirdPersonCameraRig). Komponen ini menyalakan CinemachineBrain
//                 dan meneruskan Snap/pose ke rig, jadi GateSelectionRoom dan MenuCameraShowcase tidak perlu tahu mode.
// F8 berganti mode saat Play. Setup Third Person lewat menu MathGate > Camera.
public class TopDownCameraFollow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform target;

    [Header("Mode")]
    [SerializeField] private GameplayCameraMode mode = GameplayCameraMode.TopDown;
    // Diisi menu MathGate > Camera > Setup Third Person Camera. Kosong = selalu Top Down.
    [SerializeField] private ThirdPersonCameraRig thirdPerson;
    // F8 berganti Top Down / Third Person saat Play, untuk membandingkan.
    [SerializeField] private bool toggleHotkey = true;

    [Header("Framing")]
    [SerializeField, Range(30f, 89f)] private float pitch = 52f;
    // 0 atau 45 derajat, tidak berubah saat bermain.
    [SerializeField] private float yaw = 0f;
    [SerializeField] private float distance = 16f;
    // Titik yang dilihat kamera relatif terhadap pivot target (kira-kira setinggi dada MC).
    [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1f, 0f);

    [Header("Lens")]
    [SerializeField, Range(20f, 60f)] private float fieldOfView = 38f;

    [Header("Smoothing")]
    // Waktu kejar posisi target. 0 berarti menempel tanpa damping.
    [SerializeField] private float smoothTime = 0.3f;

    private Vector3 velocity;
    private CinemachineBrain brain;
    private Camera cam;

    public GameplayCameraMode Mode => UsesThirdPerson ? GameplayCameraMode.ThirdPerson : GameplayCameraMode.TopDown;

    // Yaw tetap kamera top-down (derajat). Minimap memutar petanya dengan nilai ini supaya atas peta = atas layar.
    public float Yaw => yaw;

    private bool UsesThirdPerson => mode == GameplayCameraMode.ThirdPerson && thirdPerson != null;

    public bool HasThirdPersonRig => thirdPerson != null;

    public Transform Target
    {
        get => target;
        set
        {
            target = value;
            SnapToTarget();
        }
    }

    // Pose yang dituju kamera saat ini. Dipakai MenuCameraShowcase untuk transisi halus kembali ke gameplay.
    public Vector3 DesiredPosition =>
        UsesThirdPerson ? thirdPerson.DesiredPosition : target != null ? GetDesiredPosition() : transform.position;

    public Quaternion DesiredRotation => UsesThirdPerson ? thirdPerson.DesiredRotation : GetRotation();

    public float FieldOfView => UsesThirdPerson ? thirdPerson.FieldOfView : fieldOfView;

    private void Awake()
    {
        brain = GetComponent<CinemachineBrain>();
        cam = GetComponent<Camera>();
    }

    // MenuCameraShowcase mematikan komponen ini selama menu utama tampil. Brain ikut mati supaya tidak berebut kamera.
    private void OnEnable()
    {
        ApplyDriver();
    }

    private void OnDisable()
    {
        if (brain != null)
            brain.enabled = false;

        if (thirdPerson != null)
            thirdPerson.SetDriving(false, transform);
    }

    private void Start()
    {
        ApplyLens();
        SnapToTarget();
    }

    public void SetMode(GameplayCameraMode next)
    {
        mode = next;
        ApplyDriver();
        SnapToTarget();
    }

    private void ApplyDriver()
    {
        bool third = UsesThirdPerson && isActiveAndEnabled;

        if (brain != null)
            brain.enabled = third;

        if (thirdPerson != null)
            thirdPerson.SetDriving(third, transform);
    }

    private void Update()
    {
        if (!toggleHotkey || Keyboard.current == null || !Keyboard.current.f8Key.wasPressedThisFrame)
            return;

        if (thirdPerson == null)
        {
            Debug.LogWarning("Kamera: F8 butuh kamera orang ketiga. Hentikan Play lalu jalankan MathGate > Camera > Setup Third Person Camera.", this);
            return;
        }

        SetMode(UsesThirdPerson ? GameplayCameraMode.TopDown : GameplayCameraMode.ThirdPerson);
        Debug.Log("Kamera: " + Mode, this);
    }

    private void LateUpdate()
    {
        // Third Person: posisi kamera diatur CinemachineBrain.
        if (target == null || UsesThirdPerson)
            return;

        transform.rotation = GetRotation();

        Vector3 desired = GetDesiredPosition();

        transform.position = smoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime)
            : desired;
    }

    private Quaternion GetRotation()
    {
        return Quaternion.Euler(pitch, yaw, 0f);
    }

    private Vector3 GetDesiredPosition()
    {
        Vector3 focus = target.position + lookOffset;

        return focus - GetRotation() * Vector3.forward * distance;
    }

    private void ApplyLens()
    {
        if (cam == null)
            cam = GetComponent<Camera>();

        if (cam != null)
            cam.fieldOfView = fieldOfView;
    }

    // Bisa dipanggil dari menu titik tiga komponen di Inspector untuk melihat framing tanpa Play Mode.
    [ContextMenu("Snap ke Target")]
    public void SnapToTarget()
    {
        if (UsesThirdPerson)
        {
            thirdPerson.Snap();
            return;
        }

        if (target == null)
            return;

        transform.rotation = GetRotation();
        transform.position = GetDesiredPosition();
        velocity = Vector3.zero;

        ApplyLens();
    }
}
