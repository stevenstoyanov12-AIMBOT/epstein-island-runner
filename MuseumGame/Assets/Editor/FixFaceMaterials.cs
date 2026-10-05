using UnityEditor;
using UnityEngine;

// Tools > Fix Face Materials: the Cented and Tjr head textures carry a "metallic" map with patches of up to 50-90% metal, so the skin
// renders as dark metal in those spots. Skin is not metal: set metallicFactor to 0 on both head materials.
public static class FixFaceMaterials
{
    [MenuItem("Tools/Fix Face Materials")]
    public static void Run()
    {
        foreach (var path in new[] { "Assets/Optimized/Mats/m200.mat", "Assets/Optimized/Mats/m201.mat" })   // m200 = Tjr (HeadA), m201 = Cented (HeadB)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { Debug.LogWarning("Fix Face Materials: not found " + path); continue; }
            if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            Debug.Log("Fix Face Materials: metallic off on " + path);
        }
        AssetDatabase.SaveAssets();
    }
}
