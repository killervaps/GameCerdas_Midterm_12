using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Panel OnGUI sementara untuk menguji stat dan gate sebelum GateSelectionUI (uGUI) dibuat.
// Menampilkan build MC, tiga kartu gate beserta preview stat, dan tombol pilih.
public class GateTestPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private GateGenerator generator;

    [Header("Rules")]
    [SerializeField] private int optionsPerRoll = 3;
    [SerializeField] private int maxGatesPerRun = 3;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    // Perbesar jika teks terlalu kecil di layar resolusi tinggi.
    [SerializeField] private float uiScale = 1f;

    private const float CardWidth = 230f;
    private const float CardHeight = 250f;
    private const float Margin = 10f;

    private readonly List<MathGateData> offer = new List<MathGateData>();
    private readonly List<string> cardTexts = new List<string>();
    private readonly StringBuilder builder = new StringBuilder();

    private string buildText = string.Empty;

    // Aksi tombol ditunda ke Update. Mengubah isi panel di tengah OnGUI membuat jumlah kontrol
    // berbeda antara event Layout dan Repaint sehingga IMGUI melempar error.
    private MathGateData pendingPick;
    private bool pendingReroll;
    private bool pendingReset;
    private GUIStyle richLabel;

    private void OnEnable()
    {
        if (stats != null)
            stats.StatsChanged += RefreshTexts;
    }

    private void OnDisable()
    {
        if (stats != null)
            stats.StatsChanged -= RefreshTexts;
    }

    private void Start()
    {
        if (stats == null || generator == null)
        {
            Debug.LogError("GateTestPanel: isi field Stats dan Generator di Inspector.", this);
            enabled = false;
            return;
        }

        RollOffer();
    }

    private void Update()
    {
        if (pendingReset)
            ResetRun();
        else if (pendingPick != null)
            Pick(pendingPick);
        else if (pendingReroll)
            RollOffer();

        pendingPick = null;
        pendingReroll = false;
        pendingReset = false;
    }

    private void RollOffer()
    {
        if (stats.SelectedGates.Count >= maxGatesPerRun)
            offer.Clear();
        else
            generator.Roll(optionsPerRoll, offer);

        RefreshTexts();
    }

    private void Pick(MathGateData gate)
    {
        stats.ApplyGate(gate);
        RollOffer();
    }

    private void ResetRun()
    {
        generator.ClearHistory();
        stats.ResetBuild();
        RollOffer();
    }

    // Teks disusun hanya saat build atau tawaran berubah, bukan setiap frame OnGUI.
    private void RefreshTexts()
    {
        buildText = BuildStatsText();

        cardTexts.Clear();

        foreach (MathGateData gate in offer)
            cardTexts.Add(BuildCardText(gate));
    }

    private string BuildStatsText()
    {
        builder.Clear();
        builder.Append("<b>Build MC</b>  (gate ")
            .Append(stats.SelectedGates.Count).Append('/').Append(maxGatesPerRun).Append(")\n");

        for (int i = 0; i < StatTypes.Count; i++)
        {
            StatType stat = (StatType)i;
            float baseValue = stats.GetBase(stat);
            float value = stats.Get(stat);

            builder.Append(stat).Append("  ").Append(value.ToString("0.##"));

            if (!Mathf.Approximately(baseValue, value))
                builder.Append("  <color=#999999>(base ").Append(baseValue.ToString("0.##")).Append(")</color>");

            builder.Append('\n');
        }

        builder.Append("\n<b>Gate terpilih</b>\n");

        if (stats.SelectedGates.Count == 0)
            builder.Append("-\n");

        for (int i = 0; i < stats.SelectedGates.Count; i++)
            builder.Append(i + 1).Append(". ").Append(stats.SelectedGates[i].DisplayName).Append('\n');

        return builder.ToString();
    }

    private string BuildCardText(MathGateData gate)
    {
        builder.Clear();
        builder.Append("<b>").Append(gate.DisplayName).Append("</b>\n");
        builder.Append("<color=#999999>").Append(gate.Rarity);

        foreach (string tag in gate.PreviewTags)
            builder.Append(" · ").Append(tag);

        builder.Append("</color>\n\n");

        foreach (ModifierEffect effect in gate.Effects)
        {
            string color = effect.IsBuff ? "#55dd55" : effect.IsTradeOff ? "#ff6666" : "#cccccc";
            builder.Append("<color=").Append(color).Append('>').Append(effect).Append("</color>\n");
        }

        // Preview hanya menampilkan stat yang berubah.
        StatSnapshot preview = stats.Preview(gate);

        builder.Append("\n<b>Jika dipilih</b>\n");

        for (int i = 0; i < StatTypes.Count; i++)
        {
            StatType stat = (StatType)i;
            float now = stats.Get(stat);
            float after = preview.Get(stat);

            if (Mathf.Approximately(now, after))
                continue;

            string color = after > now ? "#55dd55" : "#ff6666";

            builder.Append(stat).Append("  ").Append(now.ToString("0.##"))
                .Append(" → <color=").Append(color).Append('>').Append(after.ToString("0.##")).Append("</color>\n");
        }

        return builder.ToString();
    }

    private void OnGUI()
    {
        if (!showPanel || stats == null || generator == null)
            return;

        if (richLabel == null)
        {
            richLabel = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        }

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        GUILayout.BeginArea(new Rect(Margin, Margin, 230f, 400f), GUI.skin.box);
        GUILayout.Label(buildText, richLabel);

        if (GUILayout.Button("Reset build"))
            pendingReset = true;

        if (offer.Count > 0 && GUILayout.Button("Roll ulang"))
            pendingReroll = true;

        GUILayout.EndArea();

        float x = Margin * 2f + 230f;

        if (offer.Count == 0)
        {
            GUI.Label(
                new Rect(x, Margin, CardWidth * 2f, 40f),
                "<b>Build selesai.</b> Tekan Reset build untuk run baru.",
                richLabel
            );
            return;
        }

        for (int i = 0; i < offer.Count && i < cardTexts.Count; i++)
        {
            Rect card = new Rect(x + i * (CardWidth + Margin), Margin, CardWidth, CardHeight);

            GUILayout.BeginArea(card, GUI.skin.box);
            GUILayout.Label(cardTexts[i], richLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Pilih"))
                pendingPick = offer[i];

            GUILayout.EndArea();
        }
    }
}
