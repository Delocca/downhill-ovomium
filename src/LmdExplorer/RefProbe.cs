using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LmdExplorer;

/// Commande « refs [Type] » : liste les références Unity (Transform, GameObject, composants) portées par
/// les champs d'un composant du jeu (défaut : BikeLocomotion et PlayerCameraTarget du vélo local), avec
/// leur état : nulle, détruite (scène déchargée) ou vivante (nom, scène). Sert à trouver une référence
/// périmée après un changement de scène.
internal static class RefProbe
{
    public static void Run(string typeName)
    {
        string[] types = typeName != null ? new[] { typeName }
            : new[] { "Il2CppMegagon.Downhill.Vehicle.Controller.BikeLocomotion", "Il2CppMegagon.Downhill.Players.PlayerCameraTarget" };
        foreach (var name in types) Out.Try($"refs {name}", () => Dump(name));
    }

    private static void Dump(string name)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null)
                    ?? throw new ArgumentException($"type introuvable : {name}");
        var all = Object.FindObjectsOfType(Il2CppType.From(type));
        Out.Info($"refs {type.Name} : {all.Length} instance(s)");
        foreach (var o in all)
        {
            object inst = typeof(Il2CppObjectBase).GetMethod("Cast")!.MakeGenericMethod(type).Invoke(o, null);
            var sb = new StringBuilder($"  {o.name} :");
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var t = p.PropertyType;
                bool unity = typeof(Object).IsAssignableFrom(t);
                if (!(unity || t.IsPrimitive || t.IsEnum) || p.GetIndexParameters().Length > 0 || p.DeclaringType == typeof(Object)) continue;
                object v;
                try { v = p.GetValue(inst); }
                catch (Exception e) { sb.Append($"\n    {p.Name} = <{e.GetType().Name}>"); continue; }
                sb.Append($"\n    {p.Name} ({t.Name}) = {(unity ? Describe(v as Object, v != null) : v)}");
            }
            Out.Info(sb.ToString());
        }
    }

    private static string Describe(Object u, bool managedNotNull)
    {
        if (!managedNotNull) return "null";
        if (u == null) return "DÉTRUITE (référence périmée)";
        string scene = u is Component c ? c.gameObject.scene.name : u is GameObject g ? g.scene.name : "-";
        return $"{u.name} [scène {scene}]";
    }
}
