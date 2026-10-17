// Kontrak yang dibaca UI dan Animation, sehingga keduanya tidak perlu tahu apakah enemy digerakkan Behavior Tree atau Utility AI.
public interface IEnemyBrain
{
    // Label singkat untuk membedakan enemy di UI ("BT", "Utility").
    string BrainName { get; }

    EnemyAction CurrentAction { get; }

    // True saat enemy sedang berhenti dan menoleh di fase Search.
    bool IsScanning { get; }
}
