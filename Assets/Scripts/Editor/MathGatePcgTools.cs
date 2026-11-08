using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Menu MathGate untuk aset dan objek PCG. Semua lewat API Editor, bukan edit YAML manual.
public static class MathGatePcgTools
{
    private const string ItemFolder = "Assets/Prefabs/Items";
    private const string MaterialFolder = "Assets/Materials";
    private const string HealthOrbPath = ItemFolder + "/HealthOrb.prefab";
    private const string HealthOrbMaterialPath = MaterialFolder + "/HealthOrb.mat";

    private static readonly Color OrbColor = new Color(0.3f, 1f, 0.45f);

    [MenuItem("MathGate/PCG/1. Create Health Orb Prefab")]
    public static void CreateHealthOrbPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HealthOrbPath) != null)
        {
            Debug.Log("MathGate: " + HealthOrbPath + " sudah ada, tidak ditimpa. Hapus dulu jika ingin dibuat ulang.");
            return;
        }

        EnsureFolder(ItemFolder);
        EnsureFolder(MaterialFolder);

        GameObject root = new GameObject("HealthOrb");

        // Visual: bola kecil emisif tanpa collider.
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.up * 0.45f;
        visual.transform.localScale = Vector3.one * 0.32f;
        Object.DestroyImmediate(visual.GetComponent<Collider>());

        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateEmissiveMaterial(HealthOrbMaterialPath, OrbColor, 3f);
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        // Cahaya hijau di sekitar orb, ikut naik turun bersama visual.
        GameObject glow = new GameObject("Glow");
        glow.transform.SetParent(visual.transform, false);
        Light light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = OrbColor;
        light.range = 2.5f;
        light.intensity = 2f;
        light.shadows = LightShadows.None;

        HealthOrb orb = root.AddComponent<HealthOrb>();

        SerializedObject so = new SerializedObject(orb);
        so.FindProperty("displayName").stringValue = "Health Orb";
        so.FindProperty("visual").objectReferenceValue = visual.transform;
        so.FindProperty("glowLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, HealthOrbPath);
        Object.DestroyImmediate(root);

        Debug.Log("MathGate: prefab health orb dibuat di " + HealthOrbPath);
    }

    // Menambahkan ItemSpawner ke objek PcgDirector (atau EnemySpawner) dan mengisi tabelnya dengan Health Orb.
    [MenuItem("MathGate/PCG/2. Add Item Spawner To Scene")]
    public static void AddItemSpawner()
    {
        HealthOrb orbPrefab = AssetDatabase.LoadAssetAtPath<HealthOrb>(HealthOrbPath);

        if (orbPrefab == null)
        {
            Debug.LogError("MathGate: " + HealthOrbPath + " belum ada. Jalankan MathGate > PCG > 1 dulu.");
            return;
        }

        ItemSpawner spawner = Object.FindAnyObjectByType<ItemSpawner>(FindObjectsInactive.Include);

        if (spawner == null)
        {
            PcgDirector director = Object.FindAnyObjectByType<PcgDirector>(FindObjectsInactive.Include);
            EnemySpawner enemySpawner = Object.FindAnyObjectByType<EnemySpawner>(FindObjectsInactive.Include);
            GameObject host = director != null ? director.gameObject : enemySpawner != null ? enemySpawner.gameObject : null;

            if (host == null)
            {
                Debug.LogError("MathGate: PcgDirector dan EnemySpawner tidak ditemukan di scene yang terbuka.");
                return;
            }

            spawner = Undo.AddComponent<ItemSpawner>(host);
        }

        SerializedObject so = new SerializedObject(spawner);
        SerializedProperty table = so.FindProperty("items");

        for (int i = 0; i < table.arraySize; i++)
        {
            if (table.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue == orbPrefab)
            {
                Debug.Log("MathGate: ItemSpawner di " + spawner.name + " sudah berisi Health Orb.", spawner);
                Selection.activeObject = spawner;
                return;
            }
        }

        table.arraySize++;
        SerializedProperty entry = table.GetArrayElementAtIndex(table.arraySize - 1);
        entry.FindPropertyRelative("prefab").objectReferenceValue = orbPrefab;
        entry.FindPropertyRelative("weight").intValue = 1;
        entry.FindPropertyRelative("maxPerRound").intValue = 2;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        Selection.activeObject = spawner;

        Debug.Log("MathGate: ItemSpawner di " + spawner.name + " siap, Health Orb masuk tabel item.", spawner);
    }

    // Panel dan overlay debug PCG (ikut F1) di objek PcgDirector.
    [MenuItem("MathGate/PCG/3. Add PCG Debug Panel To Scene")]
    public static void AddDebugPanel()
    {
        PcgDebugPanel existing = Object.FindAnyObjectByType<PcgDebugPanel>(FindObjectsInactive.Include);

        if (existing != null)
        {
            Debug.Log("MathGate: PcgDebugPanel sudah ada di " + existing.name + ".", existing);
            Selection.activeObject = existing;
            return;
        }

        PcgDirector director = Object.FindAnyObjectByType<PcgDirector>(FindObjectsInactive.Include);

        if (director == null)
        {
            Debug.LogError("MathGate: PcgDirector tidak ditemukan di scene yang terbuka.");
            return;
        }

        PcgDebugPanel panel = Undo.AddComponent<PcgDebugPanel>(director.gameObject);
        EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        Selection.activeObject = panel;

        Debug.Log("MathGate: PcgDebugPanel ditambahkan ke " + director.name + ".", panel);
    }

    // Praktikum 10. Membuat atau melengkapi objek DungeonGenerator untuk Mode Dungeon. Aman dijalankan berulang:
    //   - DungeonGenerator di (300, 0, 0), jauh dari Safezone dan arena supaya tidak bertumpuk
    //   - komponen Arena (nama "Dungeon") dan PcgSpawnArea, supaya spawner memperlakukan dungeon seperti arena
    //   - DungeonAtmosphere untuk suasana bawah tanah
    //   - child PlayerEntry sebagai titik masuk MC (dipindah ke Start setiap dungeon dibangun)
    //   - referensi Dungeon di RunManager
    [MenuItem("MathGate/PCG/4. Setup Dungeon Mode")]
    public static void SetupDungeonMode()
    {
        DungeonGenerator generator = Object.FindAnyObjectByType<DungeonGenerator>(FindObjectsInactive.Include);

        if (generator == null)
        {
            PcgDirector director = Object.FindAnyObjectByType<PcgDirector>(FindObjectsInactive.Include);

            GameObject created = new GameObject("DungeonGenerator");
            Undo.RegisterCreatedObjectUndo(created, "Add Dungeon Generator");

            if (director != null)
                SceneManager.MoveGameObjectToScene(created, director.gameObject.scene);

            created.transform.position = new Vector3(300f, 0f, 0f);
            generator = Undo.AddComponent<DungeonGenerator>(created);
        }

        GameObject go = generator.gameObject;

        Arena arena = go.GetComponent<Arena>();

        if (arena == null)
            arena = Undo.AddComponent<Arena>(go);

        if (go.GetComponent<PcgSpawnArea>() == null)
            Undo.AddComponent<PcgSpawnArea>(go);

        // Lighting bawah tanah (fog, ambient, Volume, lentera MC) selama MC di dalam dungeon.
        if (go.GetComponent<DungeonAtmosphere>() == null)
            Undo.AddComponent<DungeonAtmosphere>(go);

        Transform entry = go.transform.Find("PlayerEntry");

        if (entry == null)
        {
            entry = new GameObject("PlayerEntry").transform;
            Undo.RegisterCreatedObjectUndo(entry.gameObject, "Add Dungeon Player Entry");
            entry.SetParent(go.transform, false);
        }

        SerializedObject arenaSo = new SerializedObject(arena);
        arenaSo.FindProperty("displayName").stringValue = "Dungeon";
        arenaSo.FindProperty("playerEntry").objectReferenceValue = entry;
        // Dungeon tidak ikut undian arena, bobot 0 hanya pengaman.
        arenaSo.FindProperty("baseWeight").floatValue = 0f;
        arenaSo.ApplyModifiedProperties();

        SerializedObject generatorSo = new SerializedObject(generator);
        generatorSo.FindProperty("playerEntry").objectReferenceValue = entry;

        // Dekorasi bawaan dari Kenney Mini Dungeon, hanya untuk list yang masih kosong.
        FillDecor(generatorSo, "storageProps", ("barrel", 3f, true), ("pot", 2f, true), ("table", 1f, false), ("chair", 0.6f, false));
        FillDecor(generatorSo, "columnProps", ("column", 1f, false));
        FillDecor(generatorSo, "rubbleProps", ("rocks", 2f, true), ("stones", 2f, true), ("dirt", 1f, true));
        FillDecor(generatorSo, "treasureProps", ("coin", 3f, true), ("pot", 1f, true));
        FillDecor(generatorSo, "wallProps", ("banner", 1f, false));
        FillDecor(generatorSo, "corridorProps", ("wood-support", 1f, false));
        generatorSo.ApplyModifiedProperties();

        RunManager runManager = Object.FindAnyObjectByType<RunManager>(FindObjectsInactive.Include);

        if (runManager != null)
        {
            SerializedObject runSo = new SerializedObject(runManager);
            runSo.FindProperty("dungeon").objectReferenceValue = generator;
            runSo.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(runManager.gameObject.scene);
        }
        else
        {
            Debug.LogWarning("MathGate: RunManager tidak ditemukan di scene yang terbuka. Isi field Dungeon di RunManager secara manual.");
        }

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeObject = generator;

        Debug.Log("MathGate: Mode Dungeon siap di " + go.name + " (" + go.transform.position + "). " +
                  "Klik titik tiga DungeonGenerator > Generate Preview untuk melihat dungeon, lalu simpan scene.", generator);
    }

    private static void FillDecor(SerializedObject so, string field, params (string model, float weight, bool randomYaw)[] items)
    {
        SerializedProperty list = so.FindProperty(field);

        if (list == null || !list.isArray || list.arraySize > 0)
            return;

        foreach ((string model, float weight, bool randomYaw) in items)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Dungeon/" + model + ".fbx");

            if (prefab == null)
                continue;

            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty element = list.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            element.FindPropertyRelative("weight").floatValue = weight;
            element.FindPropertyRelative("scale").floatValue = 1f;
            element.FindPropertyRelative("offset").vector3Value = Vector3.zero;
            element.FindPropertyRelative("rotation").vector3Value = Vector3.zero;
            element.FindPropertyRelative("randomYaw").boolValue = randomYaw;
            // Model Kenney berukuran besar (dirt pun berupa gundukan), jadi props lantai menghalangi.
            element.FindPropertyRelative("blocking").boolValue = true;
        }
    }

    private static Material CreateEmissiveMaterial(string path, Color color, float emission)
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
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * emission);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

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
