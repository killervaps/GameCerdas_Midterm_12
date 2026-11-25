// Satu objective yang sedang berjalan di sebuah ronde. Dibuat dan dijalankan ObjectiveRunner.
public interface IWaveObjective
{
    ObjectiveType Type { get; }

    // Dipanggil saat ronde mulai, sebelum musuh wave dimunculkan.
    void Begin(ObjectiveContext context);

    void Tick(float deltaTime);

    bool IsComplete { get; }

    // Untuk HUD, 0 sampai 1.
    float Progress01 { get; }

    // Mis. "Brainrot tersisa 4/9", "Bertahan 0:42", "Zona 63%".
    string HudText { get; }

    // Bersih-bersih saat ronde selesai atau run berhenti.
    void End();
}

// Sistem yang boleh dipakai objective. Dungeon null di Mode Arena.
public class ObjectiveContext
{
    public RunManager Run;
    public BattleManager Battle;
    public EnemySpawner Spawner;
    public DungeonGenerator Dungeon;
    public WaveData Wave;
    public int Round;
    // Aliran acak ber-seed ronde ini (PcgStream.Objective), jadi seed sama = objective berjalan sama.
    public SeededRandom Random;
}
