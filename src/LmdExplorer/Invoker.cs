using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace LmdExplorer;

/// Commande « call <Type> <Méthode> [args…] » : appelle une méthode du jeu par réflexion,
/// pour tester le rôle d'une méthode obfusquée sans recompiler.
/// Type = nom complet interop (ex. Il2CppMegagon.Downhill.MountainManager).
/// Méthode d'instance : instance prise via Manager<T>.prop_T_0, sinon FindObjectOfType.
/// Args : entier, flottant (avec .), true/false, sinon chaîne.
internal static class Invoker
{
    public static void Call(string spec)
    {
        string[] p = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Type type = typeof(Il2CppMegagon.Downhill.PlayManager).Assembly.GetType(p[0])
                    ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(p[0])).FirstOrDefault(t => t != null)
                    ?? throw new ArgumentException($"type introuvable : {p[0]}");
        object[] args = p.Skip(2).Select(Parse).ToArray();
        MethodInfo m = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .FirstOrDefault(x => x.Name == p[1] && x.GetParameters().Length == args.Length)
            ?? throw new ArgumentException($"méthode introuvable : {p[1]}/{args.Length}");
        object target = m.IsStatic ? null : Instance(type);
        object ret = m.Invoke(target, args);
        Out.Info($"call {type.Name}.{m.Name} -> {Describe(ret)}");
    }

    private static object Instance(Type type)
    {
        for (Type t = type; t != null; t = t.BaseType)
        {
            var prop = t.GetProperty("prop_T_0", BindingFlags.Public | BindingFlags.Static);
            if (prop != null) return prop.GetValue(null);
        }
        var found = UnityEngine.Object.FindObjectOfType(Il2CppType.From(type));
        return found == null ? throw new InvalidOperationException("aucune instance")
            : typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("Cast")!.MakeGenericMethod(type).Invoke(found, null);
    }

    private static object Parse(string s)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
        if (s.Contains('.') && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
        if (bool.TryParse(s, out bool b)) return b;
        return s;
    }

    private static string Describe(object o) => o switch
    {
        null => "null",
        UnityEngine.Object u => $"{u.GetIl2CppType().Name}:{u.name}",
        _ => o.ToString(),
    };
}
