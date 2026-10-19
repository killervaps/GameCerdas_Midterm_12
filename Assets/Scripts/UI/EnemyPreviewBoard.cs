using System.Text;
using TMPro;
using UnityEngine;

// Papan preview musuh ronde berikutnya di Safezone: ikon role, jumlah, peran singkat,
// dan ancaman wave terhadap build MC saat ini (sebelum memilih gate).
public class EnemyPreviewBoard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private RunManager runManager;
    [SerializeField] private ThreatEstimator threatEstimator;
    [SerializeField] private PlayerStats playerStats;

    [Header("Display")]
    // Label diputar searah kamera supaya terbaca dari kamera top-down.
    [SerializeField] private bool billboard = true;

    private readonly StringBuilder builder = new StringBuilder();

    private Transform cameraTransform;

    private void Awake()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void OnEnable()
    {
        if (runManager != null)
            runManager.StateChanged += HandleStateChanged;

        if (playerStats != null)
            playerStats.StatsChanged += Refresh;
    }

    private void OnDisable()
    {
        if (runManager != null)
            runManager.StateChanged -= HandleStateChanged;

        if (playerStats != null)
            playerStats.StatsChanged -= Refresh;
    }

    private void Start()
    {
        Refresh();
    }

    private void LateUpdate()
    {
        if (billboard && label != null && cameraTransform != null)
            label.transform.rotation = cameraTransform.rotation;
    }

    private void HandleStateChanged(RunState state)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (label == null || runManager == null)
            return;

        WaveData wave = runManager.CurrentWave;

        builder.Clear();
        builder.Append("<b>RONDE ").Append(Mathf.Max(1, runManager.CurrentRound))
            .Append('/').Append(runManager.TotalRounds).Append("</b>\n");

        if (wave == null)
        {
            label.text = builder.Append('-').ToString();
            return;
        }

        builder.Append("<size=70%>Musuh berikutnya</size>\n");

        foreach (WaveEntry entry in wave.Entries)
        {
            if (entry.count <= 0)
                continue;

            builder.Append("<color=").Append(RoleColors.Hex(entry.role)).Append("><b>[")
                .Append(RoleBadgeLetter(entry.role)).Append("]</b> ")
                .Append(entry.count).Append(' ').Append(entry.role).Append("</color>  <size=60%>")
                .Append(RoleSummary(entry.role)).Append("</size>\n");
        }

        if (threatEstimator != null && playerStats != null)
        {
            ThreatResult threat = threatEstimator.Evaluate(wave, playerStats.GetSnapshot());

            builder.Append("<size=80%>Ancaman build sekarang <color=").Append(threat.ColorHex).Append("><b>")
                .Append(threat.Label).Append("</b></color> (").Append(threat.ratio.ToString("0.00")).Append(")</size>");
        }

        label.text = builder.ToString();
    }

    public static string RoleBadgeLetter(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Shooter: return "S";
            case EnemyRole.Guardian: return "G";
            default: return "C";
        }
    }

    public static string RoleSummary(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Shooter: return "menembak dari luar jangkauan";
            case EnemyRole.Guardian: return "melindungi rekan, tahan damage";
            default: return "menerjang setelah 3 ketukan";
        }
    }
}
