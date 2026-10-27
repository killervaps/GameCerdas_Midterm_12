using TMPro;
using UnityEngine;

// Satu gate fisik di Safezone. Collider trigger di depan gate mendeteksi MC,
// label TextMeshPro menampilkan isi kartu. Keputusan memilih diurus GateSelectionRoom.
[RequireComponent(typeof(Collider))]
public class GatePortal : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private TMP_Text label;
    // Objek yang aktif hanya saat MC berdiri di depan gate, mis. teks "Tahan E untuk memilih" atau lampu sorot.
    [SerializeField] private GameObject highlightObject;
    // Label diputar searah kamera supaya selalu terbaca dari kamera top-down.
    [SerializeField] private bool billboardLabel = true;

    [Header("Text")]
    [SerializeField] private string closedText = "<color=#888888>Tertutup</color>";

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private GateSelectionRoom room;
    private Collider trigger;
    private Transform cameraTransform;

    public MathGateData Gate { get; private set; }

    public bool IsOpen => Gate != null;

    public bool IsHighlighted { get; private set; }

    private void Awake()
    {
        trigger = GetComponent<Collider>();

        if (!trigger.isTrigger)
        {
            Debug.LogWarning("GatePortal " + name + ": collider dijadikan trigger otomatis.", this);
            trigger.isTrigger = true;
        }

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;

        // Nama dan label gate SIGMA berwarna pelangi yang berputar.
        if (label != null && label.GetComponent<TMPRainbowLinks>() == null)
            label.gameObject.AddComponent<TMPRainbowLinks>();

        SetHighlighted(false);
    }

    public void Bind(GateSelectionRoom owner)
    {
        room = owner;
    }

    public void Open(MathGateData gate, string text)
    {
        Gate = gate;

        if (label != null)
            label.text = text;

        SetHighlighted(IsHighlighted);
    }

    public void Close()
    {
        Gate = null;

        if (label != null)
            label.text = closedText;

        SetHighlighted(false);
    }

    public void SetHighlighted(bool highlighted)
    {
        IsHighlighted = highlighted;

        if (highlightObject != null)
            highlightObject.SetActive(highlighted && IsOpen);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (room != null)
            room.NotifyPortalEnter(this, other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (room != null)
            room.NotifyPortalExit(this, other);
    }

    private void LateUpdate()
    {
        if (billboardLabel && label != null && cameraTransform != null)
            label.transform.rotation = cameraTransform.rotation;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Collider col = trigger != null ? trigger : GetComponent<Collider>();

        if (col == null)
            return;

        Gizmos.color = !Application.isPlaying
            ? new Color(1f, 1f, 0f, 0.6f)
            : IsHighlighted && IsOpen
                ? Color.green
                : IsOpen ? Color.cyan : Color.gray;

        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
