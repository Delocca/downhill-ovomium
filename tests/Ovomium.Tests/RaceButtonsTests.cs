using Ovomium.Core.Race;

namespace Ovomium.Tests;

/// Boutons de course : appui bref / maintenu au clavier, combinaisons manette, jauge.
public class RaceButtonsTests
{
    private static readonly RaceButtonState Up = new(false, false, false, false);
    private static readonly RaceButtonState Key = new(true, false, false, false);

    /// Fait tourner b de `from` à `to` ms par images de 16 ms avec l'état s ; renvoie les commandes non nulles.
    private static List<RaceCommand> Run(RaceButtons b, RaceButtonState s, double from, double to, bool canGo = true, bool canQuit = true)
    {
        var got = new List<RaceCommand>();
        for (double t = from; t < to; t += 16)
        {
            var c = b.Update(s, t, canGo, canQuit);
            if (c != RaceCommand.None) got.Add(c);
        }
        return got;
    }

    [Fact]
    public void KeyTapIsGo()
    {
        var b = new RaceButtons();
        Assert.Empty(Run(b, Key, 0, 150));
        Assert.Equal(new[] { RaceCommand.Go }, Run(b, Up, 150, 200));
    }

    [Fact]
    public void KeyGoesOnPressWhenQuitMeaningless()
    {
        var b = new RaceButtons();
        Assert.Equal(new[] { RaceCommand.Go }, Run(b, Key, 0, 2000, canQuit: false));
        Assert.Empty(Run(b, Up, 2000, 2100, canQuit: false));
    }

    [Fact]
    public void KeyHoldIsQuitOnceWithGauge()
    {
        var b = new RaceButtons();
        Assert.Empty(Run(b, Key, 0, 750));
        Assert.InRange(b.HoldProgress, 0.45, 0.55);
        Assert.False(b.HoldFromPad);
        Assert.Equal(new[] { RaceCommand.Quit }, Run(b, Key, 750, 3000));
        Assert.Equal(0, b.HoldProgress);
        Assert.Empty(Run(b, Up, 3000, 3100)); // relâcher après : pas de Go
    }

    [Fact]
    public void KeyReleasedMidHoldDoesNothing()
    {
        var b = new RaceButtons();
        Assert.Empty(Run(b, Key, 0, 900));
        Assert.Empty(Run(b, Up, 900, 1000));
        Assert.Equal(0, b.HoldProgress);
    }

    [Fact]
    public void KeyTapWhenOnlyQuitGivesHint()
    {
        var b = new RaceButtons();
        Run(b, Key, 0, 100, canGo: false);
        Assert.Equal(new[] { RaceCommand.HoldHint }, Run(b, Up, 100, 150, canGo: false));
    }

    [Fact]
    public void PadModifierPlusGoIsGo()
    {
        var b = new RaceButtons();
        Assert.Empty(Run(b, new(false, true, false, false), 0, 300));
        Assert.Equal(new[] { RaceCommand.Go }, Run(b, new(false, true, true, false), 300, 1000));
    }

    [Fact]
    public void PadGoWithoutModifierIsIgnored()
    {
        var b = new RaceButtons();
        Assert.Empty(Run(b, new(false, false, true, true), 0, 3000));
    }

    [Fact]
    public void PadHoldQuitAndReleaseCancels()
    {
        var b = new RaceButtons();
        var hold = new RaceButtonState(false, true, false, true);
        Assert.Empty(Run(b, hold, 0, 1000));
        Assert.True(b.HoldFromPad);
        Assert.InRange(b.HoldProgress, 0.6, 0.7);
        Assert.Empty(Run(b, new(false, false, false, true), 1000, 1100)); // modificateur relâché : annulé
        Assert.Equal(0, b.HoldProgress);
        Assert.Empty(Run(b, hold, 1100, 2500)); // recommence à zéro
        Assert.Equal(new[] { RaceCommand.Quit }, Run(b, hold, 2500, 5000));
    }

    [Fact]
    public void NothingWhenNothingAllowed()
    {
        var b = new RaceButtons();
        Assert.Empty(Run(b, new(true, true, true, true), 0, 3000, canGo: false, canQuit: false));
        Assert.Empty(Run(b, Up, 3000, 3100, canGo: false, canQuit: false));
    }
}
