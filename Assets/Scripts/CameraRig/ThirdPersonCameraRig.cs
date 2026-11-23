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
    // Deadzone untuk stik Joystick generik (stik Gamepad sudah punya deadzone sendiri).
    [SerializeField] private float stickDeadzone = 0.15f;

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

    [Header("Tembok")]
    // Saat Play: tambahkan CameraWallClamp jika belum ada, dan matikan Deoccluder/Decollider (diganti CameraWallClamp).
    [SerializeField] private bool enforceWallHandling = true;
    // Selama kamera ditahan tembok dekat MC, putaran kamera KE arah tembok diperlambat (mouse/stik/lock-on).
    // Putaran keluar dari tembok tetap normal. Besarnya diatur di CameraWallClamp (Sensitivitas Dekat Tembok).
    [SerializeField] private bool slowRotationAgainstWall = true;

    [Header("Fade Saat Kamera Dekat")]
    [SerializeField] private bool fadeWhenClose = true;
    // Jarak kamera ke permukaan badan MC (bounds renderer). Badan hanya disembunyikan saat lensa hampir masuk ke badan,
    // supaya MC tetap terlihat saat kamera ditahan tembok dekat MC. Muncul lagi di atas Show Margin
    // (dua batas supaya tidak berkedip saat kamera tepat di batas).
    [SerializeField] private float hideMargin = 0.12f;
    [SerializeField] private float showMargin = 0.25f;
    // MC dibuat tembus pandang sebagian saat kamera mendekat (mis. ditahan tembok), dengan opasitas yang sama seperti
    // prop yang menghalangi. Mulai memudar di Fade Start Distance, paling tembus di Fade Full Distance
    // (jarak lensa ke permukaan badan MC). Di bawah Hide Margin badan tetap disembunyikan penuh (lensa di dalam badan).
    [SerializeField] private bool partialFadeWhenClose = true;
    [SerializeField, Range(0f, 1f)] private float closeOpacity = 0.3f;
    [SerializeField] private float fadeStartDistance = 1.0f;
    [SerializeField] private float fadeFullDistance = 0.4f;
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
    private CameraWallClamp wallClamp;
    private List<Renderer> bodyRenderers;

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

    // Kamera sedang benar-benar ditahan tembok (CameraWallClamp), bukan sisa damping saat kembali.
    public bool IsAgainstWall => wallClamp != null && wallClamp.isActiveAndEnabled && wallClamp.IsHoldingCamera;

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

        EnsureWallHandling();

        lookAction = InputSystem.actions != null ? InputSystem.actions.FindAction(lookActionName) : null;

        if (lookAction == null)
            Debug.LogWarning("ThirdPersonCameraRig: action '" + lookActionName + "' tidak ditemukan, kamera tidak bisa diputar.", this);
    }

    // CameraWallClamp adalah satu-satunya pemecah tabrakan kamera TPP. Scene lama belum punya komponen ini,
    // jadi ditambahkan saat Play. Deoccluder/Decollider dimatikan karena koreksinya selalu diganti CameraWallClamp
    // (Deoccluder di scene lama memeriksa dari kaki MC dan menarik kamera turun ke lantai).
    private void EnsureWallHandling()
    {
        wallClamp = GetComponent<CameraWallClamp>();

        if (!enforceWallHandling)
            return;

        if (wallClamp == null)
        {
            wallClamp = gameObject.AddComponent<CameraWallClamp>();
            wallClamp.Configure(Orbit.TargetOffset.y);
        }

        CinemachineDeoccluder deoccluder = GetComponent<CinemachineDeoccluder>();

        if (deoccluder != null)
            deoccluder.enabled = false;

        CinemachineDecollider decollider = GetComponent<CinemachineDecollider>();

        if (decollider != null)
            decollider.enabled = false;
    }

    private void OnEnable()
    {
        lookAction?.Enable();
    }

    private void OnDisable()
    {
        SetHidden(false);
        ReleaseBodyFade();
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
            ReleaseBodyFade();
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

        // Map Player dimatikan GameUI saat menu/jeda: kamera juga tidak diputar.
        if (lookAction == null || paused || !lookAction.enabled)
            return;

        ReadLookControls(rightHeld, out Vector2 mouse, out Vector2 stick);

        // Mouse memberi pergeseran piksel per frame, stik memberi nilai -1..1 yang dikali waktu.
        // Pengali dari menu Pengaturan (tab Keyboard untuk mouse, tab Kontroler untuk stik).
        Vector2 delta = mouse * (mouseSensitivity * CameraSensitivity.Mouse) +
                        Vector2.Scale(stick, stickSpeed) * (Time.unscaledDeltaTime * CameraSensitivity.Stick);

        if (delta.sqrMagnitude < 0.0000001f)
            return;

        if (blockManualYawWhileLocked && LockedEnemy != null)
            delta.x = 0f;

        InputAxis horizontal = Orbit.HorizontalAxis;
        delta.x = ResistWallYaw(horizontal.Value, delta.x);

        // Naik-turun ikut diperlambat selama kamera menempel tembok dekat MC.
        if (slowRotationAgainstWall && IsAgainstWall)
            delta.y *= wallClamp.SensitivityScale;

        horizontal.Value = horizontal.ClampValue(horizontal.Value + delta.x);
        Orbit.HorizontalAxis = horizontal;

        // Mouse ke atas = melihat ke atas = kamera turun = sudut vertikal mengecil.
        InputAxis vertical = Orbit.VerticalAxis;
        vertical.Value = vertical.ClampValue(vertical.Value + (invertY ? delta.y : -delta.y));
        Orbit.VerticalAxis = vertical;
    }

    // Membaca setiap kontrol yang terikat ke action Look satu per satu, bukan lookAction.ReadValue().
    // Action Look bertipe Value: ia hanya mengikuti SATU kontrol (yang nilainya paling besar). Binding <Joystick>/stick
    // pada controller generik adalah stik kiri (stik jalan), jadi saat MC berjalan action dikuasai stik kiri dan
    // stik kanan maupun mouse diabaikan, sehingga kamera terasa tidak responsif.
    private void ReadLookControls(bool rightHeld, out Vector2 mouse, out Vector2 stick)
    {
        mouse = Vector2.zero;
        stick = Vector2.zero;

        // Joystick generik sudah dicerminkan ke Gamepad virtual oleh JoystickGamepadBridge (stik kanan yang benar),
        // jadi binding Joystick hanya dipakai jika tidak ada Gamepad sama sekali.
        bool hasGamepad = Gamepad.all.Count > 0;

        foreach (InputControl control in lookAction.controls)
        {
            if (!(control is InputControl<Vector2> vectorControl))
                continue;

            InputDevice device = control.device;

            if (device is Pointer)
            {
                if (!mouseRequiresRightButton || rightHeld)
                    mouse += vectorControl.ReadValue();

                continue;
            }

            if (device is Joystick && hasGamepad)
                continue;

            Vector2 value = vectorControl.ReadValue();

            // Stik Gamepad sudah punya deadzone bawaan, stik Joystick generik belum (drift kecil memutar kamera).
            if (!(device is Gamepad))
                value = ApplyDeadzone(value, stickDeadzone);

            if (value.sqrMagnitude > stick.sqrMagnitude)
                stick = value;
        }
    }

    private static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
    {
        float magnitude = value.magnitude;

        if (magnitude <= deadzone)
            return Vector2.zero;

        float scaled = Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
        return value / magnitude * scaled;
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
        yaw = horizontal.Value + ResistWallYaw(horizontal.Value, Mathf.DeltaAngle(horizontal.Value, yaw));
        horizontal.Value = horizontal.ClampValue(yaw);
        Orbit.HorizontalAxis = horizontal;
    }

    // Putaran yaw yang menekan kamera makin masuk ke tembok (dekat MC) diperlambat; putaran keluar tetap normal.
    private float ResistWallYaw(float yaw, float delta)
    {
        if (!slowRotationAgainstWall || !IsAgainstWall || !wallClamp.YawMovesIntoWall(yaw, delta))
            return delta;

        return delta * wallClamp.SensitivityScale;
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

    private CameraSeeThroughProp bodyFade;

    // Penanda see-through di MC, dibuat saat pertama dibutuhkan dan tidak ikut dicek CameraOccluderFade.
    private CameraSeeThroughProp BodyFade
    {
        get
        {
            if (bodyFade == null && target != null)
            {
                bodyFade = target.GetComponent<CameraSeeThroughProp>();

                if (bodyFade == null)
                    bodyFade = target.gameObject.AddComponent<CameraSeeThroughProp>();

                bodyFade.ScanByCamera = false;
            }

            return bodyFade;
        }
    }

    private void ReleaseBodyFade()
    {
        if (bodyFade != null)
            bodyFade.ReleaseFade(6f);
    }

    private void UpdateFade()
    {
        if (!fadeWhenClose || outputCamera == null || target == null)
        {
            SetHidden(false);
            ReleaseBodyFade();
            return;
        }

        if (!TryGetBodyBounds(out Bounds body))
        {
            SetHidden(false);
            ReleaseBodyFade();
            return;
        }

        float distance = Mathf.Sqrt(body.SqrDistance(outputCamera.position));

        if (partialFadeWhenClose && BodyFade != null)
        {
            float t = Mathf.InverseLerp(fadeFullDistance, fadeStartDistance, distance);
            float opacity = Mathf.Lerp(closeOpacity, 1f, t);

            if (opacity < 0.999f)
                BodyFade.RequestFade(opacity, 6f);
            else
                BodyFade.ReleaseFade(6f);
        }

        if (!hidden && distance < hideMargin)
            SetHidden(true);
        else if (hidden && distance > showMargin)
            SetHidden(false);
    }

    private bool TryGetBodyBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        if (bodyRenderers == null)
        {
            bodyRenderers = new List<Renderer>();

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    bodyRenderers.Add(renderer);
            }
        }

        foreach (Renderer renderer in bodyRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (any)
                bounds.Encapsulate(renderer.bounds);
            else
                bounds = renderer.bounds;

            any = true;
        }

        return any;
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
        if (target == null || !Application.isPlaying || !TryGetBodyBounds(out Bounds body))
            return;

        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.6f);
        Gizmos.DrawWireCube(body.center, body.size + Vector3.one * (hideMargin * 2f));
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireCube(body.center, body.size + Vector3.one * (showMargin * 2f));
    }
}
