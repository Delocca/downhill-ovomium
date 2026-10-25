using System.Text;
using UnityEngine;

namespace LmdExplorer;

/// Regroupe les sondes : ligne d'état périodique et dump complet vers fichiers.
internal static class Probes
{
    public static string StatusLine()
    {
        var sb = new StringBuilder();
        Out.Try("état", () => sb.Append($"state={TrackProbe.GameState()} "));
        var bike = BikeProbe.Find();
        if (bike != null) Out.Try("vélo", () => sb.Append(BikeProbe.Status(bike)).Append(' '));
        Out.Try("chrono", () => sb.Append(TrackProbe.TimerLine()));
        return sb.ToString();
    }

    public static void FullDump()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Out.Try("racines", () => Out.WriteFile($"roots-{scene}.txt", Hierarchy.SceneRoots()));
        Out.Try("piste", () => Out.WriteFile($"track-{scene}.txt", TrackProbe.Report()));
        var bike = BikeProbe.Find();
        if (bike == null) { Out.Info("dump : pas de vélo"); return; }
        Out.Try("vélo", () => Out.WriteFile("bike-report.txt", BikeProbe.Report(bike)));
        Out.Try("hiérarchie vélo", () => Out.WriteFile("bike-hierarchy.txt", Hierarchy.Dump(bike.transform.root)));
        var ragdoll = Hierarchy.FindRoot("RagDoll(Clone)");
        if (ragdoll != null)
            Out.Try("ragdoll", () => Out.WriteFile("ragdoll-hierarchy.txt", Hierarchy.Dump(ragdoll.transform)));
        var cam = GameObject.Find("PlayCamera(Clone)");
        Out.Info($"PlayCamera(Clone)={(cam != null ? cam.transform.position.ToString() : "absent")}");
    }
}
