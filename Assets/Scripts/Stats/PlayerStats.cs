using System;
using System.Collections.Generic;
using UnityEngine;

// Build MC selama satu run. Modifier dari gate bertumpuk, stat selalu dihitung ulang dari base.
public class PlayerStats : MonoBehaviour
{
    [Header("Base Stats")]
    [SerializeField] private StatDefinition[] baseStats = DefaultBaseStats();

    [Header("Debug")]
    [SerializeField] private bool logChanges = true;

    private readonly List<ModifierEffect> modifiers = new List<ModifierEffect>();
    private readonly List<MathGateData> selectedGates = new List<MathGateData>();

    private readonly float[] baseValues = new float[StatTypes.Count];
    private readonly float[] clampMins = new float[StatTypes.Count];
    private readonly float[] current = new float[StatTypes.Count];

    private bool initialized;

    // Dikirim setiap Recalculate. Pendengar membaca nilai baru lewat Get().
    public event Action StatsChanged;

    public IReadOnlyList<MathGateData> SelectedGates => selectedGates;

    public IReadOnlyList<ModifierEffect> Modifiers => modifiers;

    private void Awake()
    {
        EnsureInitialized();
    }

    public float Get(StatType stat)
    {
        EnsureInitialized();

        return current[(int)stat];
    }

    public float GetBase(StatType stat)
    {
        EnsureInitialized();

        return baseValues[(int)stat];
    }

    public StatSnapshot GetSnapshot()
    {
        EnsureInitialized();

        return new StatSnapshot((float[])current.Clone());
    }

    public void ApplyGate(MathGateData gate)
    {
        if (gate == null)
            return;

        selectedGates.Add(gate);
        modifiers.AddRange(gate.Effects);

        if (logChanges)
            Debug.Log("PlayerStats: gate dipilih " + gate.DisplayName, this);

        Recalculate();
    }

    // Dipanggil saat run baru dimulai.
    public void ResetBuild()
    {
        selectedGates.Clear();
        modifiers.Clear();

        if (logChanges)
            Debug.Log("PlayerStats: build di-reset ke base", this);

        Recalculate();
    }

    public void Recalculate()
    {
        EnsureInitialized();

        for (int i = 0; i < StatTypes.Count; i++)
        {
            current[i] = StatCalculator.Calculate(
                (StatType)i,
                baseValues[i],
                clampMins[i],
                modifiers
            );
        }

        if (logChanges)
            Debug.Log("PlayerStats: " + Describe(), this);

        StatsChanged?.Invoke();
    }

    // Stat hipotetis jika gate dipilih, tanpa mengubah build. Dipakai kartu gate dan threat preview.
    public StatSnapshot Preview(MathGateData gate)
    {
        EnsureInitialized();

        float[] values = new float[StatTypes.Count];
        IReadOnlyList<ModifierEffect> extra = gate != null ? gate.Effects : null;

        for (int i = 0; i < StatTypes.Count; i++)
        {
            values[i] = StatCalculator.Calculate(
                (StatType)i,
                baseValues[i],
                clampMins[i],
                modifiers,
                extra
            );
        }

        return new StatSnapshot(values);
    }

    public string Describe()
    {
        EnsureInitialized();

        System.Text.StringBuilder builder = new System.Text.StringBuilder();

        for (int i = 0; i < StatTypes.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append((StatType)i).Append(' ').Append(current[i].ToString("0.##"));
        }

        return builder.ToString();
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;

        BuildLookup();

        for (int i = 0; i < StatTypes.Count; i++)
            current[i] = Mathf.Max(baseValues[i], clampMins[i]);
    }

    private void BuildLookup()
    {
        bool[] found = new bool[StatTypes.Count];

        if (baseStats != null)
        {
            foreach (StatDefinition definition in baseStats)
            {
                int index = (int)definition.stat;

                baseValues[index] = definition.baseValue;
                clampMins[index] = definition.clampMin;
                found[index] = true;
            }
        }

        for (int i = 0; i < StatTypes.Count; i++)
        {
            if (!found[i])
                Debug.LogWarning("PlayerStats: base stat " + (StatType)i + " belum diisi, dianggap 0.", this);
        }
    }

    private void OnValidate()
    {
        // Perubahan base di Inspector langsung terlihat, baik saat Play maupun di gizmo Edit Mode.
        if (!initialized)
            return;

        BuildLookup();

        if (Application.isPlaying)
        {
            Recalculate();
            return;
        }

        for (int i = 0; i < StatTypes.Count; i++)
            current[i] = Mathf.Max(baseValues[i], clampMins[i]);
    }

    // Klik kanan judul komponen > Reset untuk kembali ke nilai awal desain.
    private void Reset()
    {
        baseStats = DefaultBaseStats();
    }

    public static StatDefinition[] DefaultBaseStats()
    {
        return new[]
        {
            new StatDefinition(StatType.MaxHP, 100f, 1f),
            new StatDefinition(StatType.Damage, 10f, 1f),
            new StatDefinition(StatType.AttackRate, 1.5f, 0.2f),
            new StatDefinition(StatType.Range, 8f, 2f),
            new StatDefinition(StatType.Defense, 10f, 0f),
            new StatDefinition(StatType.MoveSpeed, 5f, 1f)
        };
    }
}
