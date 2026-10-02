using UnityEngine;
// Narrow scanned doorway (vestibule <-> dining room): the real opening is thinner than the player's capsule, so inside this box the capsule is made slim.
[DefaultExecutionOrder(1099)]
public class DoorSqueeze : MonoBehaviour
{
    public Vector3 center = new Vector3(22.4f, 3.4f, 3.05f);
    public Vector3 size = new Vector3(2.6f, 3.0f, 1.8f);
    Transform player;
    void LateUpdate()
    {
        if (!player) { var p = GameObject.Find("Player"); if (p) player = p.transform; else return; }
        var d = player.position - center;
        if (Mathf.Abs(d.x) < size.x * 0.5f && Mathf.Abs(d.y) < size.y * 0.5f && Mathf.Abs(d.z) < size.z * 0.5f) SeamlessPortal.RequestSqueeze();
    }
}
