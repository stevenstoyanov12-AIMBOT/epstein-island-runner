using UnityEngine;
// Hides the listed renderers (every frame, after other visibility scripts) while the camera/player is outside the facade (+X side).
[DefaultExecutionOrder(1000)]
public class HideWhenOutside : MonoBehaviour
{
    public Renderer[] targets; public float facadeX = 26.42f; Transform player;
    void Start()
    {
        var p = GameObject.Find("Player"); if (p) player = p.transform;
        foreach (var r in targets) if (r) foreach (var c in r.GetComponents<Collider>()) c.enabled = false;
    }
    void LateUpdate()
    {
        var cam = Camera.main; bool outside = (player && player.position.x > facadeX + 0.05f) || (cam && cam.transform.position.x > facadeX + 0.05f);
        foreach (var r in targets) if (r && outside) r.enabled = false;
    }
}
