using UnityEngine;
// Ladder zone on a scaffold: inside it, W climbs up, S climbs down, gravity is off and the
// scaffold's decks are passable; step sideways/forward out of the zone to land on a deck.
public class ScaffoldLadder : MonoBehaviour
{
    public Vector3 size = new Vector3(0.9f, 7.2f, 1.2f);   // local zone size
    public Collider[] passThrough;
    public float topFeet = 5.78f;                           // world height of the top deck                           // scaffold colliders ignored while climbing
    CharacterController cc; FirstPersonController fpc; bool inside;
    void Start() { var p = GameObject.Find("Player"); if (p) { cc = p.GetComponent<CharacterController>(); fpc = p.GetComponent<FirstPersonController>(); } }
    void Update()
    {
        if (!cc) return;
        var l = transform.InverseTransformPoint(cc.transform.position);
        bool now = Mathf.Abs(l.x) < size.x * 0.5f && l.y > -0.5f && l.y < size.y && Mathf.Abs(l.z) < size.z * 0.5f;
        if (now != inside)
        {
            inside = now;
            if (now) FirstPersonController.ladderNear = this; else { if (FirstPersonController.ladderNear == this) FirstPersonController.ladderNear = null; if (FirstPersonController.ladder == this) FirstPersonController.ladder = null; }
        }
    }
    bool ignoring;
    void LateUpdate()
    {
        bool want = inside && FirstPersonController.ladder == this;
        if (cc && want != ignoring) { if (passThrough != null) foreach (var c in passThrough) if (c) Physics.IgnoreCollision(cc, c, want); ignoring = want; }
    }
    void OnGUI()
    {
        if (FirstPersonController.ladderNear != this || FirstPersonController.ladder != null) return;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; st.normal.textColor = Color.white;
        GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 40), "PRESS E TO CLIMB", st);
    }
    void OnDisable() { if (FirstPersonController.ladder == this) FirstPersonController.ladder = null; if (FirstPersonController.ladderNear == this) FirstPersonController.ladderNear = null; }
    void OnDrawGizmos() { Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireCube(Vector3.up * size.y * 0.5f, size); }
}
