using System.IO;
using UnityEditor;
using UnityEngine;

// Menu MathGate untuk aset alur run: wave contoh dan prefab dummy per role.
public static class MathGateRunTools
{
    private const string WaveFolder = "Assets/Data/Waves";
    private const string EnemyPrefabFolder = "Assets/Prefabs/Enemies";
    private const string TripleTModelPath = "Assets/Models/TripleT+Rigs.fbx";

    [MenuItem("MathGate/Create/Sample Waves (3)")]
    public static void CreateSampleWaves()
    {
        EnsureFolder(WaveFolder);

        int created = 0;

        created += CreateWave("Wave_R1", "Ronde 1",
            new WaveEntry(EnemyRole.Charger, 2));

        created += CreateWave("Wave_R2", "Ronde 2",
            new WaveEntry(EnemyRole.Charger, 1),
            new WaveEntry(EnemyRole.Shooter, 1));

        created += CreateWave("Wave_R3", "Ronde 3",
            new WaveEntry(EnemyRole.Charger, 1),
            new WaveEntry(EnemyRole.Shooter, 1),
            new WaveEntry(EnemyRole.Guardian, 1));

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: " + created + " wave dibuat di " + WaveFolder + " (yang sudah ada tidak ditimpa).");
    }

    // Placeholder sampai brain per role dibuat. Semua memakai model TripleT, dibedakan warna, skala, dan HP.
    [MenuItem("MathGate/Create/Dummy Role Prefabs (3)")]
    public static void CreateDummyRolePrefabs()
    {
        int layer = LayerMask.NameToLayer(GameLayers.Enemy);

        if (layer < 0)
        {
            Debug.LogError("MathGate: layer Enemy belum ada. Jalankan MathGate > Setup > 1 dulu.");
            return;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(TripleTModelPath);

        if (model == null)
        {
            Debug.LogError("MathGate: model " + TripleTModelPath + " tidak ditemukan.");
            return;
        }

        EnsureFolder(EnemyPrefabFolder);

        int created = 0;

        // HP dari tabel nilai awal role. Skala hanya pembeda visual sementara.
        created += CreateRolePrefab(model, layer, EnemyRole.Charger, 60f, 1f);
        created += CreateRolePrefab(model, layer, EnemyRole.Shooter, 40f, 0.85f);
        created += CreateRolePrefab(model, layer, EnemyRole.Guardian, 150f, 1.3f);

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: " + created + " prefab dummy dibuat di " + EnemyPrefabFolder + " (yang sudah ada tidak ditimpa).");
    }

    private static int CreateWave(string fileName, string displayName, params WaveEntry[] entries)
    {
        string path = WaveFolder + "/" + fileName + ".asset";

        if (AssetDatabase.LoadAssetAtPath<WaveData>(path) != null)
            return 0;

        WaveData wave = ScriptableObject.CreateInstance<WaveData>();
        wave.EditorSetup(displayName, entries);

        AssetDatabase.CreateAsset(wave, path);

        return 1;
    }

    private static int CreateRolePrefab(GameObject model, int layer, EnemyRole role, float maxHealth, float scale)
    {
        string path = EnemyPrefabFolder + "/Dummy_" + role + ".prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            return 0;

        GameObject enemy = (GameObject)PrefabUtility.InstantiatePrefab(model);
        enemy.name = "Dummy_" + role;
        enemy.transform.localScale = Vector3.one * scale;

        foreach (Transform t in enemy.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;

        Animator animator = enemy.GetComponent<Animator>();

        if (animator != null)
            animator.applyRootMotion = false;

        // Ukuran disamakan dengan collider Charger yang sudah dipasang di scene arena.
        CapsuleCollider capsule = enemy.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1.35f, 0f);
        capsule.height = 2.7f;
        capsule.radius = 0.5f;

        EnemyHealth health = enemy.AddComponent<EnemyHealth>();
        health.SetMaxHealth(maxHealth);

        RendererTint tint = enemy.AddComponent<RendererTint>();
        tint.SetColor(RoleColors.Get(role));

        PrefabUtility.SaveAsPrefabAsset(enemy, path);
        Object.DestroyImmediate(enemy);

        return 1;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
    }
}
