using System.Collections.Generic;
using System.Text;
using UnityEngine;

[System.Serializable]
public struct WaveEntry
{
    public EnemyRole role;
    [Min(0)] public int count;

    public WaveEntry(EnemyRole role, int count)
    {
        this.role = role;
        this.count = count;
    }
}

// Komposisi musuh satu ronde. Dibuat saat runtime oleh PCG (PcgEnemyComposition) setiap ronde,
// lalu dibaca kartu gate, preview musuh, HUD, dan spawner. Asset buatan tangan masih bisa dipakai alat Editor.
[CreateAssetMenu(fileName = "Wave_New", menuName = "MathGate/Wave Data")]
public class WaveData : ScriptableObject
{
    [SerializeField] private string waveName;
    [SerializeField] private WaveEntry[] entries;

    // Wave sementara hasil PCG. Tidak disimpan ke Project, hapus dengan Destroy jika sudah tidak dipakai.
    public static WaveData CreateRuntime(string displayName, WaveEntry[] entries)
    {
        WaveData wave = CreateInstance<WaveData>();
        wave.name = displayName;
        wave.waveName = displayName;
        wave.entries = entries;
        wave.hideFlags = HideFlags.DontSave;
        return wave;
    }

    public string DisplayName => string.IsNullOrEmpty(waveName) ? name : waveName;

    public IReadOnlyList<WaveEntry> Entries => entries ?? System.Array.Empty<WaveEntry>();

    public int TotalCount
    {
        get
        {
            int total = 0;

            foreach (WaveEntry entry in Entries)
                total += Mathf.Max(0, entry.count);

            return total;
        }
    }

    public int CountOf(EnemyRole role)
    {
        int total = 0;

        foreach (WaveEntry entry in Entries)
        {
            if (entry.role == role)
                total += Mathf.Max(0, entry.count);
        }

        return total;
    }

    // Mis. "2 Charger" atau "1 Charger, 1 Shooter". Dipakai preview musuh sebelum ronde.
    public string Describe()
    {
        StringBuilder builder = new StringBuilder();

        foreach (WaveEntry entry in Entries)
        {
            if (entry.count <= 0)
                continue;

            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(entry.count).Append(' ').Append(entry.role);
        }

        return builder.Length > 0 ? builder.ToString() : "-";
    }

#if UNITY_EDITOR
    // Dipakai menu Editor untuk membuat wave contoh.
    public void EditorSetup(string newName, WaveEntry[] newEntries)
    {
        waveName = newName;
        entries = newEntries;
    }
#endif
}
