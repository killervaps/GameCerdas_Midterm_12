using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu MathGate untuk UI. GameUI sebenarnya dibuat otomatis saat Play, menu ini hanya
// menaruhnya di scene supaya pengaturan (menu utama, petunjuk, teks judul) bisa diubah di Inspector.
public static class MathGateUITools
{
    [MenuItem("MathGate/Setup/6. Game UI (scene terbuka)")]
    public static void AddGameUI()
    {
        GameUI existing = Object.FindAnyObjectByType<GameUI>(FindObjectsInactive.Include);

        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            Wire(existing);
            Debug.Log("MathGate: GameUI sudah ada di scene, referensi diperbarui.", existing);
            return;
        }

        GameObject go = new GameObject("GameUI");
        Undo.RegisterCreatedObjectUndo(go, "Add Game UI");

        GameUI ui = go.AddComponent<GameUI>();
        Wire(ui);

        Selection.activeObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);

        Debug.Log("MathGate: GameUI ditambahkan. Isi UI dibangun saat Play. Simpan scene (Ctrl+S).", go);
    }

    [MenuItem("MathGate/Debug/Reset Tutorial Hints")]
    public static void ResetHints()
    {
        HintToast.ResetSeen();
        Debug.Log("MathGate: petunjuk tutorial akan muncul lagi.");
    }

    private static void Wire(GameUI ui)
    {
        SerializedObject so = new SerializedObject(ui);

        Assign<RunManager>(so, "runManager");
        Assign<GateSelectionRoom>(so, "gateRoom");
        Assign<BattleManager>(so, "battle");
        Assign<EnemySpawner>(so, "spawner");
        Assign<ThreatEstimator>(so, "threatEstimator");
        Assign<PlayerStats>(so, "playerStats");
        Assign<PlayerHealth>(so, "playerHealth");
        Assign<PlayerMotor>(so, "playerMotor");

        so.ApplyModifiedProperties();
    }

    private static void Assign<T>(SerializedObject so, string field) where T : Object
    {
        SerializedProperty property = so.FindProperty(field);

        if (property == null || property.objectReferenceValue != null)
            return;

        T found = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        if (found != null)
            property.objectReferenceValue = found;
        else
            Debug.LogWarning("MathGate: " + typeof(T).Name + " tidak ditemukan di scene.");
    }
}
