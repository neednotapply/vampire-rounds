using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace VampireRounds.Vs
{
    /// <summary>
    /// Developer console (only with VAMPIREROUNDS_DEV set, never for players): reads commands from
    /// %LOCALAPPDATA%\VampireRounds\dev\cmd.txt and writes results to out.txt, to look around VS while it runs.
    /// pages | objs &lt;Type&gt; | call &lt;Type&gt; &lt;Method&gt; [args] | get &lt;Type&gt; &lt;Member&gt; | sget &lt;Type&gt; &lt;Member&gt; | scall &lt;Type&gt; &lt;Method&gt; [args]
    /// Type names are the interop .NET names (e.g. Il2CppVampireSurvivors.UI.MainMenuPage).
    /// </summary>
    public static class DevConsole
    {
        public static readonly bool On = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV"));
        private static string Dir => Path.Combine(Paths.DataDir(), "dev");
        private static float next;

        public static void Tick()
        {
            if (!On || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            string cmd = Path.Combine(Dir, "cmd.txt");
            if (!File.Exists(cmd)) return;
            string[] lines;
            try { lines = File.ReadAllLines(cmd); File.Delete(cmd); } catch { return; }
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                sb.AppendLine("> " + line);
                try { sb.AppendLine(Run(line.Trim())); }
                catch (Exception e) { sb.AppendLine("ERROR " + (e.InnerException ?? e)); }
            }
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "out.txt"), sb.ToString());
        }

        private static Type T(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(name, false);
                if (t != null) return t;
            }
            throw new Exception("no type " + name);
        }

        private static object[] Objs(Type t)
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.From(t), true);
            return found.Select(o => Activator.CreateInstance(t, o.Pointer)).ToArray();
        }

        private static object Arg(string s, Type want)
        {
            if (want == typeof(string)) return s;
            if (want == typeof(int)) return int.Parse(s);
            if (want == typeof(float)) return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            if (want == typeof(bool)) return bool.Parse(s);
            if (want.IsEnum) return Enum.Parse(want, s);
            return null;
        }

        private static object Invoke(Type t, object target, string name, string[] args)
        {
            var m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault(x => x.Name == name && x.GetParameters().Length >= args.Length
                && x.GetParameters().Skip(args.Length).All(p => p.IsOptional));
            if (m == null) throw new Exception("no method " + name);
            var ps = m.GetParameters();
            var vals = ps.Select((p, i) => i < args.Length ? Arg(args[i], p.ParameterType) : p.DefaultValue).ToArray();
            return m.Invoke(target, vals);
        }

        private static string Show(object o)
        {
            if (o == null) return "null";
            if (o is Il2CppObjectBase b)
            {
                string s = o.GetType().Name;
                if (o is UnityEngine.Object u) s += " '" + u.name + "'";
                return s;
            }
            return o.ToString();
        }

        private static string Run(string line)
        {
            var p = line.Split(' ');
            switch (p[0])
            {
                case "pages":
                {
                    var sb = new StringBuilder();
                    foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                    {
                        if (mb == null || !mb.isActiveAndEnabled) continue;
                        string n = mb.GetIl2CppType().FullName;
                        if (n.EndsWith("Page") || n.EndsWith("State") || n.Contains("Menu") || n.EndsWith("Machine")) sb.AppendLine(n + " on '" + mb.gameObject.name + "'");
                    }
                    return sb.ToString();
                }
                case "objs":
                    return string.Join("\n", Objs(T(p[1])).Select(Show));
                case "call":
                {
                    var t = T(p[1]);
                    var o = Objs(t).FirstOrDefault() ?? throw new Exception("no object of " + p[1]);
                    return Show(Invoke(t, o, p[2], p.Skip(3).ToArray()));
                }
                case "scall":
                    return Show(Invoke(T(p[1]), null, p[2], p.Skip(3).ToArray()));
                case "get":
                {
                    var t = T(p[1]);
                    var o = Objs(t).FirstOrDefault() ?? throw new Exception("no object of " + p[1]);
                    var pr = t.GetProperty(p[2]);
                    return Show(pr.GetValue(o));
                }
                case "sget":
                    return Show(T(p[1]).GetProperty(p[2], BindingFlags.Public | BindingFlags.Static).GetValue(null));
                case "methods":
                    return string.Join("\n", T(p[1]).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Where(m => !m.IsSpecialName).Select(m => m.Name + "(" + string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")"));
                case "tree":
                {
                    // tree <Type>: the first live object's renderers below its root (sprite, texture, enabled, order)
                    var t = T(p[1]);
                    var o = Objs(t).FirstOrDefault() as Component ?? throw new Exception("no object of " + p[1]);
                    var sb = new StringBuilder();
                    foreach (var r in o.GetComponentsInChildren<SpriteRenderer>(true))
                        sb.AppendLine(r.gameObject.name + " enabled=" + r.enabled + " active=" + r.gameObject.activeInHierarchy + " sprite=" + (r.sprite != null ? r.sprite.name + " tex=" + r.sprite.texture.name : "-")
                            + " layer=" + r.sortingLayerName + "/" + r.sortingOrder + " pos=" + r.transform.position + " scale=" + r.transform.lossyScale);
                    return sb.ToString();
                }
                case "active":
                {
                    // active <Type>: every object of the type, active or not, with its path
                    var t = T(p[1]);
                    var sb = new StringBuilder();
                    foreach (var o in Objs(t))
                    {
                        var c = o as Component;
                        if (c == null) continue;
                        string path = c.name;
                        for (var tr = c.transform.parent; tr != null; tr = tr.parent) path = tr.name + "/" + path;
                        sb.AppendLine(path + " active=" + c.gameObject.activeInHierarchy);
                    }
                    return sb.ToString();
                }
                case "images":
                {
                    // images [minSize]: active UI Images at least this many pixels wide, with their sprite and colour
                    float min = p.Length > 1 ? float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 600f;
                    var sb = new StringBuilder();
                    foreach (var im in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>())
                    {
                        if (im == null || !im.isActiveAndEnabled) continue;
                        var rt = im.rectTransform;
                        if (rt.rect.width < min) continue;
                        string path = im.name;
                        for (var tr = im.transform.parent; tr != null; tr = tr.parent) path = tr.name + "/" + path;
                        sb.AppendLine(path + " sprite=" + (im.sprite != null ? im.sprite.name : "-") + " color=" + im.color + " size=" + rt.rect.size);
                    }
                    return sb.ToString();
                }
                case "big":
                {
                    // big [minHeight]: every enabled SpriteRenderer at least this tall (VS units)
                    float min = p.Length > 1 ? float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                    var sb = new StringBuilder();
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
                    {
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.bounds.size.y < min) continue;
                        string path = r.name;
                        for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                        sb.AppendLine(path + " sprite=" + (r.sprite != null ? r.sprite.name : "-") + " size=" + r.bounds.size + " at " + r.bounds.center + " order=" + r.sortingLayerName + "/" + r.sortingOrder);
                    }
                    return sb.ToString();
                }
                case "sorting":
                {
                    // sorting [radius]: how VS sorts sprites (camera and project settings), and every enabled SpriteRenderer
                    // within radius of player 1 with its sorting layer, order, sort point and z
                    float rad = p.Length > 1 ? float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 2f;
                    var sb = new StringBuilder();
                    var cam = Camera.main;
                    sb.AppendLine("graphics: " + UnityEngine.Rendering.GraphicsSettings.transparencySortMode + " axis " + UnityEngine.Rendering.GraphicsSettings.transparencySortAxis
                        + " | camera: " + (cam != null ? cam.transparencySortMode + " axis " + cam.transparencySortAxis + " z " + cam.transform.position.z : "none"));
                    var c = Session.SeatChar[0] ?? Session.SeatChar[1];
                    Vector3 at = c != null ? c.transform.position : (cam != null ? cam.transform.position : Vector3.zero);
                    sb.AppendLine("player at " + at);
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                        if (((Vector2)(r.bounds.center - at)).magnitude > rad && ((Vector2)(r.transform.position - at)).magnitude > rad) continue;
                        string path = r.name;
                        for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                        var sr = r as SpriteRenderer;
                        sb.AppendLine(path + " [" + r.GetIl2CppType().Name + "] layer=" + r.sortingLayerName + "(" + r.sortingLayerID + ")/" + r.sortingOrder
                            + (sr != null ? " sortPoint=" + sr.spriteSortPoint + " sprite=" + (sr.sprite != null ? sr.sprite.name : "-") : "")
                            + " pos=" + r.transform.position.ToString("F3") + " bounds=" + r.bounds.center.ToString("F2") + "/" + r.bounds.size.ToString("F2")
                            + " goLayer=" + r.gameObject.layer + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "-"));
                    }
                    var sgs = UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.SortingGroup>();
                    sb.AppendLine(sgs.Length + " sorting groups");
                    return sb.ToString();
                }
                case "ui":
                {
                    // ui <text>: active UI Images whose path contains the text, with their sprite's pixels and units
                    var sb = new StringBuilder();
                    foreach (var im in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>())
                    {
                        if (im == null || !im.isActiveAndEnabled) continue;
                        string path = im.name;
                        for (var tr = im.transform.parent; tr != null; tr = tr.parent) path = tr.name + "/" + path;
                        if (p.Length > 1 && path.IndexOf(p[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var s = im.sprite;
                        var rt = im.rectTransform;
                        sb.AppendLine(path + " sprite=" + (s != null ? s.name + " tex=" + (s.texture != null ? s.texture.name : "-") + " rect=" + s.rect + " ppu=" + s.pixelsPerUnit + " pivot=" + s.pivot : "-")
                            + " size=" + rt.rect.size + " scale=" + rt.lossyScale.ToString("F3") + " preserveAspect=" + im.preserveAspect + " type=" + im.type
                            + " canvasRefPpu=" + (im.canvas != null ? im.canvas.referencePixelsPerUnit.ToString() : "-") + " anchors=" + rt.anchorMin + "-" + rt.anchorMax);
                    }
                    return sb.ToString();
                }
                default:
                    return "unknown command";
            }
        }
    }
}
