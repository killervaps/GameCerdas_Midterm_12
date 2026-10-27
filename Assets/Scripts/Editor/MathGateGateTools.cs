using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu MathGate untuk membuat 20 gate, mengisi pool GateGenerator, dan memeriksa rumus penumpukan stat.
public static class MathGateGateTools
{
    private const string GateFolder = "Assets/Data/Gates";

    private const string BajaPath = GateFolder + "/Gate_Baja.asset";
    private const string KacaPath = GateFolder + "/Gate_Kaca.asset";
    private const string PelariPath = GateFolder + "/Gate_Pelari.asset";

    private struct GateSpec
    {
        public string file;
        public string name;
        public string description;
        public GateRarity rarity;
        public string[] tags;
        public ModifierEffect[] effects;
    }

    private static GateSpec Gate(string file, string name, string description, GateRarity rarity, string[] tags, params ModifierEffect[] effects)
    {
        return new GateSpec
        {
            file = file,
            name = name,
            description = description,
            rarity = rarity,
            tags = tags,
            effects = effects
        };
    }

    private static ModifierEffect Add(StatType stat, float value) => new ModifierEffect(stat, ModifierOp.Add, value);
    private static ModifierEffect Sub(StatType stat, float value) => new ModifierEffect(stat, ModifierOp.Subtract, value);
    private static ModifierEffect Mul(StatType stat, float value) => new ModifierEffect(stat, ModifierOp.Multiply, value);
    private static ModifierEffect Div(StatType stat, float value) => new ModifierEffect(stat, ModifierOp.Divide, value);

