namespace LmdMulti.Core.Protocol;

/// Constantes du protocole réseau. Changer le format d'un message => incrémenter Version.
public static class ProtocolInfo
{
    /// Version du format binaire : un pair d'une autre version est signalé, jamais décodé.
    public const byte Version = 1;

    public const byte Magic0 = (byte)'L', Magic1 = (byte)'M';

    public const int DefaultPort = 47700;

    /// magic(2) + version(1) + type(1) + id d'instance de l'émetteur(8).
    public const int HeaderSize = 12;

    /// Reste sous la MTU (1280 octets en IPv6/Tailscale) : pas de fragmentation IP.
    public const int MaxPacketSize = 1200;

    public const int MaxNameBytes = 48;
}

public enum MessageType : byte
{
    Announce = 1, // qui je suis, où j'en suis (~1 Hz, broadcast + unicast)
    Pose = 2,     // pose du vélo + rider (20-30 Hz, unicast)
    Ping = 3,     // mesure du temps aller-retour
    Pong = 4,
    Bye = 5,      // départ propre
}

/// Squelette transmis : os `<nom>_BindJNT` sous `bike/Bind_JNT` (vélo et ragdoll ont les mêmes noms).
public static class Skeleton
{
    public const string Suffix = "_BindJNT";

    /// Ordre fixe = ordre sur le fil. Ne pas réordonner sans changer ProtocolInfo.Version.
    public static readonly string[] Bones =
    {
        "root", "cog", "back_suspension", "wheel_back_axis", "wheel_back_spin",
        "handlebar", "wheel_front_axis", "wheel_front_spin", "pedal",
        "spine_1", "hip_L", "knee_L", "ankle_L", "toe_L", "hip_R", "knee_R", "ankle_R", "toe_R",
        "spine_2", "spine_3", "neck", "head", "head_tip",
        "shoulder_L", "elbow_L", "hand_L", "finger_L", "shoulder_R", "elbow_R", "hand_R", "finger_R",
    };

    public static int Count => Bones.Length;

    /// Os dont on envoie aussi la position locale (ceux qui translatent : suspensions, centre du rider…).
    /// Masque de bits libre côté émetteur : le récepteur applique ce qu'il reçoit.
    /// Relevé en jeu (batchmode, vélo à l'arrêt) : cog, spine_1, hand_L/R translatent (IK des mains) ;
    /// suspensions et roues ajoutées par prudence, à confirmer en roulant (DebugLog).
    public static readonly uint DefaultPositionMask = MaskOf(
        "root", "cog", "back_suspension", "wheel_back_axis", "handlebar", "wheel_front_axis", "pedal", "spine_1",
        "hand_L", "hand_R");

    public static int IndexOf(string bone) => System.Array.IndexOf(Bones, bone);

    public static uint MaskOf(params string[] bones)
    {
        uint m = 0;
        foreach (var b in bones) m |= 1u << IndexOf(b);
        return m;
    }
}
