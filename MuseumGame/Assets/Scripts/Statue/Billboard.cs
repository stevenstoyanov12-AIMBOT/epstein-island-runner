using UnityEngine;

// Keeps a quad (a light's glow halo) turned to face whichever camera is rendering.
public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        foreach (var c in Camera.allCameras)
            if (c.enabled && c.depth >= (cam != null ? cam.depth : float.MinValue)) cam = c;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
    }
}
