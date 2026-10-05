using UnityEditor;
using UnityEngine;

// Tools > Make Break Meshes Readable: turns Read/Write on for every shard/crater mesh and the pillar model.
// Those meshes get MeshColliders at runtime (when something breaks); a web build can only cook those colliders from readable meshes.
public static class MakeMeshesReadable
{
    [MenuItem("Tools/Make Break Meshes Readable")]
    public static void Run()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Museum/Shards", "Assets/Museum/Shards3" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".asset")) continue;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (mesh == null || mesh.isReadable) continue;
            var so = new SerializedObject(mesh); var p = so.FindProperty("m_IsReadable"); if (p == null) continue;
            p.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(mesh); n++;
        }
        foreach (var fbx in new[] { "Assets/Museum/DetailedPillar.fbx", "Assets/Museum/HallFractured.fbx", "Assets/Museum/Barrel_Shattered.fbx", "Assets/Museum/GlassShards.fbx" })
        {
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter; if (imp == null || imp.isReadable) continue;
            imp.isReadable = true; imp.SaveAndReimport(); n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Made " + n + " mesh assets/models readable.");
    }
}
