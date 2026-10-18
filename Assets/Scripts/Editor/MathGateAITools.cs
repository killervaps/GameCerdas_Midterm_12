using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Menu MathGate untuk data role dan komponen AI pada prefab dummy.
public static class MathGateAITools
{
    private const string RoleFolder = "Assets/Data/Roles";
    private const string EnemyPrefabFolder = "Assets/Prefabs/Enemies";

    [MenuItem("MathGate/Create/Enemy Role Data (3)")]
    public static void CreateRoleData()
    {
        EnsureFolder(RoleFolder);

        int created = 0;

        // Nilai awal dari tabel role. Attack Range 2 untuk serangan dummy, jarak kerja khusus role menyusul di brain masing-masing.
        created += CreateRole(EnemyRole.Charger, 60f, 4.5f, 14f, 120f, 15f, 2.5f, 2f);
        created += CreateRole(EnemyRole.Shooter, 40f, 3.5f, 16f, 140f, 8f, 1.5f, 2f);
        created += CreateRole(EnemyRole.Guardian, 150f, 2.5f, 10f, 360f, 20f, 2f, 2f);

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: " + created + " Role Data dibuat di " + RoleFolder + " (yang sudah ada tidak ditimpa).");
    }

    // Menambahkan NavMeshAgent, komponen AI bersama, brain dummy, dan label debug ke objek musuh yang dipilih.
    // Bisa objek di scene (mis. template Charger/Shooter/Guardian) maupun prefab asset di Project.
    // Role ditentukan dari Role Data yang sudah terpasang, atau dari nama objek (mengandung Charger, Shooter, atau Guardian).
    // Komponen yang sudah ada tidak diganti, jadi aman dijalankan ulang.
    [MenuItem("MathGate/Setup/4. Enemy AI Components (objek terpilih)")]
    public static void SetupSelectedEnemies()
    {
        GameObject[] selection = Selection.gameObjects;

        if (selection.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "MathGate",
                "Pilih dulu objek musuh di Hierarchy atau prefab musuh di Project, lalu jalankan menu ini lagi.",
                "OK"
            );
            return;
        }

        CreateRoleData();

        int done = 0;

        foreach (GameObject go in selection)
        {
            if (EditorUtility.IsPersistent(go))
            {
                if (SetupPrefabAsset(go))
                    done++;
            }
            else if (SetupSceneObject(go))
            {
                done++;
            }
        }

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: " + done + " musuh diberi komponen AI. Jika objek di scene, simpan scene (Ctrl+S).");
    }

    // Versi lama untuk prefab hasil menu Dummy Role Prefabs.
    [MenuItem("MathGate/Setup/4b. Enemy AI Components (prefab Dummy_*)")]
    public static void UpgradeDummyPrefabs()
    {
        CreateRoleData();

        int upgraded = 0;

        foreach (EnemyRole role in new[] { EnemyRole.Charger, EnemyRole.Shooter, EnemyRole.Guardian })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabFolder + "/Dummy_" + role + ".prefab");

            if (prefab != null && SetupPrefabAsset(prefab))
                upgraded++;
        }

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: " + upgraded + " prefab dummy diperbarui dengan komponen AI.");
    }

    private static bool SetupSceneObject(GameObject go)
    {
        if (!TryResolveRole(go, out EnemyRole role))
            return false;

        Undo.RegisterFullObjectHierarchyUndo(go, "Setup Enemy AI");

        ApplyComponents(go, role, true);

        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

        return true;
    }

    private static bool SetupPrefabAsset(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);

        if (!TryResolveRole(prefab, out EnemyRole role))
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            ApplyComponents(root, role, false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return true;
    }

    private static void ApplyComponents(GameObject root, EnemyRole role, bool useUndo)
    {
        EnemyRoleData data = AssetDatabase.LoadAssetAtPath<EnemyRoleData>(RolePath(role));

        bool addedAgent = root.GetComponent<NavMeshAgent>() == null;
        NavMeshAgent agent = GetOrAdd<NavMeshAgent>(root, useUndo);

        if (addedAgent)
        {
            // Ukuran agent diambil dari CapsuleCollider supaya ikut skala model yang sudah disesuaikan.
            // Radius dan tinggi agent sama-sama dikali skala objek, sama seperti collider.
            CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();

            agent.radius = capsule != null ? capsule.radius : 0.5f;
            agent.height = capsule != null ? capsule.height : 2f;
            agent.baseOffset = 0f;
        }

        GetOrAdd<EnemyHealth>(root, useUndo);
        GetOrAdd<EnemyBlackboard>(root, useUndo);
        GetOrAdd<EnemyPerception>(root, useUndo);
        GetOrAdd<EnemyMotor>(root, useUndo);
        GetOrAdd<RendererTint>(root, useUndo);

        EnemyController controller = GetOrAdd<EnemyController>(root, useUndo);

        SerializedObject so = new SerializedObject(controller);
        SerializedProperty roleProperty = so.FindProperty("roleData");

        if (roleProperty.objectReferenceValue == null)
        {
            roleProperty.objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        if (root.GetComponent<EnemyBrain>() == null)
            Add<DummyChaseBrain>(root, useUndo);

        GetOrAdd<EnemyDebugLabel>(root, useUndo);
    }

    private static bool TryResolveRole(GameObject go, out EnemyRole role)
    {
        EnemyController existing = go.GetComponent<EnemyController>();

        if (existing != null && existing.RoleData != null)
        {
            role = existing.RoleData.Role;
            return true;
        }

        string lower = go.name.ToLowerInvariant();

        foreach (EnemyRole candidate in new[] { EnemyRole.Charger, EnemyRole.Shooter, EnemyRole.Guardian })
        {
            if (lower.Contains(candidate.ToString().ToLowerInvariant()))
            {
                role = candidate;
                return true;
            }
        }

        role = EnemyRole.Charger;

        Debug.LogWarning(
            "MathGate: role untuk " + go.name + " tidak dikenali. Beri nama yang mengandung Charger, Shooter, atau Guardian.",
            go
        );

        return false;
    }

    private static int CreateRole(
        EnemyRole role,
        float maxHealth,
        float moveSpeed,
        float visionRange,
        float visionAngle,
        float damage,
        float cooldown,
        float attackRange)
    {
        string path = RolePath(role);

        if (AssetDatabase.LoadAssetAtPath<EnemyRoleData>(path) != null)
            return 0;

        EnemyRoleData data = ScriptableObject.CreateInstance<EnemyRoleData>();
        data.EditorSetup(role, RoleColors.Get(role), maxHealth, moveSpeed, visionRange, visionAngle, damage, cooldown, attackRange);

        AssetDatabase.CreateAsset(data, path);

        return 1;
    }

    private static string RolePath(EnemyRole role)
    {
        return RoleFolder + "/Role_" + role + ".asset";
    }

    private static T GetOrAdd<T>(GameObject go, bool useUndo) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Add<T>(go, useUndo);
    }

    private static T Add<T>(GameObject go, bool useUndo) where T : Component
    {
        return useUndo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
    }
}
