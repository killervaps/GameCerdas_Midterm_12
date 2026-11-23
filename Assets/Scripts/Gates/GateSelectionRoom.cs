using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Mengatur pemilihan gate di Safezone. Tiga GatePortal diisi hasil GateGenerator,
// MC berdiri di depan salah satu gate lalu menahan tombol Interact, kemudian diteleport ke arena.
public class GateSelectionRoom : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private PlayerMotor playerMotor;
    [SerializeField] private GateGenerator generator;
    [SerializeField] private GatePortal[] portals;
    // Opsional. Jika diisi, kamera langsung pindah tanpa meluncur melintasi peta setelah teleport.
    [SerializeField] private TopDownCameraFollow cameraFollow;
    // Opsional. Jika diisi, tiap kartu menampilkan label risiko wave berikutnya.
    [SerializeField] private ThreatEstimator threatEstimator;

    [Header("Spawn Points")]
    [SerializeField] private Transform safezoneSpawn;
    [SerializeField] private Transform arenaSpawn;

    [Header("Input")]
    // Action Interact memakai interaction Hold, jadi perlu ditahan sebentar. Mencegah salah pilih saat lewat.
    [SerializeField] private string confirmActionName = "Player/Interact";

    [Header("Gate Focus")]
    // Aktif = gate dipilih dari jarak datar MC ke gate (tinggi diabaikan), bukan dari trigger collider.
    // Trigger gate kecil dan rendah sehingga hanya tersentuh di titik tertentu, jarak datar tidak bergantung pada itu.
    [SerializeField] private bool focusByDistance = true;
    // Radius area depan gate = setengah ukuran collider gate (X/Z) + nilai ini. Lingkaran kuning di Scene view.
    [SerializeField, Min(0f)] private float focusMargin = 0.35f;

    [Header("Rules")]
    // Jika ada RunManager, nilai ini ditimpa jumlah ronde.
    [SerializeField] private int maxGatesPerRun = 3;
    // Dipakai hanya tanpa RunManager. Dengan RunManager, alur dimulai dari RunManager.
    [SerializeField] private bool startInSafezone = true;

    [Header("Debug")]
    [SerializeField] private bool logEvents = true;
    // Hanya tanpa RunManager. Tombol R di arena mengembalikan MC ke Safezone.
    [SerializeField] private bool debugReturnKey = true;

    private readonly List<MathGateData> offer = new List<MathGateData>();

    private InputAction confirmAction;
    private GatePortal activePortal;
    private bool drivenExternally;
    private WaveData upcomingWave;

    public bool IsSelecting { get; private set; }

    public GatePortal ActivePortal => activePortal;

    // Dikirim setelah gate dipilih dan MC sudah berada di arena. RunManager nanti memulai ronde dari sini.
    public event Action<MathGateData> GateChosen;

    // Dikirim setelah MC kembali ke Safezone dan pilihan baru sudah dibuka.
    public event Action SelectionOpened;

    // Dikirim saat MC masuk collider trigger sebuah gate (gate itu menjadi aktif). Dipakai GateInfoModal.
    public event Action<GatePortal> PortalFocused;

    // Dikirim saat gate aktif dilepas (MC keluar trigger, gate dipilih, atau pilihan ditutup).
    public event Action<GatePortal> PortalUnfocused;

    // Progress tahan tombol Interact (0-1) selama MC berdiri di depan gate yang terbuka.
    public float ConfirmProgress =>
        IsSelecting &&
        activePortal != null &&
        activePortal.IsOpen &&
        confirmAction != null &&
        confirmAction.phase == InputActionPhase.Started
            ? Mathf.Clamp01(confirmAction.GetTimeoutCompletionPercentage())
            : 0f;

    private void Awake()
    {
        confirmAction = InputSystem.actions != null
            ? InputSystem.actions.FindAction(confirmActionName)
            : null;

        if (confirmAction == null)
            Debug.LogError("GateSelectionRoom: action '" + confirmActionName + "' tidak ditemukan.", this);

        foreach (GatePortal portal in portals)
        {
            if (portal != null)
                portal.Bind(this);
        }
    }

    private void OnEnable()
    {
        confirmAction?.Enable();
    }

    // Wave yang akan dihadapi setelah memilih gate. Dipanggil RunManager sebelum pilihan dibuka.
    public void SetUpcomingWave(WaveData wave)
    {
        upcomingWave = wave;
    }

    public WaveData UpcomingWave => upcomingWave;

    // Dipanggil RunManager setelah arena ronde berikutnya dipilih. MC diteleport ke sini saat memilih gate.
    public void SetArenaSpawn(Transform point)
    {
        if (point != null)
            arenaSpawn = point;
    }

    // Dipanggil RunManager di Awake. Room tidak memulai sendiri dan tombol debug R dimatikan.
    public void UseExternalDriver(int maxGates)
    {
        drivenExternally = true;
        maxGatesPerRun = Mathf.Max(1, maxGates);
    }

    private void Start()
    {
        if (playerStats == null || playerMotor == null || generator == null)
        {
            Debug.LogError("GateSelectionRoom: isi Player Stats, Player Motor, dan Generator di Inspector.", this);
            enabled = false;
            return;
        }

        if (drivenExternally)
            return;

        if (startInSafezone)
            ReturnToSafezone();
        else
            OpenSelection();
    }

    private void Update()
    {
        if (IsSelecting)
            RefreshFocus();

        if (IsSelecting &&
            activePortal != null &&
            activePortal.IsOpen &&
            confirmAction != null &&
            confirmAction.WasPerformedThisFrame())
        {
            Choose(activePortal);
            return;
        }

        if (debugReturnKey &&
            !drivenExternally &&
            !IsSelecting &&
            Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame)
        {
            ReturnToSafezone();
        }
    }

    public Transform SafezoneSpawn => safezoneSpawn;

    // Hanya memindahkan MC ke Safezone tanpa membuka pilihan gate. Dipakai menu utama.
    public void TeleportToSafezone()
    {
        TeleportPlayer(safezoneSpawn);
    }

    public void ReturnToSafezone()
    {
        TeleportPlayer(safezoneSpawn);
        OpenSelection();
    }

    // Mengacak ulang tiga gate tanpa memindahkan MC. Dipakai panel debug (paksa rarity) saat demo.
    // Gate yang sedang didekati MC tetap difokuskan supaya modal info langsung menampilkan isi barunya.
    public void RerollOffer()
    {
        if (!IsSelecting)
            return;

        GatePortal focused = activePortal;

        OpenSelection();

        if (focused != null && focused.IsOpen && IsSelecting)
        {
            activePortal = focused;
            focused.SetHighlighted(true);
            PortalFocused?.Invoke(focused);
        }
    }

    public GateGenerator Generator => generator;

    public void OpenSelection()
    {
        ClearActivePortal();

        if (playerStats.SelectedGates.Count >= maxGatesPerRun)
        {
            CloseAllPortals();
            IsSelecting = false;

            Log("build sudah penuh (" + maxGatesPerRun + " gate), semua gate ditutup");
            return;
        }

        generator.Roll(portals.Length, offer);

        for (int i = 0; i < portals.Length; i++)
        {
            if (portals[i] == null)
                continue;

            if (i < offer.Count)
                portals[i].Open(offer[i], GateTextFormatter.BuildCard(offer[i], playerStats, EvaluateThreat(offer[i])));
            else
                portals[i].Close();
        }

        IsSelecting = true;

        Log("pilihan dibuka, " + offer.Count + " gate");

        SelectionOpened?.Invoke();
    }

    // Menghitung ulang teks kartu gate yang sedang terbuka tanpa mengganti gate-nya, mis. setelah wave berikutnya berubah.
    public void RefreshOfferCards()
    {
        if (!IsSelecting)
            return;

        for (int i = 0; i < portals.Length && i < offer.Count; i++)
        {
            if (portals[i] != null)
                portals[i].Open(offer[i], GateTextFormatter.BuildCard(offer[i], playerStats, EvaluateThreat(offer[i])));
        }
    }

    private ThreatResult? EvaluateThreat(MathGateData gate)
    {
        if (threatEstimator == null || upcomingWave == null)
            return null;

        ThreatResult result = threatEstimator.Evaluate(upcomingWave, playerStats.Preview(gate));

        Log(gate.DisplayName + " -> ancaman " + result.Label + " (" + result.ratio.ToString("0.00") + ")");

        return result;
    }

    // Dipanggil saat run baru. Build kembali ke base dan riwayat roll dibersihkan.
    // Saat Play bisa dipanggil dari menu titik tiga komponen ini.
    [ContextMenu("Reset Run")]
    public void ResetRun()
    {
        if (!Application.isPlaying)
            return;

        generator.ClearHistory();
        playerStats.ResetBuild();
        ReturnToSafezone();
    }

    private void Choose(GatePortal portal)
    {
        MathGateData gate = portal.Gate;

        IsSelecting = false;

        playerStats.ApplyGate(gate);

        CloseAllPortals();
        ClearActivePortal();

        Log("memilih " + gate.DisplayName + ", teleport ke arena");

        SfxManager.Play(SfxCue.GateChoose);

        TeleportPlayer(arenaSpawn);

        GateChosen?.Invoke(gate);
    }

    // Gate yang trigger-nya sedang dimasuki MC. Bisa lebih dari satu karena gate bersebelahan dan trigger-nya bertumpuk.
    private readonly HashSet<GatePortal> occupiedPortals = new HashSet<GatePortal>();

    public void NotifyPortalEnter(GatePortal portal, Collider other)
    {
        if (!IsPlayer(other))
            return;

        occupiedPortals.Add(portal);
        RefreshFocus();
    }

    public void NotifyPortalExit(GatePortal portal, Collider other)
    {
        if (!IsPlayer(other))
            return;

        occupiedPortals.Remove(portal);
        RefreshFocus();
    }

    // Gate aktif = gate terbuka terdekat di antara trigger yang sedang dimasuki MC. Dihitung ulang setiap frame,
    // jadi gate tengah tidak "hilang" saat MC keluar dari trigger gate sebelah walau masih berdiri di gate tengah,
    // dan gate yang sudah dimasuki sebelum pilihan dibuka langsung aktif begitu pilihan terbuka.
    private void RefreshFocus()
    {
        GatePortal best = null;

        if (IsSelecting && playerMotor != null)
        {
            Vector3 player = playerMotor.transform.position;
            float bestDistance = float.PositiveInfinity;

            IEnumerable<GatePortal> candidates = focusByDistance ? (IEnumerable<GatePortal>)portals : occupiedPortals;

            foreach (GatePortal portal in candidates)
            {
                if (portal == null || !portal.IsOpen)
                    continue;

                GetFocusArea(portal, out Vector3 center, out float radius);
                center.y = player.y;

                float distance = (center - player).sqrMagnitude;

                if (focusByDistance && distance > radius * radius)
                    continue;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = portal;
                }
            }
        }

        if (best == activePortal)
            return;

        if (activePortal != null)
        {
            GatePortal previous = activePortal;
            previous.SetHighlighted(false);
            activePortal = null;
            PortalUnfocused?.Invoke(previous);
        }

        if (best != null)
        {
            activePortal = best;
            best.SetHighlighted(true);
            PortalFocused?.Invoke(best);
        }
    }

    private void ClearActivePortal()
    {
        if (activePortal == null)
            return;

        GatePortal previous = activePortal;
        previous.SetHighlighted(false);
        activePortal = null;

        PortalUnfocused?.Invoke(previous);
    }

    private void CloseAllPortals()
    {
        foreach (GatePortal portal in portals)
        {
            if (portal != null)
                portal.Close();
        }
    }

    private bool IsPlayer(Collider other)
    {
        return playerMotor != null &&
               other.GetComponentInParent<PlayerMotor>() == playerMotor;
    }

    private void TeleportPlayer(Transform point)
    {
        if (point == null)
        {
            Debug.LogWarning("GateSelectionRoom: spawn point belum diisi, teleport dilewati.", this);
            return;
        }

        playerMotor.Teleport(point.position, point.rotation);

        if (cameraFollow != null)
            cameraFollow.SnapToTarget();
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log("GateSelectionRoom: " + message, this);
    }

    // Pusat dan radius area depan gate di bidang datar, diambil dari collider gate.
    private void GetFocusArea(GatePortal portal, out Vector3 center, out float radius)
    {
        Collider area = portal.GetComponent<Collider>();

        if (area == null)
        {
            center = portal.transform.position;
            radius = focusMargin + 0.5f;
            return;
        }

        Bounds bounds = area.bounds;
        center = bounds.center;
        radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + focusMargin;
    }

    private void OnDrawGizmos()
    {
        if (focusByDistance && portals != null)
        {
            foreach (GatePortal portal in portals)
            {
                if (portal == null)
                    continue;

                GetFocusArea(portal, out Vector3 center, out float radius);
                center.y = portal.transform.position.y + 0.05f;

                Gizmos.color = portal == activePortal ? Color.green : new Color(1f, 0.9f, 0.2f, 0.8f);
                const int segments = 32;
                Vector3 previous = center + new Vector3(radius, 0f, 0f);

                for (int i = 1; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    Gizmos.DrawLine(previous, point);
                    previous = point;
                }
            }
        }

        DrawSpawnGizmo(safezoneSpawn, Color.cyan);
        DrawSpawnGizmo(arenaSpawn, new Color(1f, 0.5f, 0f));

        if (safezoneSpawn != null && arenaSpawn != null)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            Gizmos.DrawLine(safezoneSpawn.position, arenaSpawn.position);
        }
    }

    private static void DrawSpawnGizmo(Transform point, Color color)
    {
        if (point == null)
            return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(point.position + Vector3.up * 0.1f, 0.4f);
        Gizmos.DrawLine(point.position + Vector3.up * 0.1f, point.position + Vector3.up * 0.1f + point.forward);
    }
}
