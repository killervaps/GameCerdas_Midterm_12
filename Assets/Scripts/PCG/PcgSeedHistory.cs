using System;
using System.Collections.Generic;
using UnityEngine;

// Challenge 7: riwayat seed.
// Menyimpan seed run terakhir beserta catatan tiap rondenya (arena dan komposisi musuh),
// supaya seed yang menghasilkan ronde menarik bisa dipilih lalu dimainkan ulang. Alur designer: Generate -> Evaluate -> Select.
// Seed favorit tidak ikut terhapus saat riwayat penuh. Disimpan di PlayerPrefs, jadi tetap ada setelah Play dihentikan.
// Dipasang di objek yang sama dengan PcgDirector. Tampilannya di PcgLabPanel.
[RequireComponent(typeof(PcgDirector))]
public class PcgSeedHistory : MonoBehaviour
{
    [Serializable]
    public class Record
    {
        public int seed;
        public bool favorite;
        // Satu catatan per ronde, index 0 = ronde 1. Mis. "R1 Shooter: 2C 1S".
        // Diisi saat ronde itu selesai dibuat dengan seed ini, jadi ronde yang belum dimainkan masih kosong.
        public List<string> rounds = new List<string>();

        // Mis. "R1 Shooter: 2C 1S  |  R2 Guardian: 1C 2G  |  R3 ...".
        public string Note
        {
            get
            {
                if (rounds == null || rounds.Count == 0)
                    return string.Empty;

                List<string> filled = new List<string>();

                foreach (string round in rounds)
                {
                    if (!string.IsNullOrEmpty(round))
                        filled.Add(round);
                }

                return string.Join("  |  ", filled);
            }
        }
    }

    [Serializable]
    private class SaveData
    {
        public List<Record> records = new List<Record>();
    }

    private const string PrefsKey = "MathGate.PcgSeedHistory";

    // Jumlah seed non-favorit yang disimpan. Favorit tidak dihitung.
    [SerializeField, Min(1)] private int maxRecent = 8;
    [SerializeField] private bool saveBetweenSessions = true;
    // Dicari otomatis jika kosong. Dipakai untuk mencatat arena dan komposisi ronde pertama tiap seed.
    [SerializeField] private RunManager runManager;

    private readonly List<Record> records = new List<Record>();

    private PcgDirector director;

    // Terbaru di atas.
    public IReadOnlyList<Record> Records => records;

    public event Action Changed;

    private void Awake()
    {
        director = GetComponent<PcgDirector>();

        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();

        Load();
    }

    private void OnEnable()
    {
        if (director == null)
            director = GetComponent<PcgDirector>();

        director.SeedChanged += HandleSeedChanged;

        if (runManager != null)
            runManager.RoundPrepared += HandleRoundPrepared;
    }

    private void OnDisable()
    {
        if (director != null)
            director.SeedChanged -= HandleSeedChanged;

        if (runManager != null)
            runManager.RoundPrepared -= HandleRoundPrepared;
    }

    public bool IsFavorite(int seed)
    {
        Record record = Find(seed);
        return record != null && record.favorite;
    }

    public void ToggleFavorite(int seed)
    {
        Record record = Find(seed);

        if (record == null)
            return;

        record.favorite = !record.favorite;
        Trim();
        Save();
        Changed?.Invoke();
    }

    public void Remove(int seed)
    {
        Record record = Find(seed);

        if (record == null)
            return;

        records.Remove(record);
        Save();
        Changed?.Invoke();
    }

    // Menghapus semua seed kecuali favorit.
    public void ClearNonFavorites()
    {
        records.RemoveAll(r => !r.favorite);
        Save();
        Changed?.Invoke();
    }

    private void HandleSeedChanged(int seed)
    {
        Record record = Find(seed);

        if (record != null)
            records.Remove(record);
        else
            record = new Record { seed = seed };

        records.Insert(0, record);

        Trim();
        Save();
        Changed?.Invoke();
    }

    // Setiap ronde yang selesai dibuat dicatat di seed yang sedang dipakai. Regenerate (F5) menimpa catatan ronde yang sama.
    private void HandleRoundPrepared(int round)
    {
        if (round < 1 || director == null || !director.HasRunSeed)
            return;

        Record record = Find(director.RunSeed);

        if (record == null)
            return;

        if (record.rounds == null)
            record.rounds = new List<string>();

        while (record.rounds.Count < round)
            record.rounds.Add(string.Empty);

        record.rounds[round - 1] = "R" + round + " " + ShortArena(runManager.CurrentArena) + ": " + ShortWave(runManager.CurrentWave);

        Save();
        Changed?.Invoke();
    }

    // "Shooter Arena" -> "Shooter", supaya catatan tiga ronde muat di satu baris.
    private static string ShortArena(Arena arena)
    {
        if (arena == null)
            return "-";

        string name = arena.DisplayName;
        return name.EndsWith(" Arena") ? name.Substring(0, name.Length - 6) : name;
    }

    // "2 Charger, 1 Shooter" -> "2C 1S".
    private static string ShortWave(WaveData wave)
    {
        if (wave == null || wave.TotalCount == 0)
            return "-";

        List<string> parts = new List<string>();

        foreach (WaveEntry entry in wave.Entries)
        {
            if (entry.count > 0)
                parts.Add(entry.count + entry.role.ToString().Substring(0, 1));
        }

        return string.Join(" ", parts);
    }

    private Record Find(int seed)
    {
        foreach (Record record in records)
        {
            if (record.seed == seed)
                return record;
        }

        return null;
    }

    // Seed non-favorit paling lama dibuang jika melebihi Max Recent.
    private void Trim()
    {
        int recent = 0;

        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].favorite)
                continue;

            recent++;

            if (recent > maxRecent)
            {
                records.RemoveAt(i);
                i--;
            }
        }
    }

    private void Load()
    {
        records.Clear();

        if (!saveBetweenSessions || !PlayerPrefs.HasKey(PrefsKey))
            return;

        SaveData data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(PrefsKey));

        if (data != null && data.records != null)
            records.AddRange(data.records);
    }

    private void Save()
    {
        if (!saveBetweenSessions)
            return;

        SaveData data = new SaveData();
        data.records.AddRange(records);

        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }
}
