using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoboStreetSoccer;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class SoccerProjectSetup
{
    const string ScenePath = "Assets/Soccer/Scenes/RoboStreetSoccer.unity";
    const string ModelPath = "Assets/Art/Robot/RoboPlayerSoccer.fbx";
    const string TexturePath = "Assets/Art/Robot/robot-basecolor.png";
    const string ControllerPath = "Assets/Soccer/Animation/RoboSoccer.controller";

    [MenuItem("Robo Street Soccer/Build final 2v2 scene")]
    public static void BuildPracticeScene()
    {
        Directory.CreateDirectory("Assets/Soccer/Scenes");
        Directory.CreateDirectory("Assets/Soccer/Materials");
        Directory.CreateDirectory("Assets/Soccer/Animation");
        AssetDatabase.Refresh();
        AddTag("Board");
        AddTag("GoalFrame");
        ConfigureProject();
        ConfigureModelImport();
        AnimatorController controller = BuildAnimatorController();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Material pitch = MaterialAsset("Pitch", new Color(.055f, .37f, .31f), .15f, 0f);
        Material boundary = MaterialAsset("Boundary", new Color(.075f, .15f, .17f), .4f, .15f);
        Material orange = RobotMaterial();
        Material mint = MaterialAsset("MintRobot", new Color(.20f, .92f, .68f), .46f, .12f);
        Material white = MaterialAsset("Lines", new Color(.94f, .93f, .82f), .08f, 0f);
        Material frame = MaterialAsset("GoalFrame", new Color(.96f, .57f, .20f), .42f, .12f);
        Material ballMaterial = MaterialAsset("Ball", new Color(.98f, .82f, .21f), .32f, .02f);
        Material ringMaterial = MaterialAsset("SelectedRing", new Color(.25f, .95f, .76f), .18f, 0f, true);
        PhysicsMaterial ballPhysics = PhysicsMaterialAsset("BallPhysics", .18f, .48f, PhysicsMaterialCombine.Multiply, PhysicsMaterialCombine.Average);
        PhysicsMaterial boardPhysics = PhysicsMaterialAsset("BoardPhysics", .12f, .62f, PhysicsMaterialCombine.Multiply, PhysicsMaterialCombine.Multiply);
        PhysicsMaterial robotPhysics = PhysicsMaterialAsset("RobotPhysics", .25f, .02f, PhysicsMaterialCombine.Minimum, PhysicsMaterialCombine.Minimum);

        CreateEnvironment(pitch, boundary, white, frame, boardPhysics);
        SoccerBall ball = CreateBall(ballMaterial, ballPhysics);
        SoccerRobot playerA = CreateRobot("Orange One", new Vector3(-1.35f, 0, -5.8f), orange, robotPhysics, controller, SoccerTeam.Orange, 0);
        SoccerRobot playerB = CreateRobot("Orange Two", new Vector3(2.7f, 0, -1.4f), orange, robotPhysics, controller, SoccerTeam.Orange, 1);
        SoccerRobot opponentA = CreateRobot("Mint One", new Vector3(1.35f, 0, 5.8f), mint, robotPhysics, controller, SoccerTeam.Mint, 2);
        SoccerRobot opponentB = CreateRobot("Mint Two", new Vector3(-2.7f, 0, 2.2f), mint, robotPhysics, controller, SoccerTeam.Mint, 3);

        GameObject systems = new GameObject("Soccer Systems");
        SoccerGame game = systems.AddComponent<SoccerGame>();
        systems.AddComponent<SoccerHud>();
        systems.AddComponent<SoccerAcceptanceRunner>();
        game.Initialize(ball, playerA, playerB, opponentA, opponentB);

        CreateCameraAndLight();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("SOCCER_SETUP_OK scene=" + ScenePath);
    }

    public static void BuildWindows()
    {
        BuildPracticeScene();
        string root = Directory.GetParent(Application.dataPath).Parent.FullName;
        string buildDirectory = Path.Combine(root, "Builds", "RoboStreetSoccer");
        Directory.CreateDirectory(buildDirectory);
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(buildDirectory, "RoboStreetSoccer.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        string burstDebugDirectory = Path.Combine(buildDirectory, "Robo Street Soccer_BurstDebugInformation_DoNotShip");
        if (Directory.Exists(burstDebugDirectory)) Directory.Delete(burstDebugDirectory, true);
        string receipt = JsonUtility.ToJson(new BuildReceipt
        {
            result = summary.result.ToString(),
            errors = summary.totalErrors,
            warnings = summary.totalWarnings,
            bytes = summary.totalSize,
            seconds = (float)summary.totalTime.TotalSeconds,
            output = "Builds/RoboStreetSoccer/RoboStreetSoccer.exe",
            unity = Application.unityVersion,
            fixedDeltaTime = Time.fixedDeltaTime
        }, true);
        string evidence = Path.Combine(root, "Evidence", "match-windows-build.json");
        Directory.CreateDirectory(Path.GetDirectoryName(evidence));
        File.WriteAllText(evidence, receipt);
        Debug.Log("SOCCER_BUILD " + receipt);
        if (summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + summary.result);
    }

    public static void DiagnoseContactPoses()
    {
        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        Transform foot = FindBone(visual.transform, "Foot.R");
        Dictionary<string, float> contacts = new Dictionary<string, float>
        {
            { "Dribble", 11f / 30f }, { "Receive", 11f / 30f },
            { "Pass", 16f / 30f }, { "Shoot", 19f / 30f }
        };
        foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>())
        {
            string shortName = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
            if (!contacts.TryGetValue(shortName, out float time)) continue;
            clip.SampleAnimation(visual, time);
            Debug.Log($"SOCCER_POSE {shortName} footLocal={visual.transform.InverseTransformPoint(foot.position)}");
        }
        UnityEngine.Object.DestroyImmediate(visual);
    }

    static void ConfigureProject()
    {
        PlayerSettings.companyName = "Robo Street Lab";
        PlayerSettings.productName = "Robo Street Soccer";
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        Time.fixedDeltaTime = .01f;
        Time.maximumDeltaTime = .1f;
        RenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }
        Physics.defaultSolverIterations = 12;
        Physics.defaultSolverVelocityIterations = 4;
    }

    static void ConfigureModelImport()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (!importer) throw new FileNotFoundException("Soccer robot FBX not imported", ModelPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.isReadable = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            int separator = clip.name.LastIndexOf('|');
            if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
            bool looping = clip.name == "Idle" || clip.name == "Run" || clip.name == "Turn";
            clip.loopTime = looping;
            clip.loopPose = looping;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.lockRootRotation = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimatorController BuildAnimatorController()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (AnimationClip clip in clips)
        {
            AnimatorState state = machine.AddState(clip.name);
            state.motion = clip;
            if (clip.name == "Idle") machine.defaultState = state;
        }
        string[] required = { "Idle", "Run", "Turn", "Dribble", "Receive", "Pass", "Shoot", "Tackle", "ContactLean" };
        string[] found = clips.Select(clip => clip.name).ToArray();
        string[] missing = required.Where(name => !found.Contains(name)).ToArray();
        if (missing.Length > 0) throw new Exception("Imported robot is missing clips: " + string.Join(", ", missing));
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void CreateEnvironment(Material pitch, Material boundary, Material lines, Material frame, PhysicsMaterial boardPhysics)
    {
        GameObject floor = Cube("Pitch", new Vector3(0, -.10f, 0), new Vector3(18f, .2f, 26f), pitch, true);
        floor.GetComponent<BoxCollider>().material = PhysicsMaterialAsset("PitchPhysics", .44f, .08f, PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Minimum);
        Cube("Centre line", new Vector3(0, .012f, 0), new Vector3(18f, .018f, .055f), lines, false);
        Cube("North box", new Vector3(0, .012f, 10.8f), new Vector3(7f, .018f, .055f), lines, false);
        Cube("South box", new Vector3(0, .012f, -10.8f), new Vector3(7f, .018f, .055f), lines, false);
        CreateCentreCircle();

        Board("West Board", new Vector3(-9.15f, .36f, 0), new Vector3(.3f, .72f, 26.4f), boundary, boardPhysics);
        Board("East Board", new Vector3(9.15f, .36f, 0), new Vector3(.3f, .72f, 26.4f), boundary, boardPhysics);
        float segmentWidth = (18f - SoccerGame.GoalHalfWidth * 2f) * .5f;
        float segmentCentre = SoccerGame.GoalHalfWidth + segmentWidth * .5f;
        foreach (float z in new[] { -13.15f, 13.15f })
        {
            Board((z > 0 ? "North" : "South") + " Board L", new Vector3(-segmentCentre, .36f, z), new Vector3(segmentWidth, .72f, .3f), boundary, boardPhysics);
            Board((z > 0 ? "North" : "South") + " Board R", new Vector3(segmentCentre, .36f, z), new Vector3(segmentWidth, .72f, .3f), boundary, boardPhysics);
            CreateGoal(z, frame, boundary, boardPhysics);
        }
        Cube("West Stand", new Vector3(-11.2f, .35f, 0), new Vector3(2.8f, .7f, 25f), MaterialAsset("Stand", new Color(.17f, .27f, .29f), .1f, 0f), false);
        Cube("East Stand", new Vector3(11.2f, .35f, 0), new Vector3(2.8f, .7f, 25f), MaterialAsset("Stand", new Color(.17f, .27f, .29f), .1f, 0f), false);
    }

    static void CreateGoal(float z, Material frame, Material net, PhysicsMaterial physics)
    {
        string side = z > 0 ? "North" : "South";
        for (int sign = -1; sign <= 1; sign += 2)
        {
            GameObject post = Cylinder(side + " Goal Post " + sign, new Vector3(sign * SoccerGame.GoalHalfWidth, SoccerGame.CrossbarHeight * .5f, z), new Vector3(.10f, SoccerGame.CrossbarHeight * .5f, .10f), frame);
            post.tag = "GoalFrame";
            post.GetComponent<Collider>().material = physics;
        }
        GameObject bar = Cylinder(side + " Crossbar", new Vector3(0, SoccerGame.CrossbarHeight, z), new Vector3(.10f, SoccerGame.GoalHalfWidth, .10f), frame);
        bar.transform.rotation = Quaternion.Euler(0, 0, 90);
        bar.tag = "GoalFrame";
        bar.GetComponent<Collider>().material = physics;
        float backZ = z + Mathf.Sign(z) * 1.35f;
        Cube(side + " Goal Back", new Vector3(0, 1.12f, backZ), new Vector3(4.45f, 2.25f, .12f), net, true);
        Cube(side + " Goal Side L", new Vector3(-2.16f, 1.12f, z + Mathf.Sign(z) * .68f), new Vector3(.12f, 2.25f, 1.35f), net, true);
        Cube(side + " Goal Side R", new Vector3(2.16f, 1.12f, z + Mathf.Sign(z) * .68f), new Vector3(.12f, 2.25f, 1.35f), net, true);
    }

    static SoccerBall CreateBall(Material material, PhysicsMaterial physics)
    {
        GameObject ballObject = new GameObject();
        ballObject.name = "Match Ball";
        // Oversize the rendered ball for readability from the full-pitch camera,
        // while preserving the validated 22 cm gameplay contact footprint. The
        // visual child is raised so its larger shell sits on the pitch instead
        // of appearing half buried; the rigid-body centre is unchanged.
        ballObject.transform.localScale = Vector3.one * .66f;
        ballObject.transform.position = new Vector3(-1.2f, .125f, -5.67f);
        GameObject visibleBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visibleBall.name = "Readable Visual Shell";
        UnityEngine.Object.DestroyImmediate(visibleBall.GetComponent<Collider>());
        visibleBall.transform.SetParent(ballObject.transform, false);
        visibleBall.transform.localPosition = Vector3.up * (1f / 3f);
        visibleBall.GetComponent<Renderer>().sharedMaterial = material;
        SphereCollider collider = ballObject.AddComponent<SphereCollider>();
        collider.radius = 1f / 6f;
        collider.material = physics;
        Rigidbody body = ballObject.AddComponent<Rigidbody>();
        body.mass = .43f;
        return ballObject.AddComponent<SoccerBall>();
    }

    static SoccerRobot CreateRobot(string name, Vector3 position, Material material, PhysicsMaterial physics, AnimatorController controller,
        SoccerTeam team, int rosterIndex)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;
        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.radius = .35f;
        capsule.height = 1.62f;
        capsule.center = new Vector3(0, .81f, -.06f);
        capsule.material = physics;
        root.AddComponent<Rigidbody>();
        SoccerRobot robot = root.AddComponent<SoccerRobot>();
        robot.ConfigureTeam(team, true, rosterIndex);
        root.AddComponent<SoccerAgentAI>();

        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        visual.name = "Robot Visual";
        visual.transform.SetParent(root.transform, false);
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
        Animator animator = visual.GetComponent<Animator>();
        if (!animator) animator = visual.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Fixed;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        SoccerFootPlant footPlant = visual.AddComponent<SoccerFootPlant>();
        if (!footPlant.Configure(animator, root.transform)) throw new Exception("Could not configure generic-rig foot planting on " + name);
        Transform rightFoot = FindBone(visual.transform, "Foot.R");
        if (!rightFoot) throw new Exception("Could not find Foot.R in imported soccer robot");

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Controlled Player Ring";
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0, .025f, 0);
        ring.transform.localScale = new Vector3(1.15f, .012f, 1.15f);
        Material selected = MaterialAsset("SelectedRing", new Color(.20f, 1f, .88f), 0f, 0f, true);
        Shader selectedShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (selectedShader) selected.shader = selectedShader;
        if (selected.HasProperty("_BaseColor")) selected.SetColor("_BaseColor", new Color(.20f, 1f, .88f));
        if (selected.HasProperty("_Color")) selected.SetColor("_Color", new Color(.20f, 1f, .88f));
        EditorUtility.SetDirty(selected);
        ring.GetComponent<Renderer>().sharedMaterial = selected;
        UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
        robot.ConfigureVisual(animator, rightFoot, ring.transform);
        return robot;
    }

    static Transform FindBone(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name || child.name.EndsWith(name, StringComparison.Ordinal)) return child;
        return null;
    }

    static void CreateCameraAndLight()
    {
        GameObject cameraObject = new GameObject("Match Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<SoccerCameraController>();
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0, 22.5f, -25.5f);
        cameraObject.transform.LookAt(new Vector3(0, .5f, -4f));
        camera.fieldOfView = 62f;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 100f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.53f, .72f, .74f);
        GameObject lightObject = new GameObject("Golden Hour Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.color = new Color(1f, .88f, .72f);
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.46f, .62f, .64f);
        RenderSettings.ambientEquatorColor = new Color(.25f, .34f, .34f);
        RenderSettings.ambientGroundColor = new Color(.10f, .13f, .12f);
    }

    static void CreateCentreCircle()
    {
        GameObject circle = new GameObject("Centre Circle");
        LineRenderer line = circle.AddComponent<LineRenderer>();
        Material circleMaterial = MaterialAsset("CircleLines", Color.white, 0f, 0f);
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (unlit) circleMaterial.shader = unlit;
        if (circleMaterial.HasProperty("_BaseColor")) circleMaterial.SetColor("_BaseColor", Color.white);
        if (circleMaterial.HasProperty("_Color")) circleMaterial.SetColor("_Color", Color.white);
        EditorUtility.SetDirty(circleMaterial);
        line.sharedMaterial = circleMaterial;
        line.useWorldSpace = true;
        line.loop = true;
        line.widthMultiplier = .055f;
        line.startColor = Color.white;
        line.endColor = Color.white;
        line.positionCount = 64;
        for (int i = 0; i < 64; i++)
        {
            float angle = i * Mathf.PI * 2f / 64f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * 2.1f, .025f, Mathf.Sin(angle) * 2.1f));
        }
    }

    static GameObject Cube(string name, Vector3 position, Vector3 scale, Material material, bool keepCollider)
    {
        GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name;
        item.transform.position = position;
        item.transform.localScale = scale;
        item.GetComponent<Renderer>().sharedMaterial = material;
        if (!keepCollider) UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        return item;
    }

    static void Board(string name, Vector3 position, Vector3 scale, Material material, PhysicsMaterial physics)
    {
        GameObject board = Cube(name, position, scale, material, true);
        board.tag = "Board";
        board.GetComponent<Collider>().material = physics;
    }

    static GameObject Cylinder(string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        item.name = name;
        item.transform.position = position;
        item.transform.localScale = scale;
        item.GetComponent<Renderer>().sharedMaterial = material;
        return item;
    }

    static Material RobotMaterial()
    {
        Material material = MaterialAsset("OrangeRobot", Color.white, .46f, .12f);
        Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(TexturePath);
        if (texture)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    static Material MaterialAsset(string name, Color color, float smoothness, float metallic, bool emission = false)
    {
        string path = $"Assets/Soccer/Materials/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (emission && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.1f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static PhysicsMaterial PhysicsMaterialAsset(string name, float friction, float bounce, PhysicsMaterialCombine frictionCombine, PhysicsMaterialCombine bounceCombine)
    {
        string path = $"Assets/Soccer/Materials/{name}.physicMaterial";
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (!material)
        {
            material = new PhysicsMaterial(name);
            AssetDatabase.CreateAsset(material, path);
        }
        material.dynamicFriction = friction;
        material.staticFriction = friction;
        material.bounciness = bounce;
        material.frictionCombine = frictionCombine;
        material.bounceCombine = bounceCombine;
        EditorUtility.SetDirty(material);
        return material;
    }

    static void AddTag(string tag)
    {
        UnityEngine.Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (!tagManager) return;
        SerializedObject serialized = new SerializedObject(tagManager);
        SerializedProperty tags = serialized.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        serialized.ApplyModifiedProperties();
    }

    [Serializable]
    sealed class BuildReceipt
    {
        public string result;
        public int errors;
        public int warnings;
        public ulong bytes;
        public float seconds;
        public string output;
        public string unity;
        public float fixedDeltaTime;
    }
}
