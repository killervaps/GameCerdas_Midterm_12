using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu MathGate untuk animasi prioritas 1: perbaikan import, pembuatan Animator Controller, dan pemasangan driver.
// Semua lewat API Editor (ModelImporter, AnimatorController), bukan edit .meta atau .controller manual.
public static class MathGateAnimationTools
{
    private const string AnimationFolder = "Assets/Animations";
    private const string ControllerFolder = "Assets/Animations/Controllers";
    private const string UpperBodyMaskPath = ControllerFolder + "/UpperBody.mask";
    private const string ChargerRolePath = "Assets/Data/Roles/Role_Charger.asset";

    // Nama file FBX (tanpa ekstensi) di Assets/Animations.
    private const string IdleClip = "Idle";
    private const string WalkClip = "EnemyWalking";
    private const string RunClip = "Fast Run";
    private const string BackwardClip = "Walking Backwards";
    private const string RunBackwardClip = "Running Backward";
    private const string RunLeftClip = "Left Strafe";
    private const string RunRightClip = "Right Strafe";
    private const string WalkLeftClip = "Left Strafe Walking";
    private const string WalkRightClip = "Right Strafe Walking";
    private const string LookAroundClip = "Looking Around";
    private const string WindupClip = "Standing Taunt Battlecry";
    private const string LungeClip = "JumpAttack";
    private const string RecoverClip = "Orc Idle";
    private const string MeleeClip = "Melee Attack";
    private const string PunchClip = "Punch Combo";
    private const string Magic1HClip = "Standing 1H Magic Attack 01";
    private const string Magic2HClip = "Standing 2H Magic Area Attack 01";
    private const string HitClip = "GettingHit";
    private const string DeathClip = "Death";

    // Animasi yang diulang. Sisanya sekali jalan.
    private static readonly HashSet<string> LoopingClips = new HashSet<string>
    {
        IdleClip, WalkClip, RunClip, BackwardClip, RecoverClip,
        RunBackwardClip, RunLeftClip, RunRightClip, WalkLeftClip, WalkRightClip, LookAroundClip
    };

    private class ControllerSpec
    {
        public string fileName;
        public bool chargerSpecials;
        public string attackClip;
        public bool playerActions;
        // Karakter lambat (Guardian) memakai set jalan di kecepatan penuh supaya kaki tidak terlihat meluncur.
        public bool walkOnly;
        public bool lookAround = true;
    }

    // ---------- 1. Import ----------

    [MenuItem("MathGate/Setup/6. Fix Animation Imports (Humanoid, loop, in place)")]
    public static void FixAnimationImports()
    {
        int fixedCount = 0;

        foreach (string path in Directory.GetFiles(AnimationFolder, "*.fbx"))
        {
            string assetPath = path.Replace('\\', '/');

            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter importer))
                continue;

            string clipName = Path.GetFileNameWithoutExtension(assetPath);

