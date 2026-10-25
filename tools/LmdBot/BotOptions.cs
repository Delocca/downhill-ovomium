using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LmdMulti.Core.Net;
using LmdMulti.Core.Poses;

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
  --count N         simule N joueurs (ports N, N+1…, pseudos NOM-1, NOM-2…) : orbites de rayons
                    croissants, ou rejeux décalés de 1,5 s chacun le long de la piste
  --duration S      s'arrête après S secondes (défaut : Ctrl+C)
  --loss P          jette volontairement cette part des paquets émis (0 à 1) : éprouve les renvois
Course :
  --race join       accepte les invitations, charge --race-load s, roule --race-time s (+4 s par bot)
  --race host       lance une course --race-delay s après avoir vu un vrai joueur (une seule fois)
  --race-track ID   piste de la course hôte (défaut : celle du joueur, sinon CA_01_A2)
  --race-time S     temps de course simulé (défaut 25)
  --race-load S     durée du « chargement » avant d'être prêt (défaut 3)
  --race-delay S    hôte : attente avant l'invitation (défaut 3)
  --race-decline    refuse les invitations
  --race-dnf        abandonne à mi-course";

    public int Port = 47710, TargetPort = 47700, Rate = 25, Count = 1;
    public int Index; // rang du bot simulé (0 = le seul ou le premier, qui affiche le rapport détaillé)
    public List<string> Peers = new();
    public bool Broadcast = true;
    public string Name = "LmdBot", Track, Replay;
    public float Radius = 4f;
    public double DurationS = double.PositiveInfinity, Loss;
    public string RaceMode, RaceTrack;
    public double RaceTimeS = 25, RaceLoadS = 3, RaceDelayS = 3;
    public bool RaceDecline, RaceDnf;

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
                case "--count": o.Count = Math.Clamp(int.Parse(Next()), 1, 64); break;
                case "--duration": o.DurationS = Num(Next()); break;
                case "--loss": o.Loss = Math.Clamp(Num(Next()), 0, 0.9); break;
                case "--race": o.RaceMode = Next() is "join" or "host" ? args[i] : throw new ArgumentException("--race join|host"); break;
                case "--race-track": o.RaceTrack = Next(); break;
                case "--race-time": o.RaceTimeS = Num(Next()); break;
                case "--race-load": o.RaceLoadS = Num(Next()); break;
                case "--race-delay": o.RaceDelayS = Num(Next()); break;
                case "--race-decline": o.RaceDecline = true; break;
                case "--race-dnf": o.RaceDnf = true; break;
                case "-h": case "--help": case "help": return null;
                default: throw new ArgumentException($"option inconnue : {args[i]}");
            }
        }
        return o;
    }

    /// Options du i-ème bot simulé (--count) : port, pseudo et rayon propres.
    public BotOptions ForInstance(int i)
    {
        var b = (BotOptions)MemberwiseClone();
        b.Index = i;
        if (Count == 1) return b;
        b.Port = Port + i;
        b.Name = $"{Name}-{i + 1}";
        b.Radius = Radius + 2.5f * i;
        return b;
    }

    public NodeOptions ToNodeOptions() => new()
    {
        Port = Port, DiscoveryPort = TargetPort, Broadcast = Broadcast, ModVersion = "bot", SimulatedLoss = Loss,
        ManualPeers = Endpoints.ParsePeers(string.Join(",", Peers), TargetPort, Console.Error.WriteLine),
    };

    private static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    /// « latest » : le .lmdrec utilisable (≥ 2 s) le plus récent du dossier d'enregistrements du jeu.
    /// Celui que le jeu est en train d'écrire est souvent encore vide (écriture par blocs) : on le saute.
    public static string ResolveReplay(string replay)
    {
        if (replay != "latest") return replay;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/Steam/steamapps/common/Lonely Mountains - Downhill/UserData/LmdMulti/recordings");
        var files = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("*.lmdrec").OrderByDescending(f => f.LastWriteTimeUtc) : Enumerable.Empty<FileInfo>();
        foreach (var f in files)
        {
            try
            {
                var (_, frames) = PoseRecorder.Read(f.FullName);
                if (frames.Count >= 2 && frames[^1].TimeMs - frames[0].TimeMs >= 2000) return f.FullName;
            }
            catch (InvalidDataException) { } // autre version du protocole, fichier abîmé
        }
        throw new FileNotFoundException($"aucun .lmdrec utilisable dans {dir}");
    }
}
