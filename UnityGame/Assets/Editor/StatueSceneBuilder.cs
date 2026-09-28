using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds a "Statue Garden" scene out of primitives: Tools > Build Statue Scene.
public static class StatueSceneBuilder
{
    const string ScenePath = "Assets/Scenes/StatueGarden.unity";
    const string MaterialFolder = "Assets/Materials";

    [MenuItem("Tools/Build Statue Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var stone = GetMaterial("Stone", new Color(0.78f, 0.76f, 0.72f), 0.2f, 0f);
        var marble = GetMaterial("Marble", new Color(0.93f, 0.92f, 0.89f), 0.55f, 0f);
        var gold = GetMaterial("Gold", new Color(1f, 0.77f, 0.3f), 0.8f, 1f);
        var grass = GetMaterial("Grass", new Color(0.32f, 0.5f, 0.25f), 0.1f, 0f);
        var path = GetMaterial("Path", new Color(0.62f, 0.56f, 0.47f), 0.15f, 0f);

        // Lighting
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.3f;
        sun.shadows = LightShadows.Soft;
        sun.color = new Color(1f, 0.95f, 0.86f);
        sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.8f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.18f);

        // Ground and plaza
        var env = new GameObject("Environment").transform;
        Part(PrimitiveType.Plane, "Ground", env, Vector3.zero, new Vector3(6f, 1f, 6f), grass);
        Part(PrimitiveType.Cylinder, "Plaza", env, new Vector3(0f, 0.05f, 0f), new Vector3(22f, 0.05f, 22f), path);

        // Centerpiece: large golden statue
        var statues = new GameObject("Statues").transform;
        var hero = Statue("Statue_Center", statues, Vector3.zero, 0f, 1.6f, gold, marble, Pose.ArmsRaised);

        // Ring of statues facing the centre
        var poses = new[] { Pose.Standing, Pose.Saluting, Pose.ArmsRaised, Pose.Thinker, Pose.Standing, Pose.Saluting };
        const int count = 6;
        const float radius = 8f;
        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count;
            var pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
            Statue($"Statue_{i + 1}", statues, pos, angle + 180f, 1f, marble, stone, poses[i]);
        }

        // Camera looking at the plaza
        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        cam.AddComponent<Camera>().fieldOfView = 55f;
        cam.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(0f, 6f, -17f);
        cam.transform.LookAt(hero.transform.position + Vector3.up * 3f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Debug.Log($"Statue scene built and saved to {ScenePath}");
    }

    enum Pose { Standing, ArmsRaised, Saluting, Thinker }

    // A humanoid statue on a pedestal. Height of the figure is ~2 units at scale 1.
    static GameObject Statue(string name, Transform parent, Vector3 position, float yaw, float scale,
        Material body, Material pedestal, Pose pose)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.localPosition = position;
        root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        root.localScale = Vector3.one * scale;

        // Pedestal
        Part(PrimitiveType.Cube, "Pedestal_Base", root, new Vector3(0f, 0.15f, 0f), new Vector3(1.6f, 0.3f, 1.6f), pedestal);
        Part(PrimitiveType.Cube, "Pedestal", root, new Vector3(0f, 0.8f, 0f), new Vector3(1.2f, 1f, 1.2f), pedestal);
        Part(PrimitiveType.Cube, "Pedestal_Top", root, new Vector3(0f, 1.375f, 0f), new Vector3(1.4f, 0.15f, 1.4f), pedestal);

        var fig = new GameObject("Figure").transform;
        fig.SetParent(root, false);
        fig.localPosition = new Vector3(0f, 1.45f, 0f);

        // Legs, torso, head
        Part(PrimitiveType.Capsule, "Leg_L", fig, new Vector3(-0.15f, 0.45f, 0f), new Vector3(0.22f, 0.45f, 0.22f), body);
        Part(PrimitiveType.Capsule, "Leg_R", fig, new Vector3(0.15f, 0.45f, 0f), new Vector3(0.22f, 0.45f, 0.22f), body);
        Part(PrimitiveType.Capsule, "Torso", fig, new Vector3(0f, 1.25f, 0f), new Vector3(0.55f, 0.45f, 0.35f), body);
        Part(PrimitiveType.Sphere, "Head", fig, new Vector3(0f, 1.85f, 0f), Vector3.one * 0.32f, body);

        // Arms: pivot at the shoulder so rotations look natural
        float left, right, leftFwd = 0f, rightFwd = 0f;
        switch (pose)
        {
            case Pose.ArmsRaised: left = -150f; right = 150f; break;
            case Pose.Saluting: left = -10f; right = 10f; rightFwd = -140f; break;
            case Pose.Thinker: left = -10f; right = 20f; rightFwd = -120f; break;
            default: left = -12f; right = 12f; break;
        }
        Arm("Arm_L", fig, new Vector3(-0.36f, 1.5f, 0f), left, leftFwd, body);
        Arm("Arm_R", fig, new Vector3(0.36f, 1.5f, 0f), right, rightFwd, body);

        return root.gameObject;
    }

    static void Arm(string name, Transform parent, Vector3 shoulder, float side, float forward, Material mat)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = shoulder;
        pivot.localRotation = Quaternion.Euler(forward, 0f, side);
        Part(PrimitiveType.Capsule, "Mesh", pivot, new Vector3(0f, -0.35f, 0f), new Vector3(0.16f, 0.35f, 0.16f), mat);
    }

    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material GetMaterial(string name, Color color, float smoothness, float metallic)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        string assetPath = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, assetPath);
        }
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
