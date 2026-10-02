using UnityEngine;
using System.Collections;
// Editor-test helper: poses the demo character in each state, captures a strip image, and aligns the gun in the rifle pose.
public class HeistCapture : MonoBehaviour
{
    IEnumerator Start()
    {
        var root = GameObject.Find("HeistDemo"); var m = root.transform.GetChild(0).gameObject; var an = m.GetComponent<Animator>();
        var demo = root.GetComponent("HeistCharacterDemo") as Behaviour; demo.enabled = false; m.transform.localRotation = Quaternion.identity;
        var gun = new GameObject("dummy");
        Transform wr = null, wl = null; foreach (var tr in m.GetComponentsInChildren<Transform>()) { if (tr.name == "wrist.R") wr = tr; if (tr.name == "wrist.L") wl = tr; }
        string[] st = { "ShootPistol", "ShootPistol", "Locomotion", "Locomotion", "Locomotion" }; float[] sp = { 0, 0, 2, 6, 0 }; float[] nt = { 0.3f, 0.3f, 0.25f, 0.2f, 0.4f };
        int n = 5; var tex = new Texture2D(n * 340, 600, TextureFormat.RGB24, false);
        var cam = new GameObject("TmpCam").AddComponent<Camera>(); cam.fieldOfView = 40;
        var L = new GameObject("TmpL").AddComponent<Light>(); L.type = LightType.Point; L.range = 12; L.intensity = 25;
        var rt = new RenderTexture(340, 600, 24); var fwd = m.transform.forward; var side = Vector3.Cross(Vector3.up, fwd);
        for (int i = 0; i < n; i++)
        {
            an.SetFloat("Speed", sp[i]); an.SetBool("Shooting", i < 2); an.SetBool("Pistol", i == 1);
            an.Play(st[i], 0, nt[i]); an.speed = 0; gun.SetActive(i != 1);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            var pp = root.transform.position;
            cam.transform.position = pp + (i < 2 ? fwd * 2.6f + side * 1.6f + Vector3.up * 1.3f : side * 3.2f + Vector3.up * 1.1f); cam.transform.LookAt(pp + Vector3.up * 1.0f); L.transform.position = cam.transform.position + Vector3.up;
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 340, 600), i * 340, 0); RenderTexture.active = null;
            an.speed = 1;
        }
        tex.Apply(); System.IO.File.WriteAllBytes(Application.dataPath + "/Screenshots/heist_play.png", tex.EncodeToPNG());
        Destroy(cam.gameObject); Destroy(L.gameObject); gun.SetActive(true); demo.enabled = true;
    }
}
