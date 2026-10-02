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
        Make("FX_Flash", 2f, BlendMode.One, 3100, new Color(1f, 0.8f, 0.4f, 1f));                        // additive: muzzle flash
        Make("FX_WineAlpha", 0f, BlendMode.OneMinusSrcAlpha, 3000, new Color(0.42f, 0.015f, 0.05f, 1f)); // alpha blended: wine drops and mist
        AssetDatabase.SaveAssets();
    }

    static void Make(string name, float blend, BlendMode dst, int queue, Color color)
    {
        string path = "Assets/ResData/" + name + ".mat";
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", blend); m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)dst); m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = queue;
        m.SetColor("_BaseColor", color);
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        Debug.Log("Created " + path);
    }
}
