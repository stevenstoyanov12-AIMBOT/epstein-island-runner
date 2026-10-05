using UnityEngine;
using UnityEngine.InputSystem;

// Live debugging in the web build (no rebuild per experiment). Keys: Home = next quality level, End = LOD bias 1/2/4/8,
// PageUp = occlusion culling on/off, PageDown = far clip 500/5000, Insert = shader/renderer report, Delete = hide this text.
public class WebDebug : MonoBehaviour
{
    string info = ""; bool show = true; int lodStep;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (FindAnyObjectByType<WebDebug>() != null) return; var g = new GameObject("WebDebug"); DontDestroyOnLoad(g); g.AddComponent<WebDebug>(); }

    void Update()
    {
        var kb = Keyboard.current; if (kb == null) return; var cam = Camera.main;
        if (kb.homeKey.wasPressedThisFrame) { QualitySettings.SetQualityLevel((QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length, true); Say(); }
        if (kb.endKey.wasPressedThisFrame) { float[] v = { 1f, 2f, 4f, 8f }; lodStep = (lodStep + 1) % v.Length; QualitySettings.lodBias = v[lodStep]; Say(); }
        if (kb.pageUpKey.wasPressedThisFrame && cam != null) { cam.useOcclusionCulling = !cam.useOcclusionCulling; Say(); }
        if (kb.pageDownKey.wasPressedThisFrame && cam != null) { cam.farClipPlane = cam.farClipPlane < 1000f ? 5000f : 500f; Say(); }
        if (kb.insertKey.wasPressedThisFrame) Report();
        if (kb.deleteKey.wasPressedThisFrame) show = !show;
    }

    void Say()
    {
        var cam = Camera.main;
        info = "API " + SystemInfo.graphicsDeviceType + " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " | lodBias " + QualitySettings.lodBias
            + (cam != null ? " | occlusion " + cam.useOcclusionCulling + " | far " + cam.farClipPlane : "");
        Debug.Log("WebDebug: " + info);
    }

    void Report()
    {
        Say();
        string[] shaders = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "Custom/PortalView", "Custom/FountainWater" };
        string s = ""; foreach (var n in shaders) s += n + "=" + (Shader.Find(n) != null) + "; ";
        var mats = Res.Load<Material>("FX_Flash"); s += "FX_Flash=" + (mats != null);
        int total = 0, vis = 0; foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) { total++; if (r.isVisible) vis++; }
        Debug.Log("WebDebug report: " + s + " | renderers " + total + ", visible " + vis);
        info += "\n" + s + "\nrenderers " + total + ", visible " + vis;
    }

    void OnGUI()
    {
        if (!show || string.IsNullOrEmpty(info)) return;
        GUI.color = Color.black; GUI.Label(new Rect(11, Screen.height - 79, Screen.width, 80), info); GUI.color = Color.yellow; GUI.Label(new Rect(10, Screen.height - 80, Screen.width, 80), info);
    }
}
