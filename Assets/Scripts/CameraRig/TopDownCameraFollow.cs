using UnityEngine;

// Kamera top-down miring yang mengikuti MC
// Yaw tetap supaya arah WASD konsisten. Pengganti Cinemachine yang belum terpasang.
public class TopDownCameraFollow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform target;

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

    public Transform Target
    {
        get => target;
        set
        {
            target = value;
            SnapToTarget();
        }
    }

    private void Start()
    {
        ApplyLens();
        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (target == null)
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
        Camera cam = GetComponent<Camera>();

        if (cam != null)
            cam.fieldOfView = fieldOfView;
    }

    // Bisa dipanggil dari menu titik tiga komponen di Inspector untuk melihat framing tanpa Play Mode.
    [ContextMenu("Snap ke Target")]
    public void SnapToTarget()
    {
        if (target == null)
            return;

        transform.rotation = GetRotation();
        transform.position = GetDesiredPosition();
        velocity = Vector3.zero;

        ApplyLens();
    }
}
