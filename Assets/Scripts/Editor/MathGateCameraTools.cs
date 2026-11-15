using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu MathGate untuk kamera orang ketiga. Semua lewat API Editor, bukan edit YAML manual.
// Aman dijalankan berulang: komponen yang sudah ada dipakai ulang, nilainya diatur ulang ke bawaan.
public static class MathGateCameraTools
{
    private const string RigName = "ThirdPersonCamera";
    // Titik fokus kamera relatif kaki MC (kira-kira dada).
    private static readonly Vector3 FocusOffset = new Vector3(0f, 1.1f, 0f);

    [MenuItem("MathGate/Camera/Setup Third Person Camera")]
    public static void SetupThirdPersonCamera()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("MathGate",
                "Setup hanya bisa di Edit Mode. Hentikan Play dulu, jalankan menu ini, lalu simpan scene.", "OK");
            return;
        }

        TopDownCameraFollow follow = Object.FindAnyObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);
        PlayerMotor player = Object.FindAnyObjectByType<PlayerMotor>(FindObjectsInactive.Include);

        if (follow == null || player == null)
        {
            EditorUtility.DisplayDialog("MathGate",
                "Butuh TopDownCameraFollow (kamera utama) dan PlayerMotor (MC) di scene yang terbuka.", "OK");
            return;
        }

        // Brain di kamera utama, mati di Edit Mode. TopDownCameraFollow menyalakannya saat Play jika mode Third Person.
        CinemachineBrain brain = GetOrAdd<CinemachineBrain>(follow.gameObject);
        brain.enabled = false;

        ThirdPersonCameraRig existing = Object.FindAnyObjectByType<ThirdPersonCameraRig>(FindObjectsInactive.Include);
        GameObject go = existing != null ? existing.gameObject : null;

        if (go == null)
        {
            go = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(go, "Create Third Person Camera");
        }

        CinemachineCamera cmCamera = GetOrAdd<CinemachineCamera>(go);
        cmCamera.Target.TrackingTarget = player.transform;
        cmCamera.Target.CustomLookAtTarget = false;
        cmCamera.Lens.FieldOfView = 40f;
        cmCamera.Priority = 10;

        // Orbit bola di sekitar dada MC.
        CinemachineOrbitalFollow orbit = GetOrAdd<CinemachineOrbitalFollow>(go);
        orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
        // Cukup dekat supaya lorong dungeon tidak terlihat seluruhnya. Scroll mouse mengubahnya 60%-150%.
        orbit.Radius = 4f;
        orbit.TargetOffset = FocusOffset;
        orbit.TrackerSettings.BindingMode = BindingMode.WorldSpace;
        orbit.TrackerSettings.PositionDamping = Vector3.one * 0.15f;

        InputAxis vertical = orbit.VerticalAxis;
        vertical.Range = new Vector2(-5f, 65f);
        vertical.Center = 20f;
        vertical.Value = 20f;
        orbit.VerticalAxis = vertical;

        InputAxis radial = orbit.RadialAxis;
        radial.Range = new Vector2(0.6f, 1.5f);
        radial.Center = 1f;
        radial.Value = 1f;
        orbit.RadialAxis = radial;

        // Selalu melihat titik fokus yang sama dengan pusat orbit.
        CinemachineRotationComposer composer = GetOrAdd<CinemachineRotationComposer>(go);
        composer.TargetOffset = FocusOffset;
        composer.Damping = new Vector2(0.1f, 0.1f);

        // Tembok dan lantai: kamera ditarik ke depan penghalang. Masuk tanpa damping, keluar pelan.
        CinemachineDeoccluder deoccluder = GetOrAdd<CinemachineDeoccluder>(go);
        deoccluder.CollideAgainst = LayerMask.GetMask(GameLayers.NavBlock, GameLayers.Ground);
        deoccluder.IgnoreTag = string.Empty;
        deoccluder.TransparentLayers = 0;
        // Pengecekan tembok dimulai dari dada MC (Use Follow Target) dan sangat dekat ke badan.
        // SphereCast mengabaikan collider yang sudah tersentuh di titik awal, jadi titik awal harus selalu di dalam ruangan,
        // termasuk saat MC menempel tembok (radius CharacterController 0,5 > 0,05 + 0,2).
        deoccluder.MinimumDistanceFromTarget = 0.05f;
        deoccluder.AvoidObstacles = new CinemachineDeoccluder.ObstacleAvoidance
        {
            Enabled = true,
            DistanceLimit = 0f,
            MinimumOcclusionTime = 0f,
            CameraRadius = 0.2f,
            UseFollowTarget = new CinemachineDeoccluder.ObstacleAvoidance.FollowTargetSettings
            {
                Enabled = true,
                YOffset = FocusOffset.y
            },
            Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward,
            MaximumEffort = 4,
            SmoothingTime = 0f,
            Damping = 0.6f,
            DampingWhenOccluded = 0f
        };

        // Lensa kamera tidak boleh masuk ke dalam tembok, dan tidak boleh turun ke bawah lantai.
        CinemachineDecollider decollider = GetOrAdd<CinemachineDecollider>(go);
        decollider.CameraRadius = 0.25f;
        decollider.Decollision = new CinemachineDecollider.DecollisionSettings
        {
            Enabled = true,
            ObstacleLayers = LayerMask.GetMask(GameLayers.NavBlock),
            UseFollowTarget = new CinemachineDecollider.DecollisionSettings.FollowTargetSettings
            {
                Enabled = true,
                YOffset = FocusOffset.y
            },
            Damping = 0.3f,
            SmoothingTime = 0f
        };
        decollider.TerrainResolution = new CinemachineDecollider.TerrainSettings
        {
            Enabled = true,
            TerrainLayers = LayerMask.GetMask(GameLayers.Ground),
            MaximumRaycast = 10f,
            Damping = 0.3f
        };

        // Tembok rendah: kamera tidak boleh berada di balik tembok walau garis pandangnya lewat di atas tembok.
        CameraWallClamp wallClamp = GetOrAdd<CameraWallClamp>(go);
        SerializedObject clampData = new SerializedObject(wallClamp);
        clampData.FindProperty("wallLayers").intValue = LayerMask.GetMask(GameLayers.NavBlock);
        clampData.FindProperty("pivotHeight").floatValue = FocusOffset.y;
        clampData.ApplyModifiedProperties();

        ThirdPersonCameraRig rig = GetOrAdd<ThirdPersonCameraRig>(go);
        SerializedObject rigData = new SerializedObject(rig);
        rigData.FindProperty("target").objectReferenceValue = player.transform;
        rigData.ApplyModifiedProperties();

        // Hubungkan ke kamera utama dan pakai mode Third Person.
        SerializedObject followData = new SerializedObject(follow);
        followData.FindProperty("thirdPerson").objectReferenceValue = rig;
        followData.FindProperty("mode").enumValueIndex = (int)GameplayCameraMode.ThirdPerson;
        followData.ApplyModifiedProperties();

        foreach (Object changed in new Object[] { brain, cmCamera, orbit, composer, deoccluder, decollider })
            EditorUtility.SetDirty(changed);

        EditorSceneManager.MarkSceneDirty(go.scene);
        // Sengaja tidak dipilih: saat kamera Cinemachine dipilih, Game view menampilkan garis bantu komposisi.
        EditorGUIUtility.PingObject(go);

        Debug.Log("MathGate: kamera orang ketiga siap (" + go.name + "). Tekan Play, tahan klik kanan untuk memutar, F8 untuk ganti mode.", go);
    }

    [MenuItem("MathGate/Camera/Use Top Down Camera")]
    public static void UseTopDown()
    {
        SetMode(GameplayCameraMode.TopDown);
    }

    [MenuItem("MathGate/Camera/Use Third Person Camera")]
    public static void UseThirdPerson()
    {
        SetMode(GameplayCameraMode.ThirdPerson);
    }

    private static void SetMode(GameplayCameraMode mode)
    {
        TopDownCameraFollow follow = Object.FindAnyObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);

        if (follow == null)
        {
            Debug.LogWarning("MathGate: TopDownCameraFollow tidak ditemukan di scene.");
            return;
        }

        // Saat Play: langsung ganti mode (sama seperti F8), tidak disimpan ke scene.
        if (EditorApplication.isPlaying)
        {
            if (mode == GameplayCameraMode.ThirdPerson && !follow.HasThirdPersonRig)
            {
                Debug.LogWarning("MathGate: belum ada kamera orang ketiga. Hentikan Play lalu jalankan MathGate > Camera > Setup Third Person Camera.", follow);
                return;
            }

            follow.SetMode(mode);
            Debug.Log("MathGate: kamera sekarang " + follow.Mode + " (hanya selama Play ini).", follow);
            return;
        }

        SerializedObject data = new SerializedObject(follow);
        data.FindProperty("mode").enumValueIndex = (int)mode;
        data.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(follow.gameObject.scene);
        Debug.Log("MathGate: kamera awal = " + mode + ". Simpan scene supaya tersimpan.", follow);
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();

        if (component == null)
            component = Undo.AddComponent<T>(go);
        else
            Undo.RecordObject(component, "Setup Third Person Camera");

        return component;
    }
}