            // Tahap 1: rig Humanoid dengan avatar dari skeleton Mixamo di file itu sendiri.
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            // Tahap 2: pengaturan clip.
            ModelImporterClipAnimation[] clips = importer.clipAnimations;

            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            bool loop = LoopingClips.Contains(clipName);

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips.Length == 1)
                    clips[i].name = clipName;

                clips[i].loopTime = loop;

                // Bake into pose: karakter digerakkan NavMeshAgent/CharacterController, animasi tidak boleh memindahkan root.
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;

                // Locomotion: XZ berdasar Center of Mass supaya ayunan pinggul strafe tidak menggeser badan kiri-kanan,
                // Y berdasar Feet supaya kaki tetap menapak. Animasi aksi (lunge, mati) tetap berdasar Original.
                clips[i].keepOriginalPositionXZ = !loop;
                clips[i].keepOriginalPositionY = !loop;
                clips[i].heightFromFeet = loop;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            fixedCount++;
        }

        Debug.Log("MathGate: " + fixedCount + " FBX animasi diatur (Humanoid, loop, root bake into pose).");
    }

    // ---------- 2. Controller ----------

    [MenuItem("MathGate/Create/Animator Controllers (Charger, Shooter, Guardian, Player)")]
    public static void CreateControllers()
    {
        ControllerSpec[] specs =
        {
            new ControllerSpec { fileName = "Enemy_Charger", chargerSpecials = true, attackClip = PunchClip },
            new ControllerSpec { fileName = "Enemy_Shooter", attackClip = Magic1HClip },
            new ControllerSpec { fileName = "Enemy_Guardian", attackClip = MeleeClip, walkOnly = true },
            new ControllerSpec { fileName = "Player", playerActions = true, lookAround = false }
        };

        bool anyExists = false;

        foreach (ControllerSpec spec in specs)
            anyExists |= File.Exists(ControllerPath(spec.fileName));

        if (anyExists && !EditorUtility.DisplayDialog(
                "MathGate",
                "Sebagian Animator Controller sudah ada di " + ControllerFolder + ". Timpa? Perubahan manual di controller itu akan hilang.",
                "Timpa",
                "Batal"))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder(ControllerFolder))
            AssetDatabase.CreateFolder(AnimationFolder, "Controllers");

        AvatarMask upperBody = GetOrCreateUpperBodyMask();

        foreach (ControllerSpec spec in specs)
            BuildController(spec, upperBody);

        AssetDatabase.SaveAssets();

        Debug.Log("MathGate: Animator Controller dibuat di " + ControllerFolder + ".");
    }

    private static void BuildController(ControllerSpec spec, AvatarMask upperBody)
    {
        string path = ControllerPath(spec.fileName);

        if (File.Exists(path))
            AssetDatabase.DeleteAsset(path);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        controller.AddParameter(AnimParams.Forward, AnimatorControllerParameterType.Float);
        controller.AddParameter(AnimParams.Speed, AnimatorControllerParameterType.Float);
        controller.AddParameter(AnimParams.MoveX, AnimatorControllerParameterType.Float);
        controller.AddParameter(AnimParams.MoveZ, AnimatorControllerParameterType.Float);
        controller.AddParameter(AnimParams.Scanning, AnimatorControllerParameterType.Bool);
        controller.AddParameter(AnimParams.Special, AnimatorControllerParameterType.Int);
        controller.AddParameter(AnimParams.IsDead, AnimatorControllerParameterType.Bool);
        controller.AddParameter(AnimParams.Attack, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(AnimParams.Hit, AnimatorControllerParameterType.Trigger);

        if (spec.playerActions)
        {
            controller.AddParameter(AnimParams.Cast, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimParams.Burst, AnimatorControllerParameterType.Trigger);
        }

        // --- Base layer: locomotion, aksi terkunci Charger, mati ---
        AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;

        // Blend 2D dari kecepatan lokal. Titik (0,0) diam, ring dalam jalan, ring luar lari.
        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = AnimParams.MoveX;
        tree.blendParameterY = AnimParams.MoveZ;

        AddChild(tree, IdleClip, Vector2.zero);

        if (spec.walkOnly)
        {
            // Jalan di kecepatan penuh, lari hanya saat lebih cepat dari biasa (mis. Guardian marah ×1,4).
            AddWalkRing(tree, 1f);
            AddChild(tree, RunClip, new Vector2(0f, 1.4f));
        }
        else
        {
            AddWalkRing(tree, 0.5f);
            AddChild(tree, RunClip, new Vector2(0f, 1f));
            AddChild(tree, RunBackwardClip, new Vector2(0f, -1f));
            AddChild(tree, RunLeftClip, new Vector2(-1f, 0f));
            AddChild(tree, RunRightClip, new Vector2(1f, 0f));
        }

        // Foot IK menahan telapak kaki saat dua animasi dengan langkah berbeda dicampur.
        locomotion.iKOnFeet = true;

        baseMachine.defaultState = locomotion;

        if (spec.lookAround)
        {
            // Menoleh di fase scan Search. Hanya dari locomotion, jadi tidak menyela aksi terkunci.
            AnimatorState look = baseMachine.AddState("LookAround");
            look.motion = LoadClip(LookAroundClip);

            AnimatorStateTransition toLook = locomotion.AddTransition(look);
            toLook.AddCondition(AnimatorConditionMode.If, 0f, AnimParams.Scanning);
            toLook.duration = 0.2f;
            toLook.hasExitTime = false;

            AnimatorStateTransition fromLook = look.AddTransition(locomotion);
            fromLook.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimParams.Scanning);
            fromLook.duration = 0.2f;
            fromLook.hasExitTime = false;
        }

        if (spec.chargerSpecials)
        {
            EnemyRoleData charger = AssetDatabase.LoadAssetAtPath<EnemyRoleData>(ChargerRolePath);

            // Kecepatan state disesuaikan supaya panjang animasi pas dengan durasi fase di Role Data.
            AddSpecialState(baseMachine, locomotion, "Windup", WindupClip, AnimParams.SpecialWindup, charger != null ? charger.WindupDuration : 0f);
            AddSpecialState(baseMachine, locomotion, "Lunge", LungeClip, AnimParams.SpecialLunge, charger != null ? charger.LungeDuration : 0f);
            AddSpecialState(baseMachine, locomotion, "Recover", RecoverClip, AnimParams.SpecialRecover, 0f);
        }

        AnimatorState death = baseMachine.AddState("Death");
        death.motion = LoadClip(DeathClip);

        AnimatorStateTransition toDeath = baseMachine.AddAnyStateTransition(death);
        toDeath.AddCondition(AnimatorConditionMode.If, 0f, AnimParams.IsDead);
        toDeath.duration = 0.1f;
        toDeath.canTransitionToSelf = false;

        // Pengaman: kembali ke locomotion jika IsDead dimatikan (MC dihidupkan lagi).
        AnimatorStateTransition fromDeath = death.AddTransition(locomotion);
        fromDeath.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimParams.IsDead);
        fromDeath.duration = 0.2f;
        fromDeath.hasExitTime = false;

        // --- Layer upper-body: serangan dan kena hit, kaki tetap mengikuti locomotion ---
        controller.AddLayer("Action");

        AnimatorControllerLayer[] layers = controller.layers;
        layers[1].avatarMask = upperBody;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;

        AnimatorStateMachine actionMachine = controller.layers[1].stateMachine;

        // State kosong meneruskan pose base layer.
        AnimatorState empty = actionMachine.AddState("Empty");
        actionMachine.defaultState = empty;

        if (spec.playerActions)
        {
            AddActionState(actionMachine, empty, "Cast", Magic1HClip, AnimParams.Cast);
            AddActionState(actionMachine, empty, "Burst", Magic2HClip, AnimParams.Burst);
        }
        else if (!string.IsNullOrEmpty(spec.attackClip))
        {
            AddActionState(actionMachine, empty, "Attack", spec.attackClip, AnimParams.Attack);
        }

        AddActionState(actionMachine, empty, "Hit", HitClip, AnimParams.Hit);

        EditorUtility.SetDirty(controller);
    }

    private static void AddChild(BlendTree tree, string clipName, Vector2 position)
    {
        AnimationClip clip = LoadClip(clipName);

        if (clip != null)
            tree.AddChild(clip, position);
    }

    private static void AddWalkRing(BlendTree tree, float radius)
    {
        AddChild(tree, WalkClip, new Vector2(0f, radius));
        AddChild(tree, BackwardClip, new Vector2(0f, -radius));
        AddChild(tree, WalkLeftClip, new Vector2(-radius, 0f));
        AddChild(tree, WalkRightClip, new Vector2(radius, 0f));
    }

    private static void AddSpecialState(
        AnimatorStateMachine machine,
        AnimatorState locomotion,
        string stateName,
        string clipName,
        int specialValue,
        float targetDuration)
    {
        AnimationClip clip = LoadClip(clipName);

        AnimatorState state = machine.AddState(stateName);
        state.motion = clip;

        if (clip != null && targetDuration > 0f)
            state.speed = clip.length / targetDuration;

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.Equals, specialValue, AnimParams.Special);
        enter.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimParams.IsDead);
        enter.duration = 0.05f;
        enter.hasExitTime = false;
        enter.canTransitionToSelf = false;

        AnimatorStateTransition exit = state.AddTransition(locomotion);
        exit.AddCondition(AnimatorConditionMode.Equals, AnimParams.SpecialNone, AnimParams.Special);
        exit.duration = 0.15f;
        exit.hasExitTime = false;
    }

    private static void AddActionState(AnimatorStateMachine machine, AnimatorState empty, string stateName, string clipName, string trigger)
    {
        AnimatorState state = machine.AddState(stateName);
        state.motion = LoadClip(clipName);

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.AddCondition(AnimatorConditionMode.IfNot, 0f, AnimParams.IsDead);
        enter.duration = 0.05f;
        enter.hasExitTime = false;
        // Tembakan beruntun boleh memulai ulang animasi yang sama.
        enter.canTransitionToSelf = true;

        AnimatorStateTransition exit = state.AddTransition(empty);
        exit.hasExitTime = true;
        exit.exitTime = 0.85f;
        exit.duration = 0.15f;
    }

    private static AvatarMask GetOrCreateUpperBodyMask()
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);

        if (mask != null)
            return mask;

        mask = new AvatarMask();

        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            AvatarMaskBodyPart part = (AvatarMaskBodyPart)i;

            bool upper = part == AvatarMaskBodyPart.Body ||
                         part == AvatarMaskBodyPart.Head ||
                         part == AvatarMaskBodyPart.LeftArm ||
                         part == AvatarMaskBodyPart.RightArm ||
                         part == AvatarMaskBodyPart.LeftFingers ||
                         part == AvatarMaskBodyPart.RightFingers ||
                         part == AvatarMaskBodyPart.LeftHandIK ||
                         part == AvatarMaskBodyPart.RightHandIK;

            mask.SetHumanoidBodyPartActive(part, upper);
        }

        AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);

        return mask;
    }

    private static AnimationClip LoadClip(string fileName)
    {
        string path = AnimationFolder + "/" + fileName + ".fbx";

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        }

        Debug.LogWarning("MathGate: clip di " + path + " tidak ditemukan.");
        return null;
    }

    private static string ControllerPath(string fileName)
    {
        return ControllerFolder + "/" + fileName + ".controller";
    }

    // ---------- 3. Pasang ----------

    // Untuk objek terpilih: pasang controller sesuai role (atau Player), matikan root motion,
    // tambahkan driver animasi baru, dan lepas script animasi praktikum jika ada.
    [MenuItem("MathGate/Setup/7. Assign Animator + Driver (objek terpilih)")]
    public static void AssignSelected()
    {
        GameObject[] selection = Selection.gameObjects;

        if (selection.Length == 0)
        {
            EditorUtility.DisplayDialog("MathGate", "Pilih dulu Player dan/atau objek musuh, lalu jalankan menu ini lagi.", "OK");
            return;
        }

        int done = 0;

        foreach (GameObject go in selection)
        {
            if (EditorUtility.IsPersistent(go))
            {
                Debug.LogWarning("MathGate: " + go.name + " adalah prefab asset. Untuk prefab, buka prefab-nya lalu pilih objek di dalamnya.", go);
                continue;
            }

            Animator animator = go.GetComponentInChildren<Animator>();

            if (animator == null)
            {
                Debug.LogWarning("MathGate: " + go.name + " tidak punya Animator.", go);
                continue;
            }

            string controllerName;

            if (go.GetComponent<PlayerHealth>() != null)
            {
                controllerName = "Player";
                GetOrAdd<PlayerAnimationDriver>(go);
                RemoveIfPresent<PlayerAnimation>(go);
            }
            else if (go.GetComponent<EnemyController>() is EnemyController enemy && enemy.RoleData != null)
            {
                controllerName = "Enemy_" + enemy.RoleData.Role;
                GetOrAdd<EnemyAnimationDriver>(go);
                RemoveIfPresent<EnemyAnimation>(go);
            }
            else
            {
                Debug.LogWarning("MathGate: " + go.name + " bukan Player atau musuh dengan Role Data.", go);
                continue;
            }

            RuntimeAnimatorController runtime = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath(controllerName));

            if (runtime == null)
            {
                Debug.LogError("MathGate: " + ControllerPath(controllerName) + " belum ada. Jalankan menu Create > Animator Controllers dulu.");
                continue;
            }

            Undo.RecordObject(animator, "Assign Animator");
            animator.runtimeAnimatorController = runtime;
            animator.applyRootMotion = false;

            EditorUtility.SetDirty(animator);
            EditorSceneManager.MarkSceneDirty(go.scene);

            done++;
        }

        Debug.Log("MathGate: " + done + " objek diberi Animator Controller dan driver. Simpan scene (Ctrl+S).");
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    private static void RemoveIfPresent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();

        if (component != null)
            Undo.DestroyObjectImmediate(component);
    }
}
