using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Menu MathGate untuk membuat gate contoh dan memeriksa rumus penumpukan stat.
public static class MathGateGateTools
{
    private const string GateFolder = "Assets/Data/Gates";

    private const string BajaPath = GateFolder + "/Gate_Baja.asset";
    private const string KacaPath = GateFolder + "/Gate_Kaca.asset";
    private const string PelariPath = GateFolder + "/Gate_Pelari.asset";
    private const string PembagiPath = GateFolder + "/Gate_Pembagi.asset";

    [MenuItem("MathGate/Create/Sample Gates (4)")]
    public static void CreateSampleGates()
    {
        Directory.CreateDirectory(GateFolder);
        AssetDatabase.Refresh();

        int created = 0;

        // TODO(desain): rarity tiap gate belum ditentukan, sementara Baja/Pelari Common dan Kaca/Pembagi Rare.
        created += CreateGate(
            BajaPath, "Gerbang Baja",
            "Tubuh mengeras, langkah memberat.",
            GateRarity.Common, new[] { "Tahan", "Lambat" },
            new ModifierEffect(StatType.Defense, ModifierOp.Multiply, 1.5f),
            new ModifierEffect(StatType.MoveSpeed, ModifierOp.Multiply, 0.8f)
        );

        created += CreateGate(
            KacaPath, "Gerbang Kaca",
            "Aura menajam, tubuh serapuh kaca.",
            GateRarity.Rare, new[] { "Agresif", "Rapuh" },
            new ModifierEffect(StatType.Damage, ModifierOp.Multiply, 2f),
            new ModifierEffect(StatType.MaxHP, ModifierOp.Multiply, 0.6f)
        );

        created += CreateGate(
            PelariPath, "Gerbang Pelari",
            "Kaki ringan, pukulan ikut ringan.",
            GateRarity.Common, new[] { "Cepat", "Lemah" },
            new ModifierEffect(StatType.MoveSpeed, ModifierOp.Multiply, 1.3f),
            new ModifierEffect(StatType.Damage, ModifierOp.Subtract, 3f)
        );

        created += CreateGate(
            PembagiPath, "Gerbang Pembagi",
            "Nyawa bertambah, aura terbagi dua.",
            GateRarity.Rare, new[] { "Tahan lama", "Lambat membunuh" },
            new ModifierEffect(StatType.MaxHP, ModifierOp.Add, 40f),
            new ModifierEffect(StatType.Damage, ModifierOp.Divide, 2f)
        );

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: " + created + " gate contoh dibuat di " + GateFolder + " (yang sudah ada tidak ditimpa).");
    }

    // Mengecek contoh penumpukan Baja → Pelari → Kaca terhadap angka yang diharapkan, di semua urutan.
    [MenuItem("MathGate/Debug/Verify Stat Stacking")]
    public static void VerifyStatStacking()
    {
        MathGateData baja = AssetDatabase.LoadAssetAtPath<MathGateData>(BajaPath);
        MathGateData pelari = AssetDatabase.LoadAssetAtPath<MathGateData>(PelariPath);
        MathGateData kaca = AssetDatabase.LoadAssetAtPath<MathGateData>(KacaPath);

        if (baja == null || pelari == null || kaca == null)
        {
            Debug.LogError("MathGate: jalankan dulu MathGate > Create > Sample Gates (4).");
            return;
        }

        StatDefinition[] baseStats = PlayerStats.DefaultBaseStats();

        // Nilai harapan setelah ketiga gate dipilih.
        Dictionary<StatType, float> expected = new Dictionary<StatType, float>
        {
            { StatType.MoveSpeed, 5.2f },
            { StatType.Defense, 15f },
            { StatType.Damage, 14f },
            { StatType.MaxHP, 60f }
        };

        MathGateData[][] orders =
        {
            new[] { baja, pelari, kaca },
            new[] { baja, kaca, pelari },
            new[] { pelari, baja, kaca },
            new[] { pelari, kaca, baja },
            new[] { kaca, baja, pelari },
            new[] { kaca, pelari, baja }
        };

        bool allPassed = true;

        foreach (MathGateData[] order in orders)
        {
            List<ModifierEffect> modifiers = new List<ModifierEffect>();

            foreach (MathGateData gate in order)
                modifiers.AddRange(gate.Effects);

            string label = order[0].DisplayName + " → " + order[1].DisplayName + " → " + order[2].DisplayName;
            string result = string.Empty;
            bool passed = true;

            foreach (StatDefinition definition in baseStats)
            {
                float value = StatCalculator.Calculate(
                    definition.stat,
                    definition.baseValue,
                    definition.clampMin,
                    modifiers
                );

                result += definition.stat + " " + value.ToString("0.##") + "  ";

                if (expected.TryGetValue(definition.stat, out float target) && Mathf.Abs(value - target) > 0.001f)
                    passed = false;
            }

            allPassed &= passed;

            if (passed)
                Debug.Log("LULUS  " + label + "\n" + result);
            else
                Debug.LogError("GAGAL  " + label + "\n" + result);
        }

        Debug.Log(
            allPassed
                ? "MathGate: semua urutan menghasilkan Speed 5.2, Defense 15, Damage 14, MaxHP 60."
                : "MathGate: ada urutan yang tidak sesuai, cek log GAGAL di atas."
        );
    }

    private static int CreateGate(
        string path,
        string gateName,
        string description,
        GateRarity rarity,
        string[] tags,
        params ModifierEffect[] effects)
    {
        if (AssetDatabase.LoadAssetAtPath<MathGateData>(path) != null)
            return 0;

        MathGateData gate = ScriptableObject.CreateInstance<MathGateData>();
        gate.EditorSetup(gateName, description, rarity, tags, effects);

        AssetDatabase.CreateAsset(gate, path);

        return 1;
    }
}
