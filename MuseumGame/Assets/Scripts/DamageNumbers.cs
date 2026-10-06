using System.Collections.Generic;
using UnityEngine;

// Fortnite-style damage numbers: every hit you land pops a number over the target. Body shots white, headshots
// bigger and yellow. Each pops in with a punch, floats up and drifts aside, then fades. Screen-space (IMGUI), so it
// stays the same size at any distance and works in the WebGL/WebGPU and cloud builds alike.
//   DamageNumbers.Show(worldPos, damage, headshot)
public class DamageNumbers : MonoBehaviour
{
    class Pop { public Vector3 pos; public Vector2 drift; public string text; public bool head; public float t; }

    static DamageNumbers I;
    readonly List<Pop> pops = new List<Pop>();
    GUIStyle style;

    const float Life = 0.9f;
    static readonly Color BodyColor = Color.white;
    static readonly Color HeadColor = new Color(1f, 0.85f, 0.25f);

    public static void Show(Vector3 worldPos, float damage, bool headshot)
    {
        if (I == null)
        {
            var go = new GameObject("DamageNumbers");
            DontDestroyOnLoad(go);
            I = go.AddComponent<DamageNumbers>();
        }
        var side = Random.Range(0.35f, 1f) * (Random.value < 0.5f ? -1f : 1f);
        I.pops.Add(new Pop
        {
            pos = worldPos,
            drift = new Vector2(side * 0.05f, 0.09f),   // in screen heights over the whole life
            text = Mathf.RoundToInt(damage).ToString(),
            head = headshot,
        });
        if (I.pops.Count > 40) I.pops.RemoveAt(0);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { I = null; }

    void Update()
    {
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            pops[i].t += Time.unscaledDeltaTime;
            if (pops[i].t > Life) pops.RemoveAt(i);
        }
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null || pops.Count == 0) return;
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };

        foreach (var p in pops)
        {
            var sp = cam.WorldToScreenPoint(p.pos);
            if (sp.z <= 0f) continue;                                     // behind the camera
            float k = p.t / Life;
            // punch: big for an instant, settles to normal size
            float punch = p.t < 0.08f ? Mathf.Lerp(1.7f, 1.15f, p.t / 0.08f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((p.t - 0.08f) / 0.12f));
            float ease = 1f - (1f - k) * (1f - k);                         // fast start, slows down
            float h = Screen.height;
            float x = sp.x + p.drift.x * h * ease;
            float y = (h - sp.y) - p.drift.y * h * ease;
            int size = Mathf.RoundToInt(h * (p.head ? 0.052f : 0.040f) * punch);
            float alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;

            style.fontSize = size;
            var r = new Rect(x - 200f, y - size, 400f, size * 2f);
            // thick dark outline, then the number
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.85f);
            int o = Mathf.Max(2, size / 14);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx * o, r.y + dy * o, r.width, r.height), p.text, style);
            var c = p.head ? HeadColor : BodyColor;
            GUI.color = new Color(c.r, c.g, c.b, alpha);
            GUI.Label(r, p.text, style);
        }
        GUI.color = Color.white;
    }
}
