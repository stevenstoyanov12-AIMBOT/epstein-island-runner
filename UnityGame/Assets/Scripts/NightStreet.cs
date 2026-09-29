using UnityEngine;

// A looping stretch of night street behind the van. The van stays put; the street slides away
// behind it (+Z, out of the rear doors) at `speed`, and segments that drift too far wrap round to
// the front. Lamp lights only switch on once they are clear of the van so they never light its inside.
public class NightStreet : MonoBehaviour
{
    public float speed;                 // m/s, driven by the cutscene
    public float segmentLength = 20f;
    public Transform[] segments;
    public Light[] lampLights;          // children of the segments
    public float lampClearance = 6f;    // lamps closer to the van than this stay dark

    // Show or hide the whole street (buildings, road, lamps and anything else under it).
    public void SetVisible(bool visible)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        foreach (var l in GetComponentsInChildren<Light>(true)) l.gameObject.SetActive(visible);
    }

    void Update()
    {
        float span = segmentLength * segments.Length;
        foreach (var s in segments)
        {
            var p = s.localPosition;
            p.z += speed * Time.deltaTime;
            if (p.z > span) p.z -= span;
            s.localPosition = p;
        }
        foreach (var l in lampLights)
            l.enabled = transform.InverseTransformPoint(l.transform.position).z > lampClearance;
    }
}
