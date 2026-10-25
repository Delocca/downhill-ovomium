using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LmdMulti.Core.Net;

namespace LmdBot;

/// Options de la ligne de commande.
internal sealed class BotOptions
{
    public const string Usage = @"LmdBot : faux joueur LmdMulti pour tester le mod sans second PC.
  --port N          port d'écoute (défaut 47710 ; le jeu garde 47700)
  --target-port N   port des joueurs visé par les annonces (défaut 47700)
  --peer IP[:port]  joueur à contacter directement (répétable ; 127.0.0.1:47700 est toujours contacté)
  --no-broadcast    pas d'annonce broadcast (seulement --peer et la boucle locale)
  --name NOM        pseudo du bot (défaut LmdBot)
  --track ID        piste annoncée (défaut : celle du premier joueur vu, ou celle de l'enregistrement)
  --replay FICHIER  rejoue en boucle un enregistrement .lmdrec ; « latest » = le plus récent du jeu
  --radius M        mode orbite (sans --replay) : tourne autour du joueur à M mètres (défaut 4)
  --rate HZ         poses par seconde (défaut 25)
  --duration S      s'arrête après S secondes (défaut : Ctrl+C)";

    public int Port = 47710, TargetPort = 47700, Rate = 25;
    public List<string> Peers = new();
    public bool Broadcast = true;
    public string Name = "LmdBot", Track, Replay;
    public float Radius = 4f;
    public double DurationS = double.PositiveInfinity;

    public static BotOptions Parse(string[] args)
    {
        var o = new BotOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"valeur manquante après {args[i]}");
            switch (args[i])
            {
                case "--port": o.Port = int.Parse(Next()); break;
                case "--target-port": o.TargetPort = int.Parse(Next()); break;
                case "--peer": o.Peers.Add(Next()); break;
                case "--no-broadcast": o.Broadcast = false; break;
                case "--name": o.Name = Next(); break;
                case "--track": o.Track = Next(); break;
                case "--replay": o.Replay = Next(); break;
                case "--radius": o.Radius = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--rate": o.Rate = Math.Clamp(int.Parse(Next()), 1, 60); break;
                case "--duration": o.DurationS = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "-h": case "--help": case "help": return null;
                default: throw new ArgumentException($"option inconnue : {args[i]}");
            }
        }
        return o;
    }

    public NodeOptions ToNodeOptions() => new()
    {
        Port = Port, DiscoveryPort = TargetPort, Broadcast = Broadcast, ModVersion = "bot",
        ManualPeers = Endpoints.ParsePeers(string.Join(",", Peers), TargetPort, Console.Error.WriteLine),
    };

    /// « latest » : le .lmdrec le plus récent du dossier d'enregistrements du jeu.
    public static string ResolveReplay(string replay)
    {
        if (replay != "latest") return replay;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/Steam/steamapps/common/Lonely Mountains - Downhill/UserData/LmdMulti/recordings");
        var latest = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("*.lmdrec").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
        return latest?.FullName ?? throw new FileNotFoundException($"aucun .lmdrec dans {dir}");
    }
}
