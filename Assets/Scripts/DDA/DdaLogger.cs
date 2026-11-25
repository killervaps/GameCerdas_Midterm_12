using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Menambahkan satu baris CSV per ronde yang dinilai DDA, untuk dibandingkan antar run atau antar mode.
// File ada di Application.persistentDataPath, path-nya dicetak di Console saat mulai.
public class DdaLogger : MonoBehaviour
{
    private const string Header =
        "timestamp,seed,mode,round,duration,kills,damageTaken,hpEnd,nearDeath,score,smoothed," +
        "factorBefore,factorAfter,enemyCount,itemBonus,healRatio,maxTokens,died,waypointDelay";

    // Kosong = komponen di objek yang sama, lalu dicari di scene.
    [SerializeField] private DifficultyModel model;
    [SerializeField] private string fileName = "dda_log.csv";
    [SerializeField] private bool writeFile = true;

    private bool warned;

    public string FilePath => Path.Combine(Application.persistentDataPath, fileName);

    private void Awake()
    {
        if (model == null)
            model = GetComponent<DifficultyModel>();

        if (model == null)
            model = FindAnyObjectByType<DifficultyModel>();
    }

    private void OnEnable()
    {
        if (model != null)
            model.FactorChanged += HandleFactorChanged;
    }

    private void OnDisable()
    {
        if (model != null)
            model.FactorChanged -= HandleFactorChanged;
    }

    private void Start()
    {
        if (writeFile)
            Debug.Log("DdaLogger: log CSV DDA ditulis ke " + FilePath, this);
    }

    private void HandleFactorChanged(float before, float after, WavePerformance perf)
    {
        if (!writeFile)
            return;

        CultureInfo inv = CultureInfo.InvariantCulture;
        StringBuilder line = new StringBuilder();

        line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append(',')
            .Append(PcgDirector.Current != null ? PcgDirector.Current.RunSeed.ToString(inv) : string.Empty).Append(',')
            .Append(model.Mode).Append(',')
            .Append(perf.round.ToString(inv)).Append(',')
            .Append(perf.duration.ToString("0.00", inv)).Append(',')
            .Append(perf.kills.ToString(inv)).Append(',')
            .Append(perf.damageTaken.ToString("0.0", inv)).Append(',')
            .Append(perf.hpEndRatio.ToString("0.000", inv)).Append(',')
            .Append(perf.nearDeathCount.ToString(inv)).Append(',')
            .Append(perf.score.ToString("0.000", inv)).Append(',')
            .Append(model.SmoothedScore.ToString("0.000", inv)).Append(',')
            .Append(before.ToString("0.000", inv)).Append(',')
            .Append(after.ToString("0.000", inv)).Append(',')
            .Append(perf.enemyCount.ToString(inv)).Append(',')
            .Append(model.ItemBonus.ToString(inv)).Append(',')
            // Kosong = mode Off, heal memakai Partial Heal Ratio di RunManager.
            .Append(model.Active ? model.HealRatio.ToString("0.000", inv) : string.Empty).Append(',')
            .Append(model.MaxTokens.ToString(inv)).Append(',')
            .Append(perf.died ? "1" : "0").Append(',')
            .Append(model.WaypointDelay.ToString("0.0", inv));

        try
        {
            string path = FilePath;

            ArchiveIfHeaderChanged(path);

            if (!File.Exists(path))
                File.WriteAllText(path, Header + Environment.NewLine);

            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception exception)
        {
            if (!warned)
                Debug.LogWarning("DdaLogger: gagal menulis " + FilePath + ", " + exception.Message, this);

            warned = true;
        }
    }

    // File dari versi lama dengan kolom berbeda dipindah ke nama lain, supaya kolom di satu file tidak bercampur.
    private void ArchiveIfHeaderChanged(string path)
    {
        if (!File.Exists(path))
            return;

        string firstLine;

        using (StreamReader reader = new StreamReader(path))
            firstLine = reader.ReadLine();

        if (firstLine == Header)
            return;

        string archive = Path.Combine(Path.GetDirectoryName(path),
            Path.GetFileNameWithoutExtension(path) + "_lama_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(path));

        File.Move(path, archive);
        Debug.Log("DdaLogger: kolom CSV berubah, file lama dipindah ke " + archive, this);
    }
}
