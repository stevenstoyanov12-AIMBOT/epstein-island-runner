using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Place Venus Statues: scatters destructible Venus statues at random open spots in the open scene
// (flat ground, nothing in the way, apart from each other) and saves them into the scene, so every player sees
// them in the same places. Run it again to reshuffle; Tools > Remove Venus Statues takes them out.
public static class PlaceVenusStatues
{
    const string FbxPath = "Assets/Models/Statues/VenusStatue.fbx";
    const string Root = "VenusStatues";
    const float PlinthHeight = 0.80f;   // the model's plinth foot is 0.8 m below its origin
    const int Count = 4;
    const float MinApart = 10f;

    [MenuItem("Tools/Place Venus Statues")]
    static void Place()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (prefab == null) { Debug.LogError($"Missing {FbxPath}"); return; }
        Setup();
        Remove();

        // search area: the scene's renderers, minus the outer 10%
        var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        if (all.Length == 0) { Debug.LogError("Scene is empty"); return; }
        var area = all[0].bounds;
        foreach (var r in all) area.Encapsulate(r.bounds);
        area.Expand(new Vector3(-area.size.x * 0.1f, 0f, -area.size.z * 0.1f));

        Physics.SyncTransforms();
        var root = new GameObject(Root).transform;
        var placed = new List<Vector3>();
        var rng = new System.Random(System.Environment.TickCount);
        for (int tries = 0; tries < 4000 && placed.Count < Count; tries++)
        {
            var x = Mathf.Lerp(area.min.x, area.max.x, (float)rng.NextDouble());
            var z = Mathf.Lerp(area.min.z, area.max.z, (float)rng.NextDouble());
            if (!Physics.Raycast(new Vector3(x, area.max.y + 5f, z), Vector3.down, out var hit, area.size.y + 20f,
                                 ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Vector3.Dot(hit.normal, Vector3.up) < 0.97f) continue;                 // flat ground only
            if (hit.collider.bounds.size.x < 3f || hit.collider.bounds.size.z < 3f) continue;   // a floor, not a prop top
            bool near = false;
            foreach (var p in placed) if (Vector3.Distance(p, hit.point) < MinApart) near = true;
            if (near) continue;
            // the statue's whole footprint must be on level ground and the space above it empty
            bool level = true;
            foreach (var o in new[] { new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, -0.5f) })
                if (!Physics.Raycast(hit.point + o + Vector3.up * 0.5f, Vector3.down, out var h2, 0.7f, ~0, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(h2.point.y - hit.point.y) > 0.05f) level = false;
            if (!level) continue;
            if (Physics.CheckBox(hit.point + Vector3.up * 1.6f, new Vector3(0.7f, 1.45f, 0.7f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;

            var statue = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            statue.name = $"VenusStatue_{placed.Count + 1}";
            statue.transform.SetPositionAndRotation(hit.point + Vector3.up * PlinthHeight,
                                                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * prefab.transform.rotation);
            statue.AddComponent<VenusStatueBreak>();
            placed.Add(hit.point);
        }
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"Placed {placed.Count} Venus statues in {root.gameObject.scene.name} (save the scene to keep them)");
        if (placed.Count < Count) Debug.LogWarning("Not enough open flat ground found for all statues");
    }

    [MenuItem("Tools/Remove Venus Statues")]
    static void Remove()
    {
        var old = GameObject.Find(Root);
        if (old != null) { Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
    }

    // White marble materials and Read/Write (convex colliders in web builds) on the model
    static void Setup()
    {
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null) return;
        imp.isReadable = true;
        imp.importAnimation = false;
        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Marble"), Mat("VenusMarble", Color.white, 0.55f, 0.10f));
        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "MarbleBroken"), Mat("VenusMarbleBroken", Color.white, 0.12f, 0.14f));
        imp.SaveAndReimport();
    }

    // white marble with a faint glow, so it reads white under the warm lamps at night
    static Material Mat(string name, Color c, float smooth, float glow)
    {
        string path = $"Assets/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(glow, glow, glow * 1.02f));
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        return m;
    }
}
