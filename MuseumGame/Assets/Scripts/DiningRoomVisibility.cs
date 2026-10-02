using UnityEngine;

// Shows the Hallwyl dining room only while the camera is up in the vestibule/dining zone, so it is never visible from the street.
public class DiningRoomVisibility : MonoBehaviour
{
    public Vector3 zoneMin = new Vector3(17.5f, 2.2f, 0.5f);
    public Vector3 zoneMax = new Vector3(29.3f, 6.5f, 7.0f);
    // Renderers hidden while the camera is inside the dining room proper (they show through scan cracks as black).
    public Renderer[] hideWhenInside;
    public Vector3 innerMin = new Vector3(23.8f, 2.2f, 2.0f);
    public Vector3 innerMax = new Vector3(29.3f, 6.2f, 6.2f);
    MeshRenderer mr;
    // the museum shell lives in the Outside scene and this in the streamed-in Inside scene: re-link by name
    public string[] hideWhenInsidePaths = { "Museum/MuseumBuilding_Root/Plane.065_atlas.004_0", "Museum/MuseumBuilding_Root/Plane.065_atlas.005_0" };
    void Awake() { mr = GetComponent<MeshRenderer>(); Relink(); }
    void Relink()
    {
        if (hideWhenInsidePaths == null || hideWhenInsidePaths.Length == 0) return;
        if (hideWhenInside == null || hideWhenInside.Length != hideWhenInsidePaths.Length) hideWhenInside = new Renderer[hideWhenInsidePaths.Length];
        for (int i = 0; i < hideWhenInsidePaths.Length; i++)
            if (hideWhenInside[i] == null) { var g = GameObject.Find(hideWhenInsidePaths[i]); if (g) hideWhenInside[i] = g.GetComponent<Renderer>(); }
    }
    void LateUpdate()
    {
        var cam = Camera.main; if (cam == null || mr == null) return;
        var p = cam.transform.position;
        bool inside = p.x > zoneMin.x && p.x < zoneMax.x && p.y > zoneMin.y && p.y < zoneMax.y && p.z > zoneMin.z && p.z < zoneMax.z;
        if (mr.enabled != inside) mr.enabled = inside;
        if (hideWhenInside != null && hideWhenInside.Length > 0)
        {
            bool inner = p.x > innerMin.x && p.x < innerMax.x && p.y > innerMin.y && p.y < innerMax.y && p.z > innerMin.z && p.z < innerMax.z;
            for (int i = 0; i < hideWhenInside.Length; i++) { var r = hideWhenInside[i]; if (r != null && r.enabled == inner) r.enabled = !inner; }
        }
    }
}
