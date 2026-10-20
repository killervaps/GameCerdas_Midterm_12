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

        TeleportPlayer(arenaSpawn);

        GateChosen?.Invoke(gate);
    }

    public void NotifyPortalEnter(GatePortal portal, Collider other)
    {
        if (!IsPlayer(other))
            return;

        // Gate bersebelahan. Yang terakhir dimasuki yang aktif.
        if (activePortal != null && activePortal != portal)
        {
            GatePortal previous = activePortal;
            previous.SetHighlighted(false);
            PortalUnfocused?.Invoke(previous);
        }

        activePortal = portal;
        portal.SetHighlighted(true);

        PortalFocused?.Invoke(portal);
    }

    public void NotifyPortalExit(GatePortal portal, Collider other)
    {
        if (!IsPlayer(other) || activePortal != portal)
            return;

        ClearActivePortal();
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

    private void OnDrawGizmos()
    {
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
