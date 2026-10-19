using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Menu MathGate untuk memeriksa rumus threat dan skor Shooter tanpa Play Mode.
public static class MathGateThreatTools
{
    private const string GateFolder = "Assets/Data/Gates";
    private const string WaveFolder = "Assets/Data/Waves";
    private const string RoleFolder = "Assets/Data/Roles";

    // Contoh di dokumen: jarak 3, ideal 8, cover 0,8.
    // Baja (Mobilitas 0,8): Kite ≈ 0,44, Reposition ≈ 0,15, terpilih Kite.
    // Pelari (Mobilitas 1,3): Kite ≈ 0,13, Reposition ≈ 0,40, terpilih Reposition.
    [MenuItem("MathGate/Debug/Verify Shooter Kite vs Reposition")]
    public static void VerifyShooterExample()
    {
        float tooClose = ShooterBrain.TooClose(3f, 8f);

        bool passed = true;

        passed &= CheckShooter("Baja", 0.8f, tooClose, 0.44f, 0.15f, true);
        passed &= CheckShooter("Pelari", 1.3f, tooClose, 0.13f, 0.40f, false);

        Debug.Log(passed
            ? "MathGate: contoh Shooter tereproduksi (TooClose " + tooClose.ToString("0.000") + ")."
            : "MathGate: contoh Shooter TIDAK sesuai, cek log GAGAL di atas.");
    }

    // Tabel ThreatRatio tiap gate contoh terhadap tiap wave, dihitung dari build dasar + satu gate.
    [MenuItem("MathGate/Debug/Print Threat Table")]
    public static void PrintThreatTable()
    {
        Dictionary<EnemyRole, EnemyRoleData> roles = new Dictionary<EnemyRole, EnemyRoleData>();

        foreach (EnemyRoleData data in LoadAll<EnemyRoleData>(RoleFolder))
            roles[data.Role] = data;

        List<WaveData> waves = LoadAll<WaveData>(WaveFolder);
        List<MathGateData> gates = LoadAll<MathGateData>(GateFolder);

        if (roles.Count == 0 || waves.Count == 0)
        {
            Debug.LogError("MathGate: butuh Role Data di " + RoleFolder + " dan Wave di " + WaveFolder + ".");
            return;
        }

        waves.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        StatDefinition[] baseStats = PlayerStats.DefaultBaseStats();
        StringBuilder builder = new StringBuilder();

        builder.Append("MathGate: ThreatRatio (Power wave / Power MC), ambang Aman < 0.8, Bahaya > 1.25\n");
        builder.Append("Build".PadRight(18));

        foreach (WaveData wave in waves)
            builder.Append(wave.DisplayName.PadRight(22));

        builder.Append('\n');

        AppendRow(builder, "Dasar", baseStats, null, waves, roles);

        foreach (MathGateData gate in gates)
            AppendRow(builder, gate.DisplayName, baseStats, gate, waves, roles);

        Debug.Log(builder.ToString());
    }

    private static void AppendRow(
        StringBuilder builder,
        string label,
        StatDefinition[] baseStats,
        MathGateData gate,
        List<WaveData> waves,
        Dictionary<EnemyRole, EnemyRoleData> roles)
    {
        float[] values = new float[StatTypes.Count];

        foreach (StatDefinition definition in baseStats)
        {
            values[(int)definition.stat] = StatCalculator.Calculate(
                definition.stat,
                definition.baseValue,
                definition.clampMin,
                gate != null ? gate.Effects : null
            );
        }

        float playerPower = ThreatEstimator.PlayerPower(new StatSnapshot(values));

        builder.Append(label.PadRight(18));

        foreach (WaveData wave in waves)
        {
            float wavePower = 0f;

            foreach (WaveEntry entry in wave.Entries)
            {
                if (roles.TryGetValue(entry.role, out EnemyRoleData data))
                    wavePower += entry.count * ThreatEstimator.EnemyPower(data);
            }

            float ratio = playerPower > 0f ? wavePower / playerPower : 0f;
            string level = ratio < 0.8f ? "Aman" : ratio > 1.25f ? "Bahaya" : "Sedang";

            builder.Append((ratio.ToString("0.00") + " " + level).PadRight(22));
        }

        builder.Append('\n');
    }

    private static bool CheckShooter(string build, float mobility, float tooClose, float expectedKite, float expectedReposition, bool expectKite)
    {
        float kite = ShooterBrain.KiteScore(tooClose, mobility);
        float reposition = ShooterBrain.RepositionScore(tooClose, mobility, 0.8f);

        bool ok = Mathf.Abs(kite - expectedKite) < 0.01f &&
                  Mathf.Abs(reposition - expectedReposition) < 0.01f &&
                  (kite > reposition) == expectKite;

        string message = build + " (mob " + mobility.ToString("0.0") + ")  Kite " + kite.ToString("0.00") +
                         "  Reposition " + reposition.ToString("0.00") + "  terpilih " + (kite > reposition ? "Kite" : "Reposition");

        if (ok)
            Debug.Log("LULUS  " + message);
        else
            Debug.LogError("GAGAL  " + message);

        return ok;
    }

    private static List<T> LoadAll<T>(string folder) where T : Object
    {
        List<T> result = new List<T>();

        if (!AssetDatabase.IsValidFolder(folder))
            return result;

        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder }))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

            if (asset != null)
                result.Add(asset);
        }

        return result;
    }
}
