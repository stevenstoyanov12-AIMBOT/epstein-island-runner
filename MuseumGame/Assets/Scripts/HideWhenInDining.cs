using UnityEngine;
// Hides the listed renderers while the camera is inside the dining room (which pokes out past the facade).
[DefaultExecutionOrder(1300)]
public class HideWhenInDining : MonoBehaviour
{
    public Renderer[] targets;
    public Bounds room = new Bounds(new Vector3(25.1f, 4.0f, 4.15f), new Vector3(7.0f, 3.8f, 4.4f));
    void LateUpdate()
    {
        var cam = Camera.main; if (!cam) return;
        bool show = !room.Contains(cam.transform.position);
        foreach (var r in targets) if (r && r.enabled != show) r.enabled = show;
    }
}
