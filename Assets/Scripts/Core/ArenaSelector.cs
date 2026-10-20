using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Memilih arena tiap ronde secara acak berbobot.
// Arena yang dipakai ronde sebelumnya bobotnya dikali Repeat Penalty (0,2 = 5x lebih kecil),
// sisa peluangnya otomatis terbagi ke arena lain.
// Contoh 4 arena: ronde 1 masing-masing 25%. Setelah arena A terpilih, A = 0,2 / 3,2 = 6,25%, arena lain = 1 / 3,2 = 31,25%.
public class ArenaSelector : MonoBehaviour
{
    // Kosongkan untuk memakai semua komponen Arena di scene.
    [SerializeField] private Arena[] arenas;
    [SerializeField, Range(0f, 1f)] private float repeatPenalty = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool logPicks = true;

    private readonly StringBuilder builder = new StringBuilder();

    private Arena previous;

    public Arena Current { get; private set; }

    // Peluang arena saat ini ketika dipilih (0-1). Untuk panel debug.
    public float CurrentPickChance { get; private set; }

    public float RepeatPenalty => repeatPenalty;

    public IReadOnlyList<Arena> Arenas => arenas;

    public event Action<Arena> ArenaPicked;

    private void Awake()
    {
        if (arenas == null || arenas.Length == 0)
            arenas = FindObjectsByType<Arena>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (arenas.Length == 0)
            Debug.LogError("ArenaSelector: belum ada Arena di scene.", this);
    }

    // Dipanggil saat run baru. Ronde pertama kembali ke peluang rata.
    public void ResetHistory()
    {
        previous = null;
        Current = null;
        CurrentPickChance = 0f;
    }

    public float WeightOf(Arena arena)
    {
        if (arena == null)
            return 0f;

        float weight = arena.BaseWeight;

        if (arena == previous)
            weight *= repeatPenalty;

        return weight;
    }

    // Peluang 0-1 arena ini terpilih pada pemilihan berikutnya.
    public float ChanceOf(Arena arena)
    {
        float total = TotalWeight();
        return total > 0f ? WeightOf(arena) / total : 0f;
    }

    public Arena PickNext()
    {
        if (arenas == null || arenas.Length == 0)
            return null;

        if (logPicks)
            LogChances();

        float total = TotalWeight();
        Arena picked = null;

        if (total > 0f)
        {
            float roll = UnityEngine.Random.value * total;

            foreach (Arena arena in arenas)
            {
                float weight = WeightOf(arena);

                if (weight <= 0f)
                    continue;

                picked = arena;

                if (roll < weight)
                    break;

                roll -= weight;
            }
        }
        else
        {
            // Semua bobot nol, pilih acak supaya run tetap jalan.
            picked = arenas[UnityEngine.Random.Range(0, arenas.Length)];
        }

        CurrentPickChance = picked != null ? ChanceOf(picked) : 0f;
        previous = picked;
        Current = picked;

        if (logPicks && picked != null)
            Debug.Log("ArenaSelector: terpilih " + picked.DisplayName, this);

        ArenaPicked?.Invoke(picked);

        return picked;
    }

    private float TotalWeight()
    {
        float total = 0f;

        foreach (Arena arena in arenas)
            total += WeightOf(arena);

        return total;
    }

    private void LogChances()
    {
        builder.Clear();
        builder.Append("ArenaSelector: peluang ");

        foreach (Arena arena in arenas)
        {
            if (arena == null)
                continue;

            builder.Append(arena.DisplayName).Append(' ')
                .Append((ChanceOf(arena) * 100f).ToString("0.#")).Append("%  ");
        }

        Debug.Log(builder.ToString(), this);
    }
}
