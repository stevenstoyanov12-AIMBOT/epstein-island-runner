using UnityEngine;
// Tower sniper: turns with its tower searchlight; if the player stays in the beam > spotTime, fires at them.
public class SniperGuard : MonoBehaviour
{
    public TowerSearchlight light; public float spotTime = 1f, fireInterval = 1.4f, turnSpeed = 4f;
    Transform player, localPlayer, rifle; Light spot; float inBeam, nextShot, hitFlash; Light flash; LineRenderer tracer; float tracerT;
    static float screenFlash; Vector3 aimPt, wayPt, lastSeen; float nextWay, lostT; bool locked; public float scanRadius = 55f, trackSpeed = 6f, scanSpeed = 9f, lockedFireInterval = 0.8f, memory = 3f; Color baseCol; Renderer beamR; Color beamBase; float redT, beamLen;
    void Start()
    {
        memory = 0.3f; trackSpeed = 1f;   // leave the light and they let go of you almost at once
        var p = GameObject.Find("Player"); if (p) { player = p.transform; localPlayer = player; }
        if (!light) { float best = 1e9f; foreach (var l in FindObjectsByType<TowerSearchlight>()) { float d = (l.transform.position - transform.position).sqrMagnitude; if (d < best) { best = d; light = l; } } }
        if (light) { light.enabled = false; aimPt = light.transform.position + light.transform.forward * 30f; aimPt.y = 0; wayPt = aimPt; spot = light.GetComponent<Light>(); if (spot) baseCol = spot.color; var bt = light.transform.Find("Beam"); if (bt) { beamR = bt.GetComponent<Renderer>(); if (beamR) beamBase = beamR.material.color; } foreach (var r in light.GetComponentsInChildren<Renderer>()) if (r.name != "Beam") r.enabled = false; }
        foreach (var t in GetComponentsInChildren<Transform>()) if (t.name.Contains("SniperRifle")) rifle = t;
        var fg = new GameObject("SniperFlash"); flash = fg.AddComponent<Light>(); flash.type = LightType.Point; flash.color = new Color(1f, 0.75f, 0.4f); flash.range = 25f; flash.intensity = 0; flash.shadows = LightShadows.None;
        var tg = new GameObject("SniperTracer"); tracer = tg.AddComponent<LineRenderer>(); tracer.positionCount = 2; tracer.startWidth = 0.05f; tracer.endWidth = 0.02f;
        tracer.material = new Material(Shader.Find("Sprites/Default")); tracer.startColor = new Color(1f, 0.85f, 0.5f, 1f); tracer.endColor = new Color(1f, 0.85f, 0.5f, 0f); tracer.enabled = false;
    }
    bool avoidLast; int muzzleIdx = -1; Mesh rmesh;
    Vector3 Muzzle()
    {
        if (!rifle) return transform.position + Vector3.up * 4f + transform.forward * 2f;
        if (muzzleIdx < 0)
        {
            rmesh = rifle.GetComponent<MeshFilter>().sharedMesh; var vs = rmesh.vertices; var ext = rmesh.bounds.extents;
            Vector3 ax = ext.x > ext.y && ext.x > ext.z ? Vector3.right : (ext.y > ext.z ? Vector3.up : Vector3.forward);
            int hi = 0, lo = 0; float bh = -1e9f, bl = 1e9f;
            for (int i = 0; i < vs.Length; i++) { float d = Vector3.Dot(vs[i], ax); if (d > bh) { bh = d; hi = i; } if (d < bl) { bl = d; lo = i; } }
            var an = GetComponent<Animator>(); Vector3 head = transform.position + Vector3.up * 4.5f;
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == "Head") head = t.position;
            muzzleIdx = (rifle.TransformPoint(vs[hi]) - head).sqrMagnitude > (rifle.TransformPoint(vs[lo]) - head).sqrMagnitude ? hi : lo;
        }
        return rifle.TransformPoint(rmesh.vertices[muzzleIdx]);
    }
    // several players: stay on the one we are locked on to; otherwise whoever is currently inside the lit cone (local player first)
    void PickTarget(Vector3 mz)
    {
        if (locked && player != null && (player == localPlayer || IsAliveProxy(player))) return;
        if (localPlayer != null && LitFor(localPlayer, mz)) { player = localPlayer; return; }
        foreach (var g in GazeTarget.All) if (g != null && g.remoteProxy && g.proxyAlive && LitFor(g.transform, mz)) { player = g.transform; return; }
        player = localPlayer;
    }
    static bool IsAliveProxy(Transform t) { var g = t != null ? t.GetComponent<GazeTarget>() : null; return g != null && g.proxyAlive; }
    bool LitFor(Transform t, Vector3 mz)
    {
        var old = player; player = t;
        var toP = t.position + Vector3.up * 0.5f - mz; float half = spot ? spot.spotAngle * 0.5f : 12f;
        bool lit = SeesPlayer(mz) && Vector3.Angle(light.transform.forward, toP) < half * 0.7f && InQuarter(t.position);
        player = old; return lit;
    }
    void Update()
    {
        if (!light || !player) return;
        var mz = Muzzle();
        PickTarget(mz);
        // player visible inside the beam cone?
        var toP = player.position + Vector3.up * 0.5f - mz;
        float half = spot ? spot.spotAngle * 0.5f : 12f;
        bool los = SeesPlayer(mz);   // nothing solid between the sniper and any part of the player
        bool lit = los && Vector3.Angle(light.transform.forward, toP) < half * 0.7f;   // and he is inside the lit cone
        if (lit && !InQuarter(player.position)) lit = false;
        if (lit) { locked = true; lostT = 0; lastSeen = player.position; }
        else if (locked) { lostT += Time.deltaTime; if (lostT > memory) { locked = false; nextWay = 0f; avoidLast = true; } }   // gave him up: pick a new sweep point away from where he was
        if (locked)
        {
            var tgt = lostT > 0 ? lastSeen : player.position;   // lost him: only the last place he was seen, never his real position
            aimPt = Vector3.Lerp(aimPt, tgt, Time.deltaTime * trackSpeed);
        }
        else
        {
            if (Time.time > nextWay || (aimPt - wayPt).sqrMagnitude < 4f) PickWaypoint(mz);
            aimPt = Vector3.MoveTowards(aimPt, wayPt, Time.deltaTime * scanSpeed);
        }
        aimPt.y = Mathf.Lerp(aimPt.y, locked ? player.position.y : wayPt.y, Time.deltaTime * 3f);
        var wantRot = Quaternion.LookRotation(aimPt - light.transform.position);
        light.transform.rotation = Quaternion.RotateTowards(light.transform.rotation, wantRot, (locked ? 2.5f : 6f) * Time.deltaTime);
        var bt = light.transform.Find("Beam");
        if (bt) { float len = BeamHit(light.transform.position, light.transform.forward);
            beamLen = Mathf.Lerp(beamLen <= 0 ? len : beamLen, len, Time.deltaTime * 8f); bt.localScale = new Vector3(bt.localScale.x, bt.localScale.y, beamLen); }
        var dir = light.transform.forward; dir.y = 0; if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * turnSpeed * 2f);
        inBeam = lit ? inBeam + Time.deltaTime : (locked ? inBeam : 0f);
        fireInterval = locked ? lockedFireInterval : 1.4f;
        redT = Mathf.MoveTowards(redT, locked ? 1f : 0f, Time.deltaTime * (lit ? 10f : 2f));
        var red = new Color(1f, 0.08f, 0.05f);
        if (spot) spot.color = Color.Lerp(baseCol, red, redT);
        if (beamR) { var bc = Color.Lerp(beamBase, new Color(1f, 0.1f, 0.05f, beamBase.a), redT); beamR.material.color = bc; if (beamR.material.HasProperty("_EmissionColor")) beamR.material.SetColor("_EmissionColor", bc * 2f); }
        if (locked && lostT == 0 && inBeam > spotTime && Time.time > nextShot) Fire();
        flash.intensity = Mathf.MoveTowards(flash.intensity, 0, Time.deltaTime * 200f);
        if (tracerT > 0) { tracerT -= Time.deltaTime; if (tracerT <= 0) tracer.enabled = false; }
        screenFlash = Mathf.MoveTowards(screenFlash, 0, Time.deltaTime * 2f);
    }
    // true if a straight line from the muzzle reaches the player (head, centre or legs) before any solid thing
    readonly RaycastHit[] rayBuf = new RaycastHit[32];
    bool SeesPlayer(Vector3 mz)
    {
        if (CrateSpawn.Hidden) return false;   // hiding inside a crate
        foreach (var off in new[] { 0.55f, 0f, -0.55f })
        {
            var pt = player.position + Vector3.up * off; var d = pt - mz; float dist = d.magnitude; if (dist < 0.1f) return true;
            int n = Physics.RaycastNonAlloc(mz, d / dist, rayBuf, dist + 0.2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool isPlayer = false;
            for (int i = 0; i < n; i++)
            {
                var h = rayBuf[i]; var c = h.collider;
                if (c.transform.IsChildOf(player)) { if (h.distance < best) { best = h.distance; isPlayer = true; } continue; }
                if (c.transform.IsChildOf(transform) || (light && c.transform.IsChildOf(light.transform))) continue;   // the tower itself
                var r = c.GetComponent<Renderer>(); if (r == null || !r.enabled) continue;   // invisible helper colliders don't block
                if (h.distance < best) { best = h.distance; isPlayer = false; }
            }
            if (isPlayer) return true;
        }
        return false;
    }
    float BeamHit(Vector3 o, Vector3 d)
    {
        var hs = Physics.RaycastAll(o, d, 250f, ~0, QueryTriggerInteraction.Ignore); float best = 250f;
        foreach (var h in hs)
        {
            if (h.distance < 4f || h.distance >= best) continue;
            var r = h.collider.GetComponent<Renderer>(); if (!r || !r.enabled) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;
            best = h.distance;
        }
        return best;
    }
    bool InQuarter(Vector3 p)
    {
        var c = new Vector3(-1.28f, 0, -1.33f);
        return Mathf.Sign(p.x - c.x) == Mathf.Sign(transform.position.x - c.x) && Mathf.Sign(p.z - c.z) == Mathf.Sign(transform.position.z - c.z);
    }
    void PickWaypoint(Vector3 mz)
    {
        for (int i = 0; i < 12; i++)
        {
            var c = new Vector3(-1.28f, 0, -1.33f);
            float sx = Mathf.Sign(transform.position.x - c.x), sz = Mathf.Sign(transform.position.z - c.z);
            var p = c + new Vector3(sx * Random.Range(0f, scanRadius), 0, sz * Random.Range(0f, scanRadius));
            
            if (!InQuarter(p)) continue;
            if (avoidLast && (new Vector3(p.x - lastSeen.x, 0, p.z - lastSeen.z)).sqrMagnitude < 400f) continue;
            RaycastHit h; if (!Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out h, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (h.point.y > 1.5f) continue; // ground only, not roofs
            var d = h.point - mz; if (Physics.Raycast(mz + d.normalized * 2f, d.normalized, d.magnitude - 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            wayPt = h.point; nextWay = Time.time + Random.Range(6f, 10f); avoidLast = false; return;
        }
        nextWay = Time.time + 1f;
    }
    void LateUpdate()
    {
        if (light) light.transform.position = Muzzle();
    }
    void Fire()
    {
        nextShot = Time.time + fireInterval; var m = Muzzle();
        flash.transform.position = m; flash.intensity = 12f;
        if (!SeesPlayer(m)) return;   // something is in the way: no shot, no damage
        var target = player.position + Vector3.up * 0.6f + Random.insideUnitSphere * 0.6f;
        // no tracer: the red beam is the shot line
        if (player == localPlayer) screenFlash = 0.6f;
        var gt = player.GetComponent<GazeTarget>(); if (gt) gt.Hit(20f);
    }
    void OnGUI()
    {
        if (screenFlash <= 0) return; var c = GUI.color; GUI.color = new Color(0.8f, 0f, 0f, screenFlash * 0.5f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = c;
    }
}
