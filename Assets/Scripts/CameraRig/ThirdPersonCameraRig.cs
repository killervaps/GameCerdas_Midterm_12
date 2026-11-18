using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Kamera orang ketiga ala Genshin dengan Cinemachine 3. Dipasang lewat menu MathGate > Camera > Setup Third Person Camera.
//   Orbit   mouse (tahan klik kanan) atau stik kanan memutar kamera di sekitar MC (CinemachineOrbitalFollow).
//   Tembok  CinemachineDeoccluder menarik kamera ke depan tembok (layer NavBlock dan Ground): masuk cepat, keluar pelan.
//   Lock    saat TargetSelector mengunci musuh, kamera berputar sendiri ke belakang MC menghadap musuh itu.
//   Zoom    scroll mouse mendekat/menjauh (RadialAxis Orbital Follow, pengali Radius).
//   Fade    jika kamera terlalu dekat dengan MC, renderer MC diubah ke Shadows Only: badan hilang, bayangan tetap ada.
// Hanya bekerja jika TopDownCameraFollow di kamera utama memakai mode Third Person. TopDownCameraFollow yang
// menyalakan CinemachineBrain, jadi teleport, menu showcase, dan tombol ganti mode (F8) tetap lewat satu pintu.
[RequireComponent(typeof(CinemachineCamera))]
[RequireComponent(typeof(CinemachineOrbitalFollow))]
public class ThirdPersonCameraRig : MonoBehaviour
{
    [Header("References")]
    // Kosongkan untuk memakai MC (PlayerMotor) di scene.
    [SerializeField] private Transform target;

    [Header("Input")]
    [SerializeField] private string lookActionName = "Player/Look";
    // Mouse hanya memutar kamera selama klik kanan ditahan, supaya kursor tetap bebas untuk UI.
    [SerializeField] private bool mouseRequiresRightButton = true;
    // Derajat per piksel gerakan mouse.
    [SerializeField] private float mouseSensitivity = 0.15f;
    // Derajat per detik saat stik kanan ditekan penuh (horizontal, vertikal).
    [SerializeField] private Vector2 stickSpeed = new Vector2(180f, 110f);
    [SerializeField] private bool invertY;

    [Header("Lock-On")]
    // Saat musuh dikunci (tombol CycleTarget), kamera berputar sendiri ke belakang MC menghadap musuh itu.
    [SerializeField] private bool followLockedTarget = true;
    // Waktu kejar arah musuh. Kecil = kamera langsung menghadap, besar = berputar lebih halus.
    [SerializeField] private float lockSmoothTime = 0.15f;
    // Selama lock, putaran kiri-kanan dari mouse/stik diabaikan (naik-turun dan zoom tetap bisa).
    [SerializeField] private bool blockManualYawWhileLocked = true;
    // Musuh lebih dekat dari ini (jarak datar) tidak dikejar, supaya kamera tidak berputar liar saat musuh menempel.
    [SerializeField] private float lockMinDistance = 1.2f;

    [Header("Zoom")]
    // Pengali Radius Orbital Follow. 1 = Radius apa adanya, 0,6 = 60% lebih dekat.
    [SerializeField] private Vector2 zoomRange = new Vector2(0.6f, 1.5f);
    // Perubahan pengali per satu klik scroll.
    [SerializeField] private float zoomStep = 0.1f;

    [Header("Fade Saat Kamera Dekat")]
    [SerializeField] private bool fadeWhenClose = true;
    // Jarak kamera ke titik fokus MC. Di bawah Hide Distance badan disembunyikan, muncul lagi di atas Show Distance
    // (dua batas supaya tidak berkedip saat kamera tepat di batas).
    [SerializeField] private float hideDistance = 1.0f;
    [SerializeField] private float showDistance = 1.3f;
    // Saat pindah arena (teleport), kamera diputar ke belakang MC.
    [SerializeField] private bool snapBehindTargetOnTeleport = true;
    // MC dianggap teleport jika berpindah sejauh ini sejak frame sebelumnya.
    [SerializeField] private float teleportDistance = 3f;

    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<ShadowCastingMode> originalModes = new List<ShadowCastingMode>();

