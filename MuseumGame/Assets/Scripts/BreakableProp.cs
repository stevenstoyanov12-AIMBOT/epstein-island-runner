using UnityEngine;

// Display cases & desks: shots 1-3 chip/crack progressively, shot 4 breaks apart into pre-fractured pieces.
public class BreakableProp : MonoBehaviour
{
    public GameObject fracturedPrefab; public int breakAt = 4; public Color chipColor = new Color(0.3f,0.1f,0.06f);
    int hits;
    void OnBulletHit(RaycastHit hit)
    {
        hits++;
        if (hits < breakAt)
        {
            SimpleGun.Dust(hit.point, hit.normal, 30 + hits * 25, 1.5f + hits, chipColor);          // chips / glass flying
            SimpleGun.Dust(hit.point, hit.normal, 20 * hits, 3f, new Color(0.85f,0.92f,1f,0.9f));   // glass shards
            transform.localScale *= 0.995f;                                                          // slight sag
            return;
        }
        if (hits > breakAt) return;
        var r = GetComponent<Renderer>(); var c = r ? r.bounds.center : transform.position;
        SimpleGun.Dust(c, Vector3.up, 250, 4f, new Color(0.85f,0.92f,1f,0.9f));
        if (fracturedPrefab)
        {
            var f = Instantiate(fracturedPrefab, transform.position, transform.rotation); f.transform.localScale = transform.lossyScale;
            foreach (var mr in f.GetComponentsInChildren<MeshRenderer>())
            {
                if (r) mr.sharedMaterials = r.sharedMaterials;
                var mc = mr.gameObject.AddComponent<MeshCollider>(); mc.convex = true;
                var rb = mr.gameObject.AddComponent<Rigidbody>(); rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.mass = 3f;
                rb.AddExplosionForce(25f, hit.point - hit.normal * 0.3f, 3f, 0.3f, ForceMode.Impulse);
            }
            Destroy(f, 20f);
        }
        gameObject.SetActive(false);
    }
}
