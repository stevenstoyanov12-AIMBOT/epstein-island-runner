using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Rendering;

// Rebuilds clean collision for the photogrammetry vestibule + surrounding rooms.
// Every visible surface is sampled with rays (down, and along X and Z) on a 10 cm grid, turned into
// axis-aligned patches, merged into large flat pieces, and cooked into one static MeshCollider.
// The old decimated scan colliders are disabled. Collision therefore matches what you see, item for item.
public static class ScanCollisionBaker
{
    public static Bounds region = new Bounds(new Vector3(18.25f, 3.0f, 9.3f), new Vector3(20.7f, 7.4f, 26.4f)); // up to the facade (x 26.45): covers the dining room, never the street
    const float G = 0.1f;      // grid
    const float Q = 0.02f;     // plane snap
    const int L = 31;          // temp layer
    public static Bounds streetCarve = new Bounds(new Vector3(28.5f, 0.5f, 9.3f), new Vector3(4.1f, 3.3f, 30f)); // street under the dining room's overhang
    public static Bounds doorCarve = new Bounds(new Vector3(19.86f, 3.2f, 1.4f), new Vector3(1.0f, 1.8f, 1.0f)); // exactly the portal opening

    static bool IsScan(Renderer r)
    {
        var root = r.transform.root.name;
        if (root == "UpperVestibule_HQ") return true;
        if (root != "Museum") return false;
        return r.name.StartsWith("Object_") || r.name.StartsWith("DiningRoom");
    }

