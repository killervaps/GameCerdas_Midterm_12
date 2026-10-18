using UnityEngine;
using UnityEngine.InputSystem;

// Gerak MC berbasis Input System (pengganti SimplePlayerController yang memakai API Input lama).
// Gerak di bidang XZ relatif terhadap yaw kamera yang tetap, sehingga WASD selalu sesuai arah layar.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerStats))]
public class PlayerMotor : MonoBehaviour
{
    [Header("References")]
    // Kosongkan untuk memakai Camera.main.
    [SerializeField] private Transform cameraTransform;

    [Header("Input")]
    // Nama action di project-wide actions (Assets/InputSystem_Actions.inputactions).
    [SerializeField] private string moveActionName = "Player/Move";

    [Header("Movement")]
    // Kecepatan dasar diambil dari PlayerStats (StatType.MoveSpeed).
    [SerializeField] private float gravity = -20f;
    // Kecepatan berputar menghadap arah gerak atau target, derajat per detik.
    [SerializeField] private float turnSpeed = 720f;
    [SerializeField] private bool cameraRelative = true;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private CharacterController controller;
    private PlayerHealth health;
    private PlayerStats stats;
    private InputAction moveAction;

    private float verticalVelocity;

    // Kecepatan dari stat build, belum termasuk pengali sementara.
    public float MoveSpeed { get; private set; }

    // Pengali sementara, mis. melambat saat menembak. 1 berarti normal.
    public float SpeedMultiplier { get; private set; } = 1f;

    // Kecepatan yang benar-benar dipakai saat bergerak.
    public float EffectiveMoveSpeed => MoveSpeed * SpeedMultiplier;

    // Arah gerak terakhir di dunia (sudah dinormalisasi, nol saat diam).
    public Vector3 MoveDirection { get; private set; }

    public Vector3 Velocity => controller != null ? controller.velocity : Vector3.zero;

    // Diisi TargetSelector. Jika ada, MC menghadap target, bukan arah gerak.
    public Transform FacingTarget { get; set; }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        stats = GetComponent<PlayerStats>();

        MoveSpeed = stats.Get(StatType.MoveSpeed);

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        moveAction = InputSystem.actions != null
            ? InputSystem.actions.FindAction(moveActionName)
            : null;

        if (moveAction == null)
        {
            Debug.LogError(
                "PlayerMotor: action '" + moveActionName + "' tidak ditemukan. " +
                "Cek Project Settings > Input System Package > Project-wide Actions.",
                this
            );
        }
    }

    private void OnEnable()
    {
        moveAction?.Enable();

        stats.StatsChanged += HandleStatsChanged;
    }

    private void OnDisable()
    {
        stats.StatsChanged -= HandleStatsChanged;
    }

    private void HandleStatsChanged()
    {
        SetMoveSpeed(stats.Get(StatType.MoveSpeed));
    }

    // CharacterController harus dimatikan sementara, kalau tidak posisi baru ditimpa kembali.
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;

        verticalVelocity = 0f;
        MoveDirection = Vector3.zero;
    }

    public void SetMoveSpeed(float speed)
    {
        MoveSpeed = Mathf.Max(0f, speed);
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        SpeedMultiplier = Mathf.Max(0f, multiplier);
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            MoveDirection = Vector3.zero;
            return;
        }

        Vector2 input = moveAction != null
            ? moveAction.ReadValue<Vector2>()
            : Vector2.zero;

        MoveDirection = ToWorldDirection(input);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity =
            MoveDirection * EffectiveMoveSpeed +
            Vector3.up * verticalVelocity;

        controller.Move(velocity * Time.deltaTime);

        UpdateFacing();
    }

    private Vector3 ToWorldDirection(Vector2 input)
    {
        Vector3 direction = new Vector3(input.x, 0f, input.y);

        if (cameraRelative && cameraTransform != null)
        {
            // Hanya yaw kamera yang dipakai, pitch diabaikan supaya gerak tetap datar.
            Quaternion yaw = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
            direction = yaw * direction;
        }

        // Stik analog boleh kurang dari 1, keyboard diagonal dipotong ke 1.
        return Vector3.ClampMagnitude(direction, 1f);
    }

    private void UpdateFacing()
    {
        Vector3 look;

        if (FacingTarget != null)
            look = FacingTarget.position - transform.position;
        else
            look = MoveDirection;

        look.y = 0f;

        if (look.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(look);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeed * Time.deltaTime
        );
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Vector3 origin = transform.position + Vector3.up * 0.1f;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + MoveDirection * 1.5f);

        if (FacingTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, FacingTarget.position + Vector3.up * 0.1f);
        }
    }
}
