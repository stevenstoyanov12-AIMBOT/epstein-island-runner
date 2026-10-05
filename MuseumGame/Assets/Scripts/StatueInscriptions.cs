using UnityEngine;

// Puts "ANSEM THE BLACK GUARD" on the front of every statue pedestal.
public static class StatueInscriptions
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        foreach (var sg in Object.FindObjectsByType<StatueGaze>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var ped = sg.transform.Find("Pedestal"); if (ped == null) continue;
            var old = sg.transform.Find("Inscription"); if (old) Object.Destroy(old.gameObject);
            var g = new GameObject("AnsemPlaque"); g.transform.SetParent(sg.transform, false);
            g.transform.localPosition = new Vector3(0f, 0.9f, 1.004f); g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = g.AddComponent<TextMesh>(); tm.text = "ANSEM THE\nBLACK GUARD"; tm.fontSize = 120; tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.fontStyle = FontStyle.Normal; tm.color = new Color(0.02f, 0.02f, 0.02f);
            tm.font = Font.CreateDynamicFontFromOSFont(new[] { "Georgia Bold Italic", "Times New Roman Bold Italic", "Georgia" }, 120);
            var mr = g.GetComponent<MeshRenderer>(); var fm = tm.font.material;   // depth-tested, cut-out unlit material so the lettering sits IN the stone and hides behind walls
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Inscription" };
            m.SetTexture("_BaseMap", fm.mainTexture); m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 1f));
            m.SetFloat("_Cull", 0f); m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450;
            mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var b = mr.localBounds; float k = Mathf.Min(2.2f / Mathf.Max(0.01f, b.size.x), 1.1f / Mathf.Max(0.01f, b.size.y)); g.transform.localScale = Vector3.one * k;
        }
    }
}