    // Makin langka makin untung. Common = sidegrade (mengubah gaya main), SIGMA = sangat kuat dengan satu kelemahan.
    // Angka "power" di komentar = perubahan DPS × EHP terhadap build dasar (HP 100, DMG 10, AR 1,5, DEF 10).
    private static GateSpec[] AllGates()
    {
        return new[]
        {
            // ---------- Biasa (Common) ----------
            Gate("Gate_Baja", "Gerbang Baja", "Tubuh mengeras, langkah memberat.",
                GateRarity.Common, new[] { "Tahan", "Lambat" },
                Mul(StatType.Defense, 1.5f), Mul(StatType.MoveSpeed, 0.8f)), // power x1,05

            // Rebalance: Damage -3 terlalu berat (-30%), jadi -2.
            Gate("Gate_Pelari", "Gerbang Pelari", "Kaki ringan, pukulan ikut ringan.",
                GateRarity.Common, new[] { "Cepat", "Lemah" },
                Mul(StatType.MoveSpeed, 1.3f), Sub(StatType.Damage, 2f)), // power x0,8, mobilitas naik

            // Rebalance: dulu Rare dengan Damage ÷2 (power x0,7, rugi). Sekarang Common dengan ÷1,5.
            Gate("Gate_Pembagi", "Gerbang Pembagi", "Nyawa bertambah, aura terbagi.",
                GateRarity.Common, new[] { "Tahan lama", "Lambat membunuh" },
                Add(StatType.MaxHP, 40f), Div(StatType.Damage, 1.5f)), // power x0,93

            Gate("Gate_Teropong", "Gerbang Teropong", "Pandangan menjauh, tangan melambat.",
                GateRarity.Common, new[] { "Jauh", "Lamban" },
                Add(StatType.Range, 2f), Mul(StatType.AttackRate, 0.85f)), // power x0,85, jangkauan +50%

            Gate("Gate_Kafein", "Gerbang Kafein", "Jantung berdebar, pertahanan goyah.",
                GateRarity.Common, new[] { "Cepat tembak", "Gemetar" },
                Mul(StatType.AttackRate, 1.25f), Sub(StatType.Defense, 5f)), // power x1,19

            Gate("Gate_Mukbang", "Gerbang Mukbang", "Perut kenyang, langkah berat.",
                GateRarity.Common, new[] { "Kenyang", "Berat" },
                Add(StatType.MaxHP, 25f), Sub(StatType.MoveSpeed, 0.6f)), // power x1,25

            // ---------- Langka (Rare) ----------
            Gate("Gate_Kaca", "Gerbang Kaca", "Aura menajam, tubuh serapuh kaca.",
                GateRarity.Rare, new[] { "Agresif", "Rapuh" },
                Mul(StatType.Damage, 2f), Mul(StatType.MaxHP, 0.6f)), // power x1,2

            Gate("Gate_FanumTax", "Gerbang Fanum Tax", "Ambil jatah musuh, lupa jaga diri.",
                GateRarity.Rare, new[] { "Rakus", "Bocor" },
                Add(StatType.Damage, 5f), Sub(StatType.Defense, 10f)), // power x1,36

            Gate("Gate_Mewing", "Gerbang Mewing", "Rahang tegas, fokus menahan.",
                GateRarity.Rare, new[] { "Kokoh", "Kalem" },
                Add(StatType.Defense, 15f), Add(StatType.MaxHP, 20f), Mul(StatType.AttackRate, 0.9f)), // power x1,23

            Gate("Gate_Ohio", "Gerbang Ohio", "Hanya di Ohio, lari secepat angin.",
                GateRarity.Rare, new[] { "Lincah", "Tipis" },
                Mul(StatType.MoveSpeed, 1.4f), Add(StatType.Range, 1f), Sub(StatType.MaxHP, 15f)), // power x0,85, build kiting

            Gate("Gate_Rizz", "Gerbang Rizz", "Pesona tak tertahan, tapi harus mendekat.",
                GateRarity.Rare, new[] { "Rentetan", "Dekat" },
                Mul(StatType.AttackRate, 1.4f), Sub(StatType.Range, 1f)), // power x1,4

            // ---------- Sangat Langka (Very Rare) ----------
            Gate("Gate_LiriliLarila", "Gerbang Lirili Larila", "Gajah kaktus berduri, tebal dan pelan.",
                GateRarity.VeryRare, new[] { "Benteng", "Pelan" },
                Mul(StatType.MaxHP, 1.5f), Add(StatType.Defense, 10f), Mul(StatType.MoveSpeed, 0.75f)), // power x1,64

            Gate("Gate_BombardiroCrocodilo", "Gerbang Bombardiro Crocodilo", "Bom dari langit, isi ulangnya lama.",
                GateRarity.VeryRare, new[] { "Artileri", "Lambat isi" },
                Mul(StatType.Damage, 1.6f), Add(StatType.Range, 2f), Div(StatType.AttackRate, 1.25f)), // power x1,28, jangkauan +50%

            Gate("Gate_CappuccinoAssassino", "Gerbang Cappuccino Assassino", "Seteguk kopi, serangan mematikan.",
                GateRarity.VeryRare, new[] { "Pembunuh", "Rapuh" },
                Add(StatType.Damage, 4f), Mul(StatType.AttackRate, 1.2f), Sub(StatType.MaxHP, 15f)), // power x1,43

            Gate("Gate_BallerinaCappuccina", "Gerbang Ballerina Cappuccina", "Menari di antara serangan.",
                GateRarity.VeryRare, new[] { "Seimbang", "Terbuka" },
                Add(StatType.MaxHP, 20f), Add(StatType.Damage, 2f), Add(StatType.MoveSpeed, 0.3f), Sub(StatType.Defense, 8f)), // power x1,33

            // ---------- Epik (Epic) ----------
            Gate("Gate_TralaleroTralala", "Gerbang Tralalero Tralala", "Hiu bersepatu, menyerang tanpa henti.",
                GateRarity.Epic, new[] { "Badai", "Kurus" },
                Mul(StatType.AttackRate, 1.6f), Add(StatType.Damage, 3f), Mul(StatType.MaxHP, 0.85f)), // power x1,77

            Gate("Gate_BrrBrrPatapim", "Gerbang Brr Brr Patapim", "Akar hutan menancap, tubuh tak tumbang.",
                GateRarity.Epic, new[] { "Raksasa", "Tumpul" },
                Mul(StatType.MaxHP, 2f), Add(StatType.Defense, 10f), Div(StatType.Damage, 1.25f)), // power x1,75

            Gate("Gate_Mogging", "Gerbang Mogging", "Menatap musuh sampai menciut.",
                GateRarity.Epic, new[] { "Dominan", "Berat" },
                Mul(StatType.Damage, 1.8f), Mul(StatType.Defense, 1.5f), Mul(StatType.MoveSpeed, 0.8f)), // power x1,88

            // ---------- Legendaris (Legendary) ----------
            Gate("Gate_Gigachad", "Gerbang Gigachad", "Rahang baja, otot baja, tidak pernah buru-buru.",
                GateRarity.Legendary, new[] { "Perkasa", "Santai" },
                Mul(StatType.Damage, 2f), Mul(StatType.MaxHP, 1.5f), Mul(StatType.AttackRate, 0.85f)), // power x2,55

            // ---------- SIGMA ----------
            Gate("Gate_Sigma", "Gerbang SIGMA", "Sigma tidak butuh armor. Sigma tidak pernah kena.",
                GateRarity.Sigma, new[] { "Grindset", "Tanpa armor" },
                Mul(StatType.Damage, 2f), Mul(StatType.AttackRate, 1.5f), Mul(StatType.MaxHP, 1.5f),
                Add(StatType.Range, 2f), Mul(StatType.MoveSpeed, 1.2f), Sub(StatType.Defense, 10f)) // power x4,1
        };
    }

