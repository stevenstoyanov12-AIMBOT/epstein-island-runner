using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Tools > Create Flash Material: makes Assets/ResData/FX_Flash.mat (additive, transparent URP Particles/Unlit) so the muzzle flash
// does not depend on runtime keywords, which the web build strips (the flash then draws as a solid square).
public static class FlashMaterialBuilder
{
    [MenuItem("Tools/Create Flash Material")]
    public static void Create()
    {
        const string path = "Assets/ResData/FX_Flash.mat";
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "FX_Flash" };
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f); m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.One); m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3100;
        m.SetColor("_BaseColor", new Color(1f, 0.8f, 0.4f, 1f));
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
        Debug.Log("Created " + path);
    }
}