    public static string Bake()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var temps = new List<GameObject>(); var oldCols = new List<Collider>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!IsScan(r)) continue;
            var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
            foreach (var c in r.GetComponents<Collider>()) oldCols.Add(c);
            var t = new GameObject("__tmpcol"); t.layer = L; t.transform.SetParent(r.transform, false);
            var mc = t.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temps.Add(t);
        }
        Physics.SyncTransforms();
        bool bf = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
        int mask = 1 << L;
        // plane buckets: key = (axis, facing, quantized coordinate) -> set of (u,v) cells
        var buckets = new Dictionary<long, HashSet<long>>();
        int hits = 0;
        System.Action<int, int, float, int, int> add = (axis, face, coord, u, v) =>
        {
            long k = ((long)axis << 40) | ((long)(face > 0 ? 1 : 0) << 39) | (uint)(Mathf.RoundToInt(coord / Q) + 100000);
            HashSet<long> set; if (!buckets.TryGetValue(k, out set)) buckets[k] = set = new HashSet<long>();
            set.Add(((long)(u + 50000) << 20) | (long)(v + 50000)); hits++;
        };
        Vector3 mn = region.min, mx = region.max;
        int nx = Mathf.CeilToInt((mx.x - mn.x) / G), ny = Mathf.CeilToInt((mx.y - mn.y) / G), nz = Mathf.CeilToInt((mx.z - mn.z) / G);
        // walk a line, collecting every surface crossing
        System.Func<Vector3, Vector3, float, List<RaycastHit>> march = (o, d, len) =>
        {
            var list = new List<RaycastHit>(); float gone = 0f; RaycastHit h; int guard = 0;
            while (gone < len && guard++ < 200 && Physics.Raycast(o + d * gone, d, out h, len - gone, mask, QueryTriggerInteraction.Ignore))
            { list.Add(h); gone += h.distance + 0.004f; }
            return list;
        };
        // 1) vertical rays -> horizontal patches (floors, tops, ceilings, stair treads)
        for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
        {
            float x = mn.x + (i + 0.5f) * G, z = mn.z + (k + 0.5f) * G;
            foreach (var h in march(new Vector3(x, mx.y, z), Vector3.down, mx.y - mn.y))
            {
                if (Mathf.Abs(h.normal.y) < 0.35f) continue;
                if (doorCarve.Contains(h.point) || streetCarve.Contains(h.point)) continue;
                add(1, h.normal.y > 0 ? 1 : -1, h.point.y, i, k);
            }
        }
        // 2) rays along X -> patches facing X (walls, furniture sides, risers)
        for (int j = 0; j < ny; j++) for (int k = 0; k < nz; k++)
        {
            float y = mn.y + (j + 0.5f) * G, z = mn.z + (k + 0.5f) * G;
            foreach (var h in march(new Vector3(mn.x, y, z), Vector3.right, mx.x - mn.x))
            {
                if (Mathf.Abs(h.normal.x) < 0.35f) continue; if (doorCarve.Contains(h.point) || streetCarve.Contains(h.point)) continue;
                add(0, h.normal.x > 0 ? 1 : -1, h.point.x, j, k);
            }
        }
        // 3) rays along Z
        for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
        {
            float y = mn.y + (j + 0.5f) * G, x = mn.x + (i + 0.5f) * G;
            foreach (var h in march(new Vector3(x, y, mn.z), Vector3.forward, mx.z - mn.z))
            {
                if (Mathf.Abs(h.normal.z) < 0.35f) continue; if (doorCarve.Contains(h.point) || streetCarve.Contains(h.point)) continue;
                add(2, h.normal.z > 0 ? 1 : -1, h.point.z, i, j);
            }
        }
        Physics.queriesHitBackfaces = bf;
        foreach (var t in temps) Object.DestroyImmediate(t);

        // 4) greedy-merge each plane bucket into rectangles, emit quads (both windings: bullets & CC from either side)
        var verts = new List<Vector3>(); var tris = new List<int>(); int rects = 0;
        foreach (var kv in buckets)
        {
            int axis = (int)(kv.Key >> 40); float coord = ((int)(kv.Key & 0xFFFFFFFF) - 100000) * Q;
            var cells = kv.Value;
            while (cells.Count > 0)
            {
                long c0 = 0; foreach (var c in cells) { c0 = c; break; }
                int u0 = (int)(c0 >> 20) - 50000, v0 = (int)(c0 & 0xFFFFF) - 50000;
                // grow along v then u
                int v1 = v0; while (cells.Contains(((long)(u0 + 50000) << 20) | (long)(v1 + 1 + 50000))) v1++;
                int v0b = v0; while (cells.Contains(((long)(u0 + 50000) << 20) | (long)(v0b - 1 + 50000))) v0b--;
                int u1 = u0; bool ok = true;
                while (ok) { for (int v = v0b; v <= v1; v++) if (!cells.Contains(((long)(u1 + 1 + 50000) << 20) | (long)(v + 50000))) { ok = false; break; } if (ok) u1++; }
                int u0b = u0; ok = true;
                while (ok) { for (int v = v0b; v <= v1; v++) if (!cells.Contains(((long)(u0b - 1 + 50000) << 20) | (long)(v + 50000))) { ok = false; break; } if (ok) u0b--; }
                for (int u = u0b; u <= u1; u++) for (int v = v0b; v <= v1; v++) cells.Remove(((long)(u + 50000) << 20) | (long)(v + 50000));
                // rectangle corners in world
                Vector3 a, b, c2, d;
                float ua = u0b * G, ub = (u1 + 1) * G, va = v0b * G, vb = (v1 + 1) * G;
                if (axis == 1) { a = new Vector3(mn.x + ua, coord, mn.z + va); b = new Vector3(mn.x + ub, coord, mn.z + va); c2 = new Vector3(mn.x + ub, coord, mn.z + vb); d = new Vector3(mn.x + ua, coord, mn.z + vb); }
                else if (axis == 0) { a = new Vector3(coord, mn.y + ua, mn.z + va); b = new Vector3(coord, mn.y + ub, mn.z + va); c2 = new Vector3(coord, mn.y + ub, mn.z + vb); d = new Vector3(coord, mn.y + ua, mn.z + vb); }
                else { a = new Vector3(mn.x + va, mn.y + ua, coord); b = new Vector3(mn.x + va, mn.y + ub, coord); c2 = new Vector3(mn.x + vb, mn.y + ub, coord); d = new Vector3(mn.x + vb, mn.y + ua, coord); }
                int s = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c2); verts.Add(d);
                tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3, s, s + 2, s + 1, s, s + 3, s + 2 });
                rects++;
            }
        }
        var mesh = new Mesh { name = "ScanCollision", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        AssetDatabase.DeleteAsset("Assets/Museum/ScanCollision.asset");
        AssetDatabase.CreateAsset(mesh, "Assets/Museum/ScanCollision.asset");

        var old = GameObject.Find("ScanCollision"); if (old) Object.DestroyImmediate(old);
        var go = new GameObject("ScanCollision"); go.isStatic = true;
        var col = go.AddComponent<MeshCollider>(); col.sharedMesh = mesh;
        col.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.UseFastMidphase;
        foreach (var c in oldCols) if (c) c.enabled = false;
        return $"hits {hits} rects {rects} tris {tris.Count / 3} oldCols {oldCols.Count} time {sw.ElapsedMilliseconds}ms";
    }

    // Airtight envelope: flood-fills every floor the player can actually walk to (from seed spots),
    // and puts a solid wall on every edge of that walkable area that doesn't continue to walkable floor.
    // Holes in the scanned walls, cracks and gaps under/behind furniture are sealed. Stops body and camera.
    public static Vector3[] seeds = { new Vector3(19.86f, 3f, 3.2f), new Vector3(18.3f, 1f, 3.5f), new Vector3(14.4f, 1f, 7.6f), new Vector3(14.4f, 1f, 14f), new Vector3(24.5f, 3.5f, 4f), new Vector3(19.0f, 0.9f, 1.5f), new Vector3(18.3f, 0.9f, 2.2f), new Vector3(20.5f, 0.9f, 2.0f) };
    public static Vector3? debugPt; public static System.Text.StringBuilder dbg = new System.Text.StringBuilder();
    public static string BakeBoundary()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var old = GameObject.Find("ScanBoundary"); if (old) Object.DestroyImmediate(old);
        Physics.SyncTransforms();
        var pl = GameObject.Find("Player"); var plT = pl ? pl.transform : null;
        System.Func<Collider, bool> skip = c => c.isTrigger || (plT && c.transform.IsChildOf(plT));
        Vector3 mn = region.min, mx = region.max;
        int nx = Mathf.CeilToInt((mx.x - mn.x) / G), nz = Mathf.CeilToInt((mx.z - mn.z) / G);
        var levels = new List<float>[nx, nz]; var ceils = new List<float>[nx, nz];
        for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
        {
            float x = mn.x + (i + 0.5f) * G, z = mn.z + (k + 0.5f) * G;
            var ys = new List<(float y, bool up)>(); float from = mx.y + 1f; RaycastHit h; int guard = 0;
            while (guard++ < 60 && Physics.Raycast(new Vector3(x, from, z), Vector3.down, out h, from - (mn.y - 0.5f), ~0, QueryTriggerInteraction.Ignore))
            { if (!skip(h.collider)) ys.Add((h.point.y, h.normal.y > 0.6f)); from = h.point.y - 0.003f; }
            var lv = new List<float>(); var cl = new List<float>();
            for (int a = 0; a < ys.Count; a++)
            {
                if (!ys[a].up) continue;
                float above = a > 0 ? ys[a - 1].y : float.MaxValue; // next surface above
                if (above - ys[a].y >= 1.3f && ys[a].y > -0.305f) { lv.Add(ys[a].y); cl.Add(above); }
            }
            levels[i, k] = lv; ceils[i, k] = cl;
        }
        // patch small holes in the scan's floors/stair treads (cells surrounded by floor at ~the same height)
        var filled = new List<Vector3>();
        for (int pass = 0; pass < 3; pass++)
        {
            var add = new List<(int, int, float, float)>();
            for (int i = 1; i < nx - 1; i++) for (int k = 1; k < nz - 1; k++)
            {
                var ns = new List<float>(); var cs = new List<float>();
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) { if (a == 0 && b == 0) continue; var l2 = levels[i + a, k + b]; for (int q2 = 0; q2 < l2.Count; q2++) { ns.Add(l2[q2]); cs.Add(ceils[i + a, k + b][q2]); } }
                ns.Sort();
                for (int q2 = 0; q2 < ns.Count; q2++)
                {
                    float med = ns[q2]; int cnt = 0; foreach (var v in ns) if (Mathf.Abs(v - med) < 0.35f) cnt++;
                    if (cnt < 5) continue;
                    bool has = false; foreach (var v in levels[i, k]) if (Mathf.Abs(v - med) < 0.4f) has = true;
                    if (!has) { add.Add((i, k, med, med + 1.6f)); break; }
                }
            }
            foreach (var t in add) { levels[t.Item1, t.Item2].Add(t.Item3); ceils[t.Item1, t.Item2].Add(t.Item4); filled.Add(new Vector3(mn.x + t.Item1 * G, t.Item3, mn.z + t.Item2 * G)); }
        }
        System.Func<Vector3, Vector3, bool> blocked = (a, b) =>
        {
            var d = b - a; float len = d.magnitude; d /= len;
            foreach (var hh in new[] { 0.5f, 0.9f, 1.35f })
            {
                var o = a + Vector3.up * hh;
                foreach (var hit in Physics.RaycastAll(o - d * 0.02f, d, len + 0.04f, ~0, QueryTriggerInteraction.Ignore)) if (!skip(hit.collider)) return true;
            }
            return false;
        };
        // flood
        var seen = new HashSet<long>(); var q = new Queue<(int, int, int)>();
        System.Func<int, int, int, long> key = (i, k, l) => ((long)i << 32) | ((long)k << 8) | (long)l;
        foreach (var sd in seeds)
        {
            int i = Mathf.FloorToInt((sd.x - mn.x) / G), k = Mathf.FloorToInt((sd.z - mn.z) / G);
            if (i < 0 || k < 0 || i >= nx || k >= nz) continue;
            var lv = levels[i, k]; int best = -1; float bd = 99;
            for (int l = 0; l < lv.Count; l++) { float dd = Mathf.Abs(lv[l] - (sd.y - 0.9f)); if (lv[l] < sd.y && dd < bd) { bd = dd; best = l; } }
            if (best >= 0 && seen.Add(key(i, k, best))) q.Enqueue((i, k, best));
        }
        int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
        var walls = new Dictionary<long, HashSet<long>>(); int wallCount = 0;
        System.Action<int, int, int, float, float> addWall = (i, k, dir, y, top) =>
        {
            // vertical strip on the edge of cell (i,k) toward dir, from y-0.15 up to y+2.6
            int axis = dir < 2 ? 0 : 2; float coord = axis == 0 ? mn.x + (i + (dir == 0 ? 1 : 0)) * G : mn.z + (k + (dir == 2 ? 1 : 0)) * G;
            long bk = ((long)axis << 40) | (uint)(Mathf.RoundToInt(coord / G) + 100000);
            HashSet<long> set; if (!walls.TryGetValue(bk, out set)) walls[bk] = set = new HashSet<long>();
            int u = axis == 0 ? k : i; int y0 = Mathf.FloorToInt((y - 0.15f - mn.y) / G), y1 = Mathf.FloorToInt((Mathf.Min(y + 2.6f, top - 0.02f) - mn.y) / G);
            for (int yy = y0; yy < y1; yy++) set.Add(((long)(u + 50000) << 20) | (long)(yy + 50000));
            wallCount++;
        };
        int nodes = 0;
        while (q.Count > 0)
        {
            var (i, k, l) = q.Dequeue(); nodes++; float y = levels[i, k][l];
            var c = new Vector3(mn.x + (i + 0.5f) * G, y, mn.z + (k + 0.5f) * G);
            for (int d = 0; d < 4; d++)
            {
                int ni = i + dx[d], nk = k + dz[d];
                if (ni < 0 || nk < 0 || ni >= nx || nk >= nz) continue; // region edge: leave open (continues into the rest of the museum)
                var nl = levels[ni, nk]; int m = -1; float md = 99;
                for (int a = 0; a < nl.Count; a++) { float dd = Mathf.Abs(nl[a] - y); if (dd < md) { md = dd; m = a; } }
                var nc = new Vector3(mn.x + (ni + 0.5f) * G, m >= 0 ? nl[m] : y, mn.z + (nk + 0.5f) * G);
                bool solid = blocked(c + Vector3.up * Mathf.Max(0f, nc.y - y), nc + Vector3.up * Mathf.Max(0f, y - nc.y));
                bool ok = m >= 0 && md <= 0.65f && !solid; // stairs + scan noise
                if (ok) { if (seen.Add(key(ni, nk, m))) q.Enqueue((ni, nk, m)); }
                else if (!solid) { addWall(i, k, d, y, ceils[i, k][l]); /* only seal real gaps; never taller than this spot's own headroom */ if (debugPt.HasValue && Mathf.Abs(c.x - debugPt.Value.x) < 0.16f && Mathf.Abs(c.z - debugPt.Value.z) < 0.16f) dbg.AppendLine($"wall from {c} dir {d} nb {nc} m {m} md {md} lv [{string.Join(",", nl)}]"); }
            }
        }
        // merge wall strips into rectangles
        var verts = new List<Vector3>(); var tris = new List<int>(); int rects = 0;
        foreach (var kv in walls)
        {
            int axis = (int)(kv.Key >> 40); float coord = ((int)(kv.Key & 0xFFFFFFFF) - 100000) * G; var cells = kv.Value;
            while (cells.Count > 0)
            {
                long c0 = 0; foreach (var cc in cells) { c0 = cc; break; }
                int u0 = (int)(c0 >> 20) - 50000, v0 = (int)(c0 & 0xFFFFF) - 50000;
                System.Func<int, int, long> K = (u, v) => ((long)(u + 50000) << 20) | (long)(v + 50000);
                int v1 = v0; while (cells.Contains(K(u0, v1 + 1))) v1++; int v0b = v0; while (cells.Contains(K(u0, v0b - 1))) v0b--;
                int u1 = u0; bool okk = true; while (okk) { for (int v = v0b; v <= v1; v++) if (!cells.Contains(K(u1 + 1, v))) { okk = false; break; } if (okk) u1++; }
                int u0b = u0; okk = true; while (okk) { for (int v = v0b; v <= v1; v++) if (!cells.Contains(K(u0b - 1, v))) { okk = false; break; } if (okk) u0b--; }
                for (int u = u0b; u <= u1; u++) for (int v = v0b; v <= v1; v++) cells.Remove(K(u, v));
                float ua = u0b * G, ub = (u1 + 1) * G, ya = mn.y + v0b * G, yb = mn.y + (v1 + 1) * G;
                Vector3 a, b, c2, d;
                if (axis == 0) { a = new Vector3(coord, ya, mn.z + ua); b = new Vector3(coord, ya, mn.z + ub); c2 = new Vector3(coord, yb, mn.z + ub); d = new Vector3(coord, yb, mn.z + ua); }
                else { a = new Vector3(mn.x + ua, ya, coord); b = new Vector3(mn.x + ub, ya, coord); c2 = new Vector3(mn.x + ub, yb, coord); d = new Vector3(mn.x + ua, yb, coord); }
                int s = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c2); verts.Add(d);
                tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3, s, s + 2, s + 1, s, s + 3, s + 2 }); rects++;
            }
        }
        foreach (var f in filled)
        {
            int s0 = verts.Count; verts.Add(f); verts.Add(f + new Vector3(G, 0, 0)); verts.Add(f + new Vector3(G, 0, G)); verts.Add(f + new Vector3(0, 0, G));
            tris.AddRange(new[] { s0, s0 + 2, s0 + 1, s0, s0 + 3, s0 + 2, s0, s0 + 1, s0 + 2, s0, s0 + 2, s0 + 3 });
        }
        var mesh = new Mesh { name = "ScanBoundary", indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        AssetDatabase.DeleteAsset("Assets/Museum/ScanBoundary.asset"); AssetDatabase.CreateAsset(mesh, "Assets/Museum/ScanBoundary.asset");
        var go = new GameObject("ScanBoundary"); go.isStatic = true; var col = go.AddComponent<MeshCollider>(); col.sharedMesh = mesh;
        return $"filled {filled.Count} nodes {nodes} wallEdges {wallCount} rects {rects} time {sw.ElapsedMilliseconds}ms";
    }
}
