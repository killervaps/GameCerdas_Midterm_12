using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;


public static class MathGateSetupTools
{
    // Nama objek induk di scene arena.
    private const string FloorsRoot = "floors";
    private const string WallsRoot = "walls";
    private const string ObstaclesRoot = "obstacles";
    private const string SafezoneRoot = "Safezone";

    // Tebal minimum collider lantai supaya CharacterController tidak tembus tile yang sangat tipis.
    private const float MinColliderThickness = 0.1f;

    // Pasangan layer gameplay yang tidak tercantum di sini diabaikan.
    // Pasangan antar Ground, NavBlock, dan Decor tidak diubah.
    private static readonly Dictionary<string, string[]> CollisionRules =
        new Dictionary<string, string[]>
        {
            { GameLayers.Player, new[] { GameLayers.Ground, GameLayers.NavBlock, GameLayers.Enemy, GameLayers.EnemyProjectile } },
            { GameLayers.Enemy, new[] { GameLayers.Ground, GameLayers.NavBlock, GameLayers.Player, GameLayers.PlayerProjectile } },
            { GameLayers.PlayerProjectile, new[] { GameLayers.Enemy, GameLayers.NavBlock } },
            { GameLayers.EnemyProjectile, new[] { GameLayers.Player, GameLayers.NavBlock } }
        };

    [MenuItem("MathGate/Setup/1. Layers & Physics Matrix")]
    public static void SetupLayersAndPhysics()
    {
        if (!CreateLayers())
            return;

        ApplyCollisionMatrix();

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: layer gameplay dan physics matrix sudah diatur.");
    }

    [MenuItem("MathGate/Setup/2. Arena Colliders & Layers (scene terbuka)")]
    public static void SetupArenaColliders()
    {
        if (!LayersExist())
        {
            EditorUtility.DisplayDialog(
                "MathGate",
                "Jalankan dulu menu '1. Layers & Physics Matrix'.",
                "OK"
            );
            return;
        }

        int floors = SetupRoot(FloorsRoot, LayerMask.NameToLayer(GameLayers.Ground));
        int walls = SetupRoot(WallsRoot, LayerMask.NameToLayer(GameLayers.NavBlock));
        int obstacles = SetupRoot(ObstaclesRoot, LayerMask.NameToLayer(GameLayers.NavBlock));
        int safezone = SetupMixedRoot(SafezoneRoot);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log(
            "MathGate: collider ditambahkan. floors " + floors +
            ", walls " + walls +
            ", obstacles " + obstacles +
            ", Safezone " + safezone +
            ". Simpan scene (Ctrl+S)."
        );
    }

    [MenuItem("MathGate/Setup/3. Configure NavMeshSurface (scene terbuka)")]
    public static void ConfigureNavMeshSurface()
    {
        NavMeshSurface[] surfaces = Object.FindObjectsByType<NavMeshSurface>();

        if (surfaces.Length == 0)
        {
            Debug.LogWarning("MathGate: tidak ada NavMeshSurface di scene terbuka.");
            return;
        }

        int mask = LayerMask.GetMask(GameLayers.Ground, GameLayers.NavBlock);

        foreach (NavMeshSurface surface in surfaces)
        {
            Undo.RecordObject(surface, "Configure NavMeshSurface");

            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = mask;

            EditorUtility.SetDirty(surface);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log(
            "MathGate: NavMeshSurface memakai Physics Colliders, layer Ground + NavBlock. " +
            "Tekan Bake di Inspector NavMeshSurface lalu simpan scene."
        );
    }

    private static bool CreateLayers()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
        );
        SerializedProperty layers = tagManager.FindProperty("layers");

        foreach (string layerName in GameLayers.All)
        {
            if (LayerMask.NameToLayer(layerName) >= 0)
                continue;

            // Slot 0-7 milik Unity, layer pengguna mulai dari 8.
            int slot = -1;

            for (int i = 8; i < layers.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                Debug.LogError("MathGate: slot layer penuh, tidak bisa menambah " + layerName);
                return false;
            }

            layers.GetArrayElementAtIndex(slot).stringValue = layerName;

            // Simpan per layer supaya NameToLayer pada iterasi berikutnya melihat slot yang sudah terisi.
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        return true;
    }

