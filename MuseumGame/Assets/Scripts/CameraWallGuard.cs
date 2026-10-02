using UnityEngine;
// Runs after every camera placement (controller, head-follow, aim lock): keeps the camera's near plane
// from ever reaching through a wall, so nothing in another room can be seen.
[DefaultExecutionOrder(1050)]
public class CameraWallGuard : MonoBehaviour
{
    public float radius = 0.12f;
    CharacterController cc;
    void Awake() { cc = GetComponent<CharacterController>(); }
    void LateUpdate()
    {
        var cam = Camera.main; if (!cam || !cc) return;
        if (SeamlessPortal.camPortal != null || FirstPersonController.ladder != null) return;         // portal handover places the camera on purpose
        var ct = cam.transform;
        Vector3 top = transform.position + cc.center + Vector3.up * (cc.height * 0.5f - cc.radius);
        Vector3 origin = new Vector3(transform.position.x, Mathf.Min(ct.position.y, top.y), transform.position.z);
        Vector3 d = ct.position - origin; float len = d.magnitude; if (len < 1e-4f) return;
        Vector3 dir = d / len;
        // thin ray first (works even when a sphere at the origin would already overlap nearby geometry)
        float safe = SafeRay(origin, dir, len + radius);
        float want = Mathf.Min(len, Mathf.Max(0f, safe - radius));
        var pos = origin + dir * want;
        // never leave the near plane overlapping geometry (corners, arches, columns)
        for (int i = 0; i < 6 && Overlaps(pos, 0.09f); i++) pos = Vector3.Lerp(pos, origin, 0.35f);
        ct.position = pos;
    }
    static bool Overlaps(Vector3 p, float r)
    {
        foreach (var c in Physics.OverlapSphere(p, r, ~0, QueryTriggerInteraction.Ignore)) { if (!pl) { var g = GameObject.Find("Player"); if (g) pl = g.transform; } if (pl && c.transform.IsChildOf(pl)) continue; return true; }
        return false;
    }
    static float SafeRay(Vector3 o, Vector3 dir, float len)
    {
        float best = len;
        foreach (var h in Physics.RaycastAll(o, dir, len, ~0, QueryTriggerInteraction.Ignore))
        { if (!pl) { var g = GameObject.Find("Player"); if (g) pl = g.transform; } if (pl && h.collider.transform.IsChildOf(pl)) continue; if (h.distance < best) best = h.distance; }
        return best;
    }
    static Transform pl;
    // nearest hit not belonging to the player
    public static float SafeDist(Vector3 o, Vector3 dir, float len, Transform self = null, float r = 0.1f)
    {
        float best = len;
        foreach (var h in Physics.SphereCastAll(o, r, dir, len, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.distance <= 0f) continue;
            if (!pl) { var g = GameObject.Find("Player"); if (g) pl = g.transform; } if (pl && h.collider.transform.IsChildOf(pl)) continue;
            if (h.distance < best) best = h.distance;
        }
        return best;
    }
}
