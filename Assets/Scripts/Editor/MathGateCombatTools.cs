using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu MathGate untuk aset dan objek uji combat. Semua lewat API Editor, bukan edit YAML manual.
public static class MathGateCombatTools
{
    private const string PrefabFolder = "Assets/Prefabs";
    private const string MaterialFolder = "Assets/Materials";
    private const string ProjectilePrefabPath = PrefabFolder + "/AuraProjectile.prefab";
    private const string TripleTModelPath = "Assets/Models/TripleT+Rigs.fbx";

    [MenuItem("MathGate/Create/Aura Projectile Prefab")]
    public static void CreateAuraProjectilePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath) != null)
        {
            Debug.Log("MathGate: " + ProjectilePrefabPath + " sudah ada, tidak ditimpa.");
            return;
        }

        int layer = LayerMask.NameToLayer(GameLayers.PlayerProjectile);

        if (layer < 0)
        {
            Debug.LogError("MathGate: layer PlayerProjectile belum ada. Jalankan MathGate > Setup > 1 dulu.");
            return;
        }

        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "AuraProjectile";
        go.layer = layer;
        go.transform.localScale = Vector3.one * 0.3f;

        // Proyektil memakai SphereCast, collider bawaan primitive tidak dibutuhkan.
        Object.DestroyImmediate(go.GetComponent<Collider>());

        go.GetComponent<MeshRenderer>().sharedMaterial =
            CreateMaterial(MaterialFolder + "/AuraProjectile.mat", new Color(0.55f, 0.3f, 1f), 2f);

        go.AddComponent<Projectile>();

        PrefabUtility.SaveAsPrefabAsset(go, ProjectilePrefabPath);
        Object.DestroyImmediate(go);

        Debug.Log("MathGate: prefab proyektil dibuat di " + ProjectilePrefabPath);
    }

    // Peluru Shooter. Lurus tanpa homing supaya bisa dihindari, di layer EnemyProjectile (menabrak Player dan NavBlock).
    [MenuItem("MathGate/Create/Enemy Projectile Prefab")]
    public static void CreateEnemyProjectilePrefab()
    {
        const string path = PrefabFolder + "/EnemyBolt.prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log("MathGate: " + path + " sudah ada, tidak ditimpa.");
            return;
        }

        int layer = LayerMask.NameToLayer(GameLayers.EnemyProjectile);

        if (layer < 0)
        {
            Debug.LogError("MathGate: layer EnemyProjectile belum ada. Jalankan MathGate > Setup > 1 dulu.");
            return;
        }

        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "EnemyBolt";
        go.layer = layer;
        go.transform.localScale = Vector3.one * 0.15f;

        Object.DestroyImmediate(go.GetComponent<Collider>());

        go.GetComponent<MeshRenderer>().sharedMaterial =
            CreateMaterial(MaterialFolder + "/EnemyBolt.mat", new Color(1f, 0.25f, 0.2f), 2f);

        Projectile projectile = go.AddComponent<Projectile>();

        // Ukuran dan kecepatan disesuaikan dengan arena yang diperkecil. Ubah di prefab jika perlu.
        SerializedObject so = new SerializedObject(projectile);
        so.FindProperty("speed").floatValue = 7f;
        so.FindProperty("lifetime").floatValue = 2.5f;
        so.FindProperty("homingTurnRate").floatValue = 0f;
        so.FindProperty("radius").floatValue = 0.08f;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        Debug.Log("MathGate: prefab peluru musuh dibuat di " + path + ". Pasang di ProjectilePool dengan Pool Id 'EnemyBolt'.");
    }

    [MenuItem("MathGate/Create/Target Markers (scene terbuka)")]
    public static void CreateTargetMarkers()
    {
        GameObject root = new GameObject("TargetMarkers");
        Undo.RegisterCreatedObjectUndo(root, "Create Target Markers");

        EnsureFolder(MaterialFolder);

        CreateMarker(root.transform, "AutoTargetMarker",
            CreateMaterial(MaterialFolder + "/Marker_Auto.mat", new Color(0.3f, 1f, 0.3f), 1f));
        CreateMarker(root.transform, "LockMarker",
            CreateMaterial(MaterialFolder + "/Marker_Lock.mat", new Color(1f, 0.3f, 1f), 1f));

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log("MathGate: marker dibuat. Tarik AutoTargetMarker dan LockMarker ke TargetSelector.");
    }

    [MenuItem("MathGate/Create/Dummy Enemy (scene terbuka)")]
    public static void CreateDummyEnemy()
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

        GameObject enemy = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(enemy, "Create Dummy Enemy");

        enemy.name = "DummyEnemy";

        // Taruh di titik pandang Scene view, di atas lantai.
        SceneView view = SceneView.lastActiveSceneView;
        Vector3 position = view != null ? view.pivot : Vector3.zero;
        position.y = 0f;
        enemy.transform.position = position;

        foreach (Transform t in enemy.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;

        Animator animator = enemy.GetComponent<Animator>();

        if (animator != null)
            animator.applyRootMotion = false;

        // Ukuran awal disamakan dengan CharacterController MC, sesuaikan dengan model jika perlu.
        CapsuleCollider capsule = enemy.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1f, 0f);
        capsule.height = 2f;
        capsule.radius = 0.4f;

        EnemyHealth health = enemy.AddComponent<EnemyHealth>();
        health.SetMaxHealth(60f);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = enemy;

        Debug.Log("MathGate: DummyEnemy dibuat (HP 60, layer Enemy). Geser ke dalam arena.");
    }

    private static void CreateMarker(Transform parent, string markerName, Material material)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = markerName;
        marker.transform.SetParent(parent, false);
        marker.transform.localScale = new Vector3(1.2f, 0.02f, 1.2f);

        Object.DestroyImmediate(marker.GetComponent<Collider>());

        marker.GetComponent<MeshRenderer>().sharedMaterial = material;
        marker.SetActive(false);
    }

    private static Material CreateMaterial(string path, Color color, float emission)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
            return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;

        if (emission > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
    }
}
