using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds a test scene for the destructible Venus statue: Tools > Build Venus Statue Scene.
// Fly with WASD, click to capture the mouse, click again to shoot, R for a fresh statue.
// The statue itself comes from tools/statue_gen/build_venus.py (Blender), exported to VenusStatue.fbx.
public static class VenusSceneBuilder
{
    const string ScenePath = "Assets/Scenes/VenusStatue.unity";
    const string FbxPath = "Assets/Models/Statues/VenusStatue.fbx";
    const string MaterialFolder = "Assets/Materials";
    const float PlinthHeight = 0.80f;   // the plinth foot sits at y = -0.8 in the model

    [MenuItem("Tools/Build Venus Statue Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var marble = GetMaterial("VenusMarble", new Color(0.88f, 0.86f, 0.82f), 0.62f);
        var broken = GetMaterial("VenusMarbleBroken", new Color(0.95f, 0.94f, 0.91f), 0.2f);
        var floor = GetMaterial("VenusFloor", new Color(0.72f, 0.60f, 0.66f), 0.25f);
        var dust = DustMaterial();
        SetupImporter(marble, broken);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (prefab == null)
        {
            Debug.LogError($"Missing {FbxPath}. Run tools/statue_gen/build_venus.py first.");
            return;
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // warm late-afternoon light like the reference clip, pinkish bounce from the ground
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.6f;
        sun.color = new Color(1f, 0.86f, 0.72f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.55f, 0.66f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.45f, 0.5f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.28f, 0.3f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.GetComponent<Renderer>().sharedMaterial = floor;

        var pos = new Vector3(0f, PlinthHeight, 0f);
        var statue = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        statue.transform.position = pos;
        statue.AddComponent<DestructibleStatue>().dustMaterial = dust;

        // the model faces +Z: stand a few metres in front of it, looking back at it
        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        var c = cam.AddComponent<Camera>();
        c.nearClipPlane = 0.05f;
        cam.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(0.6f, 1.7f, 5.5f);
        cam.transform.LookAt(new Vector3(0f, 1.6f, 0f));
        cam.AddComponent<FlyCamera>();
        var gun = cam.AddComponent<StatueShooter>();
        gun.statuePrefab = prefab;
        gun.statuePosition = pos;
        gun.statueRotation = prefab.transform.rotation;
        gun.dustMaterial = dust;

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        Debug.Log($"Venus statue scene built and saved to {ScenePath}");
    }

    // Read/Write so the runtime convex MeshColliders work in builds; Blender materials mapped to ours.
    static void SetupImporter(Material marble, Material broken)
    {
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null) return;
        imp.isReadable = true;
        imp.importAnimation = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Marble"), marble);
        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "MarbleBroken"), broken);
        imp.SaveAndReimport();
    }

    // soft round puff, alpha blended, URP particle shader
    static Material DustMaterial()
    {
        string path = $"{MaterialFolder}/VenusDust.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");
        mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material GetMaterial(string name, Color color, float smoothness)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
