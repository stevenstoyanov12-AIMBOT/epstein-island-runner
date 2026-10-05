using UnityEngine;
using System.Collections.Generic;

// 3 cracked-stone spots per base (F/L/R). Only a bullet that actually hits a shard (or that spot's crater) breaks/deepens it; max 6 shots per spot.
public class ShardCrater : MonoBehaviour
{
    public int maxShots = 6;
    class Spot { public List<Transform> shards = new List<Transform>(); public int shots; public Transform cracks; }
    readonly Dictionary<string, Spot> spots = new Dictionary<string, Spot>();

    void Start()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string side = null;
            if (t.name.StartsWith("Shard_")) side = t.name.Split('_')[1];
            else if (t.name.StartsWith("Crater_Pit_") || t.name.StartsWith("Crack_Lines_")) side = t.name.Substring(t.name.LastIndexOf('_') + 1);
            if (side == null) { var mf0 = t.GetComponent<MeshFilter>(); if (mf0 && !t.GetComponent<Collider>()) t.gameObject.AddComponent<MeshCollider>().sharedMesh = mf0.sharedMesh; continue; }
            if (!spots.ContainsKey(side)) spots[side] = new Spot();
            t.localRotation = Quaternion.identity; t.localPosition = Vector3.zero; t.localScale = Vector3.one;
            var sp = spots[side];
            if (t.name.StartsWith("Crack_Lines_")) { sp.cracks = t; t.gameObject.SetActive(false); continue; }
            var mf = t.GetComponent<MeshFilter>(); if (!mf) continue;
            if (t.name.StartsWith("Shard_")) { t.localRotation = Quaternion.identity; t.localPosition = Vector3.zero; t.localScale = Vector3.one; sp.shards.Add(t); }
            var mc = t.GetComponent<MeshCollider>(); if (mc == null) mc = t.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            var r = t.gameObject.AddComponent<CraterRelay>(); r.owner = this; r.side = side;
        }
    }

    public void Hit(RaycastHit hit, string side)
    {
                if (!spots.TryGetValue(side, out var sp) || sp.shots >= maxShots) return;
        sp.shots++;
        if (sp.cracks) sp.cracks.gameObject.SetActive(true);
        float blast = 0.07f + 0.035f * sp.shots;              // tight: only around the exact impact
        // always knock out the shard that was actually hit
        var direct = hit.collider.transform;
        foreach (var s in sp.shards.ToArray())
        {
            if (!s) continue;
            float d = Vector3.Distance(s.GetComponent<Renderer>().bounds.center, hit.point);
            if (s == direct || d < blast)
            {
                sp.shards.Remove(s); s.SetParent(null, true);
                var rel = s.GetComponent<CraterRelay>(); if (rel) Destroy(rel);
                var mc = s.GetComponent<MeshCollider>(); mc.convex = true;
                var rb = s.gameObject.AddComponent<Rigidbody>(); rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.mass = 3f;
                rb.linearVelocity = hit.normal * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere + Vector3.up * 0.5f;
                rb.angularVelocity = Random.insideUnitSphere * 10f;
                s.gameObject.AddComponent<FragmentFade>().life = Random.Range(8f, 12f);
            }
            else if (d < blast + 0.12f && s.localScale.x > 0.97f) { s.localScale *= 0.97f; s.position -= hit.normal * Random.Range(0.005f, 0.015f); }
        }
    }
}
public class CraterRelay : MonoBehaviour { public ShardCrater owner; public string side; void OnBulletHit(RaycastHit h) { if (owner) owner.Hit(h, side); } }