    private CinemachineCamera cmCamera;
    private CinemachineOrbitalFollow orbit;
    private InputAction lookAction;
    private Transform outputCamera;
    private bool driving;
    private bool hidden;
    private bool cursorLocked;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;
    private Vector3 lastTargetPosition;
    private TargetSelector targetSelector;
    private float lockYawVelocity;
    private bool hasLastTargetPosition;

    public bool IsDriving => driving;

    // Kamera utama yang sedang digerakkan rig ini (null jika tidak aktif).
    public Transform OutputCamera => driving ? outputCamera : null;

    // Musuh yang sedang diikuti kamera, atau null.
    public EnemyHealth LockedEnemy
    {
        get
        {
            if (!followLockedTarget || targetSelector == null || !targetSelector.IsLocked)
                return null;

            EnemyHealth enemy = targetSelector.LockedTarget;
            return enemy != null && !enemy.IsDead ? enemy : null;
        }
    }

    public bool IsTargetHidden => hidden;

    public float FieldOfView => Rig.Lens.FieldOfView;

    // Titik yang diorbit dan dilihat kamera (kira-kira setinggi dada MC).
    public Vector3 FocusPoint => target != null ? target.position + Orbit.TargetOffset : transform.position;

    // Pose kamera tanpa koreksi tembok. Dipakai MenuCameraShowcase untuk meluncur kembali ke gameplay
    // saat Brain belum menyala.
    public Quaternion DesiredRotation => Quaternion.Euler(Orbit.VerticalAxis.Value, Orbit.HorizontalAxis.Value, 0f);

    public Vector3 DesiredPosition =>
        FocusPoint + DesiredRotation * new Vector3(0f, 0f, -Orbit.Radius * Orbit.RadialAxis.Value);

    private CinemachineCamera Rig => cmCamera != null ? cmCamera : cmCamera = GetComponent<CinemachineCamera>();

    private CinemachineOrbitalFollow Orbit => orbit != null ? orbit : orbit = GetComponent<CinemachineOrbitalFollow>();

    private void Awake()
    {
        if (target == null)
        {
            PlayerMotor player = FindAnyObjectByType<PlayerMotor>();

            if (player != null)
                target = player.transform;
        }

        if (target != null && Rig.Target.TrackingTarget == null)
            Rig.Target.TrackingTarget = target;

        if (target != null)
            targetSelector = target.GetComponentInChildren<TargetSelector>();

        if (targetSelector == null)
            targetSelector = FindAnyObjectByType<TargetSelector>();

        lookAction = InputSystem.actions != null ? InputSystem.actions.FindAction(lookActionName) : null;

        if (lookAction == null)
            Debug.LogWarning("ThirdPersonCameraRig: action '" + lookActionName + "' tidak ditemukan, kamera tidak bisa diputar.", this);
    }

    private void OnEnable()
    {
        lookAction?.Enable();
    }

    private void OnDisable()
    {
        SetHidden(false);
        SetCursorLocked(false);
    }

    // Dipanggil TopDownCameraFollow. outputCamera = kamera utama yang digerakkan CinemachineBrain.
    public void SetDriving(bool value, Transform cameraTransform)
    {
        driving = value;
        outputCamera = cameraTransform;

        if (!value)
        {
            SetHidden(false);
            SetCursorLocked(false);
        }
    }

    // Setelah teleport atau menu ditutup: kamera langsung di posisi baru tanpa meluncur melintasi peta.
    // Kamera hanya diputar ke belakang MC jika MC benar-benar berpindah jauh (teleport), bukan saat menu ditutup.
    public void Snap()
    {
        if (snapBehindTargetOnTeleport && target != null && hasLastTargetPosition &&
            Vector3.Distance(target.position, lastTargetPosition) > teleportDistance)
        {
            Orbit.HorizontalAxis.Value = Orbit.HorizontalAxis.ClampValue(target.eulerAngles.y);
        }

        RememberTargetPosition();
        Rig.PreviousStateIsValid = false;
    }

    private void Update()
    {
        if (driving)
        {
            ReadLook();
            ReadZoom();
            UpdateLockOn();
            UpdateFade();
        }

        RememberTargetPosition();
    }

    private void RememberTargetPosition()
    {
        if (target == null)
            return;

        lastTargetPosition = target.position;
        hasLastTargetPosition = true;
    }

