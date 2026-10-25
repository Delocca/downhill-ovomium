using System;

namespace LmdMulti.Core.Net;

/// Décalage entre l'horloge d'un pair et la nôtre, estimé par ping/pong (à la NTP) :
/// décalage = heure du répondeur − (émission du ping + aller-retour / 2).
/// On garde les derniers échantillons et on retient celui de plus petit aller-retour : c'est le moins
/// déformé par l'attente dans les files réseau. Erreur max ≈ la moitié de cet aller-retour.
public sealed class ClockSync
{
    private const int Window = 12;
    private readonly (int rtt, double offset)[] _samples = new (int, double)[Window];
    private int _count, _next;

    /// Horloge du pair − la nôtre (ms). Valable si HasValue.
    public double OffsetMs { get; private set; }
    public bool HasValue => _count > 0;
    /// Aller-retour de l'échantillon retenu (précision ≈ la moitié).
    public int BestRttMs { get; private set; } = -1;

    /// pingSentMs, pongRecvMs : notre horloge ; responderMs : la sienne au moment de répondre.
    public void Add(uint pingSentMs, uint responderMs, uint pongRecvMs)
    {
        int rtt = (int)(pongRecvMs - pingSentMs);
        if (rtt < 0 || rtt > 5000) return; // horloge repartie, pong très en retard : inutilisable
        double offset = (double)responderMs - (pingSentMs + rtt / 2.0);
        _samples[_next] = (rtt, offset);
        _next = (_next + 1) % Window;
        _count = Math.Min(_count + 1, Window);
        int best = 0;
        for (int i = 1; i < _count; i++) if (_samples[i].rtt < _samples[best].rtt) best = i;
        (BestRttMs, OffsetMs) = _samples[best];
    }
}
