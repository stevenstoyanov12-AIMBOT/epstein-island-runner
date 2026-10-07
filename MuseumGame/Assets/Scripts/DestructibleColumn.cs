using UnityEngine;
using System.Collections.Generic;

// Column built from pre-fractured chunks + unbreakable core. Each impact area can lose chunks for up to 6 shots, then the damage there is capped.
public class DestructibleColumn : MonoBehaviour, IWorldState
{
    public GameObject fracturedPrefab; public int maxShotsPerArea = 6; public float areaSize = 1.6f;
    readonly List<Transform> chunks = new List<Transform>();
    readonly Dictionary<Vector3Int,int> areaShots = new Dictionary<Vector3Int,int>();
    readonly List<Transform> all = new List<Transform>();   // every breakable chunk, in build order (same on every client)
    readonly List<int> gone = new List<int>();
    string key;
    void Awake() { key = Net.PathOf(transform); }

    // late join: chunks already knocked out + shots per area
    public string Save()
    {
        var a = new List<string>(); foreach (var kv in areaShots) a.Add(kv.Key.x + "," + kv.Key.y + "," + kv.Key.z + "=" + kv.Value);
        return WorldState.Ints(gone) + "|" + string.Join(";", a);
    }
    public void Load(string s)
    {
        var p = s.Split('|');
        foreach (var i in WorldState.PInts(p[0]))
        {
            if (i < 0 || i >= all.Count || gone.Contains(i)) continue;
            gone.Add(i); var c = all[i]; chunks.Remove(c); if (c) Destroy(c.gameObject);
        }
        if (p.Length > 1)
            foreach (var e in p[1].Split(';'))
            {
                var kv = e.Split('='); if (kv.Length != 2) continue; var v = kv[0].Split(',');
                areaShots[new Vector3Int(WorldState.PI(v[0]), WorldState.PI(v[1]), WorldState.PI(v[2]))] = WorldState.PI(kv[1]);
            }
    }

    void Start()
    {
        if (!fracturedPrefab) return;
        var r = GetComponent<Renderer>(); var target = r.bounds;
        var f = Instantiate(fracturedPrefab, transform.position, Quaternion.identity, transform.parent); f.name = name + "_Chunks";
        // fit fractured set onto this column
        var fb = Bounds(f); var s = new Vector3(target.size.x / fb.size.x, target.size.y / fb.size.y, target.size.z / fb.size.z);
        f.transform.localScale = Vector3.Scale(f.transform.localScale, s);
        fb = Bounds(f); f.transform.position += target.center - fb.center;
        foreach (var mr in f.GetComponentsInChildren<MeshRenderer>())
        {
            mr.sharedMaterials = r.sharedMaterials;
            if (mr.name.StartsWith("ColCore"))   // exposed inner core: painted a different shade so players can tell broken from intact
            {
                var ms = mr.materials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var tint = new Color(0.55f, 0.24f, 0.12f, 1f);
                    if (ms[i].HasProperty("_BaseColor")) ms[i].SetColor("_BaseColor", ms[i].GetColor("_BaseColor") * tint);
                    else if (ms[i].HasProperty("_Color")) ms[i].SetColor("_Color", ms[i].GetColor("_Color") * tint);
                }
                mr.materials = ms; foreach (var m2 in mr.materials) { if (m2.HasProperty("_Smoothness")) m2.SetFloat("_Smoothness", 0.05f); }
            }
            var mc = mr.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mr.GetComponent<MeshFilter>().sharedMesh;
            if (!mr.name.StartsWith("ColCore")) { chunks.Add(mr.transform); all.Add(mr.transform); mr.gameObject.AddComponent<ColumnChunkRelay>().owner = this; }
        }
        r.enabled = false; r.forceRenderingOff = true; foreach (var c in GetComponents<Collider>()) c.enabled = false;
        WorldState.Register(key, this);
    }
    static Bounds Bounds(GameObject g){ var rs = g.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); return b; }

    public void Hit(RaycastHit hit)
    {
        var key = Vector3Int.FloorToInt(hit.point / areaSize);
        areaShots.TryGetValue(key, out int n);
        if (n >= maxShotsPerArea) return;
        areaShots[key] = n + 1;
        // break the intact chunk nearest to the impact
        Transform best = null; float bd = float.MaxValue;
        foreach (var c in chunks)
        {
            if (c == null || c.GetComponent<Rigidbody>()) continue;
            float d = (c.GetComponent<Renderer>().bounds.center - hit.point).sqrMagnitude;
            if (d < bd) { bd = d; best = c; }
        }
        if (best == null || bd > 4f) { WorldState.Changed(key, this); return; }
        chunks.Remove(best); gone.Add(all.IndexOf(best)); WorldState.Changed(key, this);
        best.SetParent(null, true);   // off the scaled chunk set so the rigidbody moves freely
        var mc = best.GetComponent<MeshCollider>(); mc.convex = true;
        var pl = GameObject.Find("Player"); if (pl) foreach (var pc in pl.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mc, pc, true);
        var rb = best.gameObject.AddComponent<Rigidbody>(); rb.mass = 30f; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.AddForce((hit.normal * 1.5f + Vector3.down * 0.5f + Random.insideUnitSphere * 0.5f) * rb.mass, ForceMode.Impulse); rb.AddTorque(Random.insideUnitSphere * rb.mass * 2f, ForceMode.Impulse); best.gameObject.AddComponent<FragmentFade>().life = Random.Range(10f, 14f);
    }
}
public class ColumnChunkRelay : MonoBehaviour { public DestructibleColumn owner; void OnBulletHit(RaycastHit h){ if (owner) owner.Hit(h); } }