    private void ReadLook()
    {
        bool paused = Time.timeScale <= 0f;
        bool rightHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;

        // Kursor dikunci hanya selama klik kanan ditahan, lalu dikembalikan seperti semula.
        SetCursorLocked(mouseRequiresRightButton && rightHeld && !paused);

        if (lookAction == null || paused)
            return;

        Vector2 value = lookAction.ReadValue<Vector2>();

        if (value.sqrMagnitude < 0.000001f)
            return;

        InputControl control = lookAction.activeControl;
        Vector2 delta;

        // Mouse memberi pergeseran piksel per frame, stik memberi nilai -1..1 yang dikali waktu.
        if (control != null && control.device is Pointer)
        {
            if (mouseRequiresRightButton && !rightHeld)
                return;

            delta = value * mouseSensitivity;
        }
        else
        {
            delta = Vector2.Scale(value, stickSpeed) * Time.unscaledDeltaTime;
        }

        if (blockManualYawWhileLocked && LockedEnemy != null)
            delta.x = 0f;

        InputAxis horizontal = Orbit.HorizontalAxis;
        horizontal.Value = horizontal.ClampValue(horizontal.Value + delta.x);
        Orbit.HorizontalAxis = horizontal;

        // Mouse ke atas = melihat ke atas = kamera turun = sudut vertikal mengecil.
        InputAxis vertical = Orbit.VerticalAxis;
        vertical.Value = vertical.ClampValue(vertical.Value + (invertY ? delta.y : -delta.y));
        Orbit.VerticalAxis = vertical;
    }

    // Yaw orbit = arah dari MC ke musuh, jadi kamera berada di belakang MC dan memandang ke arah musuh.
    private void UpdateLockOn()
    {
        EnemyHealth enemy = LockedEnemy;

        if (enemy == null || target == null || Time.timeScale <= 0f)
        {
            lockYawVelocity = 0f;
            return;
        }

        Vector3 toEnemy = enemy.transform.position - target.position;
        toEnemy.y = 0f;

        if (toEnemy.magnitude < lockMinDistance)
            return;

        float desiredYaw = Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg;

        InputAxis horizontal = Orbit.HorizontalAxis;
        float yaw = Mathf.SmoothDampAngle(horizontal.Value, desiredYaw, ref lockYawVelocity, lockSmoothTime);
        horizontal.Value = horizontal.ClampValue(yaw);
        Orbit.HorizontalAxis = horizontal;
    }

    private void ReadZoom()
    {
        if (Mouse.current == null || Time.timeScale <= 0f)
            return;

        float scroll = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) < 0.01f)
            return;

        // Scroll ke atas = mendekat.
        InputAxis radial = Orbit.RadialAxis;
        radial.Range = zoomRange;
        radial.Value = Mathf.Clamp(radial.Value - Mathf.Sign(scroll) * zoomStep, zoomRange.x, zoomRange.y);
        Orbit.RadialAxis = radial;
    }

    private void UpdateFade()
    {
        if (!fadeWhenClose || outputCamera == null || target == null)
        {
            SetHidden(false);
            return;
        }

        float distance = Vector3.Distance(outputCamera.position, FocusPoint);

        if (!hidden && distance < hideDistance)
            SetHidden(true);
        else if (hidden && distance > showDistance)
            SetHidden(false);
    }

    // Shadows Only: renderer tidak digambar kamera, tapi bayangannya tetap jatuh di lantai.
    private void SetHidden(bool value)
    {
        if (hidden == value)
            return;

        hidden = value;

        if (value)
        {
            hiddenRenderers.Clear();
            originalModes.Clear();

            if (target == null)
                return;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;

                hiddenRenderers.Add(renderer);
                originalModes.Add(renderer.shadowCastingMode);
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            return;
        }

        for (int i = 0; i < hiddenRenderers.Count; i++)
        {
            if (hiddenRenderers[i] != null)
                hiddenRenderers[i].shadowCastingMode = originalModes[i];
        }

        hiddenRenderers.Clear();
        originalModes.Clear();
    }

    private void SetCursorLocked(bool value)
    {
        if (cursorLocked == value)
            return;

        cursorLocked = value;

        if (value)
        {
            previousLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = previousLockState;
            Cursor.visible = previousCursorVisible;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null)
            return;

        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(FocusPoint, hideDistance);
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(FocusPoint, showDistance);
    }
}
