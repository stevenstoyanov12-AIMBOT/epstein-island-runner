using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Late-join world sync by state, not by shots. Every breakable keeps a short state string (how many hits, which
// pieces are gone, cracks...). When the local player's shot (or action) changes it, that one client sends the new
// state ("ws"); the room keeps only the latest per object and hands the whole list to anyone who joins later,
// who snaps each object straight to its current look (no debris flying, no effects).
// Players already in the room never get "ws": they replayed the shot themselves.
public interface IWorldState
{
    string Save();
    void Load(string state);
}

public static class WorldState
{
    // true while SimpleGun applies the LOCAL player's shot: only the shooter reports the result
    public static bool Local;

    static readonly Dictionary<string, IWorldState> objects = new Dictionary<string, IWorldState>();
    static readonly Dictionary<string, string> waiting = new Dictionary<string, string>();   // states that arrived before their object was ready

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Local = false; objects.Clear(); waiting.Clear(); }

    // call once the object is fully built (end of Start); key = Net.PathOf(transform) taken in Awake
    public static void Register(string key, IWorldState o)
    {
        if (string.IsNullOrEmpty(key)) return;
        key = Id(key, o);
        objects[key] = o;
        if (waiting.TryGetValue(key, out var s)) { waiting.Remove(key); Load(o, s); }
    }

    // the object's state changed; sent only for the local player's own shot, or when forced (local action / host physics)
    public static void Changed(string key, IWorldState o, bool force = false)
    {
        if ((!Local && !force) || string.IsNullOrEmpty(key) || Net.I == null || !Net.I.Online) return;
        Net.I.SendRaw(new Net.Msg { t = "ws", c = Id(key, o), s = o.Save() });
    }

    // scene path + component type, so two breakable scripts on one object never share a state
    static string Id(string key, IWorldState o) => key + "#" + o.GetType().Name;

    // from the room's snapshot on join
    public static void Apply(string key, string state)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (objects.TryGetValue(key, out var o) && !(o is UnityEngine.Object u && u == null)) Load(o, state);
        else waiting[key] = state;
    }

    static void Load(IWorldState o, string state)
    {
        try { o.Load(state); }
        catch (System.Exception e) { Debug.LogWarning("[WorldState] bad state for " + o + ": " + e.Message); }
    }

    // ---- small helpers for the state strings ----
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string F(float v) => v.ToString("0.###", Inv);
    public static float PF(string s) => float.Parse(s, Inv);
    public static int PI(string s) => int.Parse(s, Inv);
    public static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    public static Vector3 PV(string s) { var p = s.Split(','); return new Vector3(PF(p[0]), PF(p[1]), PF(p[2])); }
    public static string Q(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
    public static Quaternion PQ(string s) { var p = s.Split(','); return new Quaternion(PF(p[0]), PF(p[1]), PF(p[2]), PF(p[3])); }
    public static string Ints(IEnumerable<int> xs) => string.Join(",", xs);
    public static List<int> PInts(string s)
    {
        var r = new List<int>(); if (string.IsNullOrEmpty(s)) return r;
        foreach (var x in s.Split(',')) if (x.Length > 0) r.Add(PI(x));
        return r;
    }
}