    [MenuItem("MathGate/Create/All Gates (20) + Pool (scene terbuka)")]
    public static void CreateAllGates()
    {
        Directory.CreateDirectory(GateFolder);
        AssetDatabase.Refresh();

        List<MathGateData> gates = new List<MathGateData>();
        int created = 0;
        int updated = 0;

        foreach (GateSpec spec in AllGates())
        {
            string path = GateFolder + "/" + spec.file + ".asset";
            MathGateData gate = AssetDatabase.LoadAssetAtPath<MathGateData>(path);

            if (gate == null)
            {
                gate = ScriptableObject.CreateInstance<MathGateData>();
                gate.EditorSetup(spec.name, spec.description, spec.rarity, spec.tags, spec.effects);
                AssetDatabase.CreateAsset(gate, path);
                created++;
            }
            else
            {
                // Gate lama ditimpa supaya rebalance dan rarity baru ikut masuk.
                Undo.RecordObject(gate, "Update Gate");
                gate.EditorSetup(spec.name, spec.description, spec.rarity, spec.tags, spec.effects);
                EditorUtility.SetDirty(gate);
                updated++;
            }

            gates.Add(gate);
        }

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: " + created + " gate dibuat, " + updated + " gate diperbarui di " + GateFolder + ".");

        AssignPool(gates);
        AddRarityDebugPanel();
    }

    // Pool GateGenerator di scene diganti dengan 20 gate ini.
    private static void AssignPool(List<MathGateData> gates)
    {
        GateGenerator generator = Object.FindAnyObjectByType<GateGenerator>(FindObjectsInactive.Include);

        if (generator == null)
        {
            Debug.LogWarning("MathGate: GateGenerator tidak ada di scene terbuka. Isi pool-nya manual dari folder " + GateFolder + ".");
            return;
        }

        SerializedObject serialized = new SerializedObject(generator);
        SerializedProperty pool = serialized.FindProperty("pool");
        pool.arraySize = gates.Count;

        for (int i = 0; i < gates.Count; i++)
            pool.GetArrayElementAtIndex(i).objectReferenceValue = gates[i];

        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);

        Debug.Log("MathGate: pool " + generator.name + " diisi " + gates.Count + " gate. Simpan scene (Ctrl+S).", generator);
    }

    private static void AddRarityDebugPanel()
    {
        GateSelectionRoom room = Object.FindAnyObjectByType<GateSelectionRoom>(FindObjectsInactive.Include);

        if (room == null || room.GetComponent<GateRarityDebugPanel>() != null)
            return;

        Undo.AddComponent<GateRarityDebugPanel>(room.gameObject);
        EditorSceneManager.MarkSceneDirty(room.gameObject.scene);

        Debug.Log("MathGate: GateRarityDebugPanel dipasang di " + room.name + ".", room);
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
            Debug.LogError("MathGate: jalankan dulu MathGate > Create > All Gates (20) + Pool.");
            return;
        }

        StatDefinition[] baseStats = PlayerStats.DefaultBaseStats();

        // Nilai harapan setelah ketiga gate dipilih. Damage = (10 - 2) × 2 = 16.
        Dictionary<StatType, float> expected = new Dictionary<StatType, float>
        {
            { StatType.MoveSpeed, 5.2f },
            { StatType.Defense, 15f },
            { StatType.Damage, 16f },
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
                ? "MathGate: semua urutan menghasilkan Speed 5.2, Defense 15, Damage 16, MaxHP 60."
                : "MathGate: ada urutan yang tidak sesuai, cek log GAGAL di atas."
        );
    }
}
