using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Places the destructible Venus statue (used by Tools > Build Statue Scene, the Statue Garden).
// Fly with WASD, click to capture the mouse, click again to shoot, R for a fresh statue.
// The statue itself comes from tools/statue_gen/build_venus.py (Blender), exported to VenusStatue.fbx.
public static class VenusSceneBuilder
{
    const string FbxPath = "Assets/Models/Statues/VenusStatue.fbx";
    const string MaterialFolder = "Assets/Materials";
    const float PlinthHeight = 0.80f;   // the plinth foot sits at y = -0.8 in the model

    // Puts the statue with its plinth foot at groundPos and makes the camera the test gun (click shoots, R respawns).
    // The model faces +Z; turn it 180 degrees to face a camera standing on the -Z side.
    public static GameObject PlaceStatue(Vector3 groundPos, Quaternion turn, GameObject cam)
    {
        var marble = GetMaterial("VenusMarble", Color.white, 0.55f, 0.10f);                         // white Carrara, faint glow so it reads white at night
        var broken = GetMaterial("VenusMarbleBroken", Color.white, 0.12f, 0.14f);                  // fresh break: brighter, chalky
        var dust = ParticleMaterial("StatueDust");
        var grit = ParticleMaterial("StatueGrit");
        SetupImporter(marble, broken);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (prefab == null)
        {
            Debug.LogError($"Missing {FbxPath}. Run tools/statue_gen/build_venus.py first.");
            return null;
        }
        var pos = groundPos + Vector3.up * PlinthHeight;
        var rot = turn * prefab.transform.rotation;
        var statue = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        statue.transform.SetPositionAndRotation(pos, rot);
        // neutral white key light on her, so the warm lamps don't turn the marble beige
        var key = new GameObject("VenusKeyLight").AddComponent<Light>();
        key.type = LightType.Spot;
        key.color = new Color(0.95f, 0.97f, 1f);
        key.intensity = 6f;
        key.range = 9f;
        key.spotAngle = 40f;
        key.shadows = LightShadows.Soft;
        var front = turn * Vector3.forward;
        key.transform.position = pos + front * 3.2f + Vector3.up * 2.6f;
        key.transform.LookAt(pos + Vector3.up * 0.9f);
        key.transform.SetParent(statue.transform.parent, true);
        var ds = statue.AddComponent<DestructibleStatue>();
        ds.dustMaterial = dust;
        ds.gritMaterial = grit;

        var gun = cam.GetComponent<StatueShooter>();
        if (gun == null) gun = cam.AddComponent<StatueShooter>();
        gun.statuePrefab = prefab;
        gun.statuePosition = pos;
        gun.statueRotation = rot;
        gun.dustMaterial = dust;
        gun.gritMaterial = grit;
        var bullet = AssetDatabase.LoadAllAssetsAtPath("Assets/Models/Weapons/BulletCasing.obj");
        foreach (var o in bullet) if (o is Mesh m) { gun.bulletMesh = m; break; }
        return statue;
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

    // Same setup as the game's FX_Dust / FX_Grit: URP Particles/Unlit, alpha blended, SoftDot texture.
    static Material ParticleMaterial(string name)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets", "Materials");
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/SoftDot.png")
                  ?? AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material GetMaterial(string name, Color color, float smoothness, float glow = 0f)
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
        if (glow > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(glow, glow, glow * 1.02f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
