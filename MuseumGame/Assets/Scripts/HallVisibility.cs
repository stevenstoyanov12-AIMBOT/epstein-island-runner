using UnityEngine;
using System.Collections.Generic;

// Shows the auction hall (geometry, props, lights, mood volume) only when the camera is inside it
// or standing in front of its street opening; hides it otherwise so nothing pokes out of the museum.
[DefaultExecutionOrder(1200)]
public class HallVisibility : MonoBehaviour
{
    public string[] roots = { "AuctionHall", "AuctionHall_Props", "HallLighting", "HallMoodVolume" };
    public Bounds hall = new Bounds(new Vector3(-10.55f, 13f, 1.7f), new Vector3(38.3f, 28f, 41.6f));
    public Bounds entrance = new Bounds(new Vector3(-36f, 5f, 1.8f), new Vector3(16f, 12f, 22f));
    readonly List<Renderer> rends = new List<Renderer>();
    readonly List<Light> lights = new List<Light>();
    readonly List<Behaviour> vols = new List<Behaviour>();
    readonly List<Renderer> overlaps = new List<Renderer>();
    bool shown = true, init, first = true;
    public static bool forceShow; public static List<Renderer> Overlaps = new List<Renderer>();

    void Collect()
    {
        rends.Clear(); lights.Clear(); vols.Clear(); overlaps.Clear();
        foreach (var n in roots)
        {
            var g = GameObject.Find(n); if (g == null) continue;
            rends.AddRange(g.GetComponentsInChildren<Renderer>(true));
            lights.AddRange(g.GetComponentsInChildren<Light>(true));
            foreach (var v in g.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true)) vols.Add(v);
            foreach (var p in g.GetComponentsInChildren<ReflectionProbe>(true)) vols.Add(p);
        }
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (r.name.EndsWith("_HallOverlap")) overlaps.Add(r);
        Overlaps = overlaps; init = true;
        foreach (var l in lights) if (l) shadowSaved[l] = l.shadows;
        foreach (var r in rends) if (r) castSaved[r] = r.shadowCastingMode;
    }

    // street area in front of the hall opening: hall is shown in a cheap "far" mode (big meshes only, no small props, no shadows)
    public Bounds farZone = new Bounds(new Vector3(-75f, 10f, 1.8f), new Vector3(62f, 40f, 90f));
    public float smallProp = 1.6f; public float openingX = -29.9f; int mode = -1;

    void LateUpdate() { Tick(); }
    public void Tick()
    {
        if (!init || rends.Count == 0) { Collect(); first = true; }
        var cam = Camera.main; if (cam == null) return;
        var p = cam.transform.position;
        bool west = p.x < openingX; // anywhere in front of the open west face: hall is only seen through the opening
        int want = (hall.Contains(p) || entrance.Contains(p) || forceShow || (west && p.x > openingX - 35f)) ? 2 : (west ? 1 : 0); // loaded early on the street facing the opening; hidden elsewhere so it never shows through museum walls
        bool inHall = hall.Contains(p);
        foreach (var r in overlaps) if (r != null) r.enabled = !inHall && want == 0; // museum slabs that cut through the hall
        // moonlight blend: full in/around the hall; ramps up as you approach the gallery door so the portal crossing needs no lighting switch
        float near = 1f - Mathf.Clamp01((Vector3.Distance(p, galleryDoor) - 0.6f) / 5.5f);
        SetLight(Mathf.Max(want > 0 ? 1f : 0f, near));
        if (want == mode && !first) return; first = false;
        mode = want; shown = want == 2;
        Apply(want);
    }
    static HallVisibility _i;
    public static HallVisibility I { get { if (_i == null) _i = FindAnyObjectByType<HallVisibility>(); return _i; } }
    void Awake() { _i = this; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { _i = null; Overlaps = new List<Renderer>(); forceShow = false; }
    // portal camera: temporarily show the full hall only for its own render, never for the main camera
    public static void PortalRender(bool on)
    {
        if (I == null || I.mode == 2) return;
        if (!I.init || I.rends.Count == 0) I.Collect();
        I.Apply(on ? 2 : I.mode);
    }
    void Apply(int m)
    {
        bool want = m > 0;
        if (beams == null) { beams = new List<Renderer>(); var ts = GameObject.Find("TowerSearchlights"); if (ts) foreach (var b in ts.GetComponentsInChildren<Renderer>(true)) if (b.name == "Beam") beams.Add(b); }
        foreach (var b in beams) if (b) b.enabled = true; // searchlight haze reads as a misty ceiling over the hall from the street
        foreach (var r in rends) if (r != null && !r.name.StartsWith("Frag"))
        {
            r.enabled = m > 0;
            UnityEngine.Rendering.ShadowCastingMode sc; r.shadowCastingMode = (m == 2 && castSaved.TryGetValue(r, out sc)) ? sc : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach (var l in lights) if (l != null) { l.enabled = m > 0; LightShadows ls; l.shadows = (m == 2 && shadowSaved.TryGetValue(l, out ls)) ? ls : LightShadows.None; }
        foreach (var v in vols) if (v != null) v.enabled = want;
    }
    readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> castSaved = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
    readonly Dictionary<Light, LightShadows> shadowSaved = new Dictionary<Light, LightShadows>();
    List<Renderer> beams;
    public Vector3 galleryDoor = new Vector3(19.86f, 3.2f, 1.4f);
    float lightT = -1f;
    void SetLight(float t)
    {
        if (sun == null) { var go = GameObject.Find("Directional Light"); if (go) { sun = go.GetComponent<Light>(); dayCol = sun.color; dayInt = sun.intensity; dayRot = sun.transform.rotation; dayAmb = new Color[]{RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor}; } }
        if (sun == null || Mathf.Abs(t - lightT) < 0.001f) return; lightT = t;
        sun.color = Color.Lerp(dayCol, new Color(0.55f, 0.68f, 1f), t); sun.intensity = Mathf.Lerp(dayInt, 0.35f, t); sun.transform.rotation = Quaternion.Slerp(dayRot, Quaternion.Euler(35f, 300f, 0f), t);
        RenderSettings.ambientSkyColor = Color.Lerp(dayAmb[0], new Color(0.45f, 0.5f, 0.62f), t);
        RenderSettings.ambientEquatorColor = Color.Lerp(dayAmb[1], new Color(0.3f, 0.33f, 0.42f), t);
        RenderSettings.ambientGroundColor = Color.Lerp(dayAmb[2], new Color(0.14f, 0.17f, 0.26f), t);
    }
    Light sun; Color dayCol; float dayInt; Quaternion dayRot; Color[] dayAmb;
}
