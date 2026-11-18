using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu MathGate untuk DDA. Aman dijalankan berulang, komponen yang sudah ada tidak ditambah lagi.
//   - asset DifficultyProfile di Assets/Data/DDA (dibuat jika belum ada)
//   - DifficultyModel, PlayerPerformanceTracker, dan DdaLogger di objek RunManager
//   - DdaDebugPanel di objek RunStatusPanel (atau objek RunManager jika tidak ada)
public static class MathGateDdaTools
{
    private const string ProfileFolder = "Assets/Data/DDA";
    private const string ProfilePath = ProfileFolder + "/DifficultyProfile.asset";

    [MenuItem("MathGate/DDA/Setup DDA (scene terbuka)")]
    public static void SetupDda()
    {
        RunManager runManager = Object.FindAnyObjectByType<RunManager>(FindObjectsInactive.Include);

        if (runManager == null)
        {
            Debug.LogError("MathGate: RunManager tidak ditemukan di scene yang terbuka.");
            return;
        }

        DifficultyProfile profile = LoadOrCreateProfile();
        GameObject host = runManager.gameObject;

        DifficultyModel model = Object.FindAnyObjectByType<DifficultyModel>(FindObjectsInactive.Include);

        if (model == null)
            model = Undo.AddComponent<DifficultyModel>(host);

        SerializedObject modelSo = new SerializedObject(model);

        if (modelSo.FindProperty("profile").objectReferenceValue == null)
            modelSo.FindProperty("profile").objectReferenceValue = profile;

        modelSo.FindProperty("runManager").objectReferenceValue = runManager;
        modelSo.ApplyModifiedProperties();

        if (Object.FindAnyObjectByType<PlayerPerformanceTracker>(FindObjectsInactive.Include) == null)
            Undo.AddComponent<PlayerPerformanceTracker>(model.gameObject);

        if (Object.FindAnyObjectByType<DdaLogger>(FindObjectsInactive.Include) == null)
            Undo.AddComponent<DdaLogger>(model.gameObject);

        if (Object.FindAnyObjectByType<DdaDebugPanel>(FindObjectsInactive.Include) == null)
        {
            RunStatusPanel status = Object.FindAnyObjectByType<RunStatusPanel>(FindObjectsInactive.Include);
            Undo.AddComponent<DdaDebugPanel>(status != null ? status.gameObject : host);
        }

        EditorSceneManager.MarkSceneDirty(host.scene);
        Selection.activeObject = model;

        Debug.Log("MathGate: DDA siap di " + model.name + ", profile " + AssetDatabase.GetAssetPath(modelSo.FindProperty("profile").objectReferenceValue) +
                  ". Simpan scene (Ctrl+S).", model);
    }

    private static DifficultyProfile LoadOrCreateProfile()
    {
        DifficultyProfile profile = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(ProfilePath);

        if (profile != null)
            return profile;

        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        if (!AssetDatabase.IsValidFolder(ProfileFolder))
            AssetDatabase.CreateFolder("Assets/Data", "DDA");

        profile = ScriptableObject.CreateInstance<DifficultyProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: asset DifficultyProfile dibuat di " + ProfilePath, profile);
        return profile;
    }
}
