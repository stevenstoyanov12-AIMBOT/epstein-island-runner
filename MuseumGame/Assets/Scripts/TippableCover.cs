using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

// Press E near a desk to kick it onto its side as cover (top faces away from the player, like Uncharted).
public class TippableCover : MonoBehaviour
{
    public float useRange = 3.0f;
    public float tipTime = 0.35f;
    bool tipped, busy;
    Transform player; Camera cam;
    static TippableCover focused;

    void Start(){ var p = GameObject.Find("Player"); if (p) player = p.transform; cam = Camera.main; }

    void Update()
    {
        if (tipped || busy || player == null) { if (focused == this) focused = null; return; }
        var b = GetBounds();
        Vector3 closest = b.ClosestPoint(player.position);
        closest.y = player.position.y; 
        float d = Vector3.Distance(new Vector3(closest.x,0,closest.z), new Vector3(player.position.x,0,player.position.z));
        bool looking = cam && Vector3.Dot(cam.transform.forward, (b.center - cam.transform.position).normalized) > 0.5f;
        if (d < useRange && looking) { if (focused == null || focused == this) focused = this; }
        else if (focused == this) focused = null;
        if (focused == this && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            StartCoroutine(Tip(player.position));
            if (Net.I != null) Net.I.SendAct("tip", transform, player.position);   // the other players see it tip the same way
        }
    }

    Bounds GetBounds(){ var rs = GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

    // another player kicked this desk over (from position `from`)
    public static void RemoteTip(string path, Vector3 from)
    {
        var t = Net.FindByPath(path); var c = t != null ? t.GetComponent<TippableCover>() : null;
        if (c != null && !c.tipped && !c.busy) c.StartCoroutine(c.Tip(from));
    }

    IEnumerator Tip(Vector3 from)
    {
        busy = true; focused = null;
        var b = GetBounds();
        // pick the short horizontal axis of the desk
        Vector3 ax = Flat(transform.right), az = Flat(transform.forward), ay = Flat(transform.up);
        Vector3[] cands = { ax, az, ay };
        Vector3 shortDir = ax; float best = float.MaxValue;
        foreach (var c in cands)
        {
            if (c.sqrMagnitude < 0.1f) continue;
            float ext = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(c.x), 0, Mathf.Abs(c.z))));
            if (ext < best) { best = ext; shortDir = c.normalized; }
        }
        Vector3 toPlayer = from - b.center; toPlayer.y = 0;
        Vector3 away = Vector3.Dot(toPlayer, shortDir) > 0 ? -shortDir : shortDir;
        Vector3 pivot = new Vector3(b.center.x, b.min.y, b.center.z) + away * best;
        Vector3 axis = Vector3.Cross(Vector3.up, away);
        float t = 0, done = 0;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / tipTime);
            float target = 90f * (t * t);
            transform.RotateAround(pivot, axis, target - done); done = target;
            yield return null;
        }
        tipped = true; busy = false;
        foreach (var mc in GetComponentsInChildren<MeshCollider>()) { mc.enabled = false; mc.enabled = true; }
    }

    static Vector3 Flat(Vector3 v){ v.y = 0; return v; }

    void OnGUI()
    {
        if (focused != this) return;
        var st = new GUIStyle(GUI.skin.box){ fontSize = 22, alignment = TextAnchor.MiddleCenter };
        GUI.Box(new Rect(Screen.width/2f - 170, Screen.height*0.72f, 340, 44), "Press E to flip desk for cover", st);
    }
}