    private static void ApplyCollisionMatrix()
    {
        foreach (KeyValuePair<string, string[]> rule in CollisionRules)
        {
            int self = LayerMask.NameToLayer(rule.Key);

            foreach (string other in GameLayers.All)
            {
                int otherLayer = LayerMask.NameToLayer(other);
                bool collide = System.Array.IndexOf(rule.Value, other) >= 0;

                // Pasangan yang juga punya aturan sendiri hanya bertabrakan jika keduanya saling mencantumkan.
                if (CollisionRules.TryGetValue(other, out string[] otherRule))
                    collide = collide && System.Array.IndexOf(otherRule, rule.Key) >= 0;

                Physics.IgnoreLayerCollision(self, otherLayer, !collide);
            }
        }
    }

    private static bool LayersExist()
    {
        foreach (string layerName in GameLayers.All)
        {
            if (LayerMask.NameToLayer(layerName) < 0)
                return false;
        }

        return true;
    }

    // Tiap anak langsung dari root dianggap satu tile/objek. Collider ditambahkan di objek anak itu dengan ukuran dari gabungan bounds mesh di bawahnya.
    private static int SetupRoot(string rootName, int layer)
    {
        GameObject root = GameObject.Find(rootName);

        if (root == null)
        {
            Debug.LogWarning("MathGate: objek '" + rootName + "' tidak ditemukan di scene.");
            return 0;
        }

        Undo.RecordObject(root, "Set Layer");
        root.layer = layer;

        int added = 0;

        foreach (Transform child in root.transform)
        {
            if (SetupTile(child, layer))
                added++;
        }

        return added;
    }

    // Root berisi campuran lantai dan dinding (mis. Safezone). Layer ditentukan dari awalan nama anak.
    // Anak lain (portal gate, spawn point, label) dilewati supaya tidak ikut diberi collider.
    private static int SetupMixedRoot(string rootName)
    {
        GameObject root = GameObject.Find(rootName);

        if (root == null)
        {
            Debug.LogWarning("MathGate: objek '" + rootName + "' tidak ditemukan di scene.");
            return 0;
        }

        int ground = LayerMask.NameToLayer(GameLayers.Ground);
        int navBlock = LayerMask.NameToLayer(GameLayers.NavBlock);
        int added = 0;

        foreach (Transform child in root.transform)
        {
            string childName = child.name.ToLowerInvariant();

            if (childName.StartsWith("floor"))
            {
                if (SetupTile(child, ground))
                    added++;
            }
            else if (childName.StartsWith("wall") || childName.StartsWith("border") || childName.StartsWith("column"))
            {
                if (SetupTile(child, navBlock))
                    added++;
            }
        }

        return added;
    }

    // Mengembalikan true jika collider baru ditambahkan.
    private static bool SetupTile(Transform tile, int layer)
    {
        SetLayerRecursive(tile.gameObject, layer);

        if (tile.GetComponentInChildren<Collider>() != null)
            return false;

        if (!TryGetLocalBounds(tile, out Bounds bounds))
            return false;

        if (layer == LayerMask.NameToLayer(GameLayers.Ground) && bounds.size.y < MinColliderThickness)
        {
            // Tebalkan ke bawah, permukaan atas tetap.
            float top = bounds.max.y;
            bounds.size = new Vector3(bounds.size.x, MinColliderThickness, bounds.size.z);
            bounds.center = new Vector3(bounds.center.x, top - MinColliderThickness * 0.5f, bounds.center.z);
        }

        BoxCollider box = Undo.AddComponent<BoxCollider>(tile.gameObject);
        box.center = bounds.center;
        box.size = bounds.size;

        return true;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        Undo.RecordObject(go, "Set Layer");
        go.layer = layer;

        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    // Bounds dalam ruang lokal 'owner' dari semua MeshFilter di bawahnya.
    private static bool TryGetLocalBounds(Transform owner, out Bounds bounds)
    {
        bounds = new Bounds();
        bool hasBounds = false;

        Matrix4x4 worldToOwner = owner.worldToLocalMatrix;

        foreach (MeshFilter filter in owner.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds meshBounds = filter.sharedMesh.bounds;
            Matrix4x4 meshToOwner = worldToOwner * filter.transform.localToWorldMatrix;

            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z
                );

                Vector3 point = meshToOwner.MultiplyPoint3x4(corner);

                if (!hasBounds)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }

        return hasBounds;
    }
}
