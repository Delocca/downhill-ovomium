using Ovomium.Core.Keys;

namespace Ovomium.Tests;

/// Touches effectives : réglage du joueur, défaut normal, défaut quand LMD-VR est installé, conflits.
public class KeyChoiceTests
{
    [Fact]
    public void SansLmdVr_ReglageGarde()
    {
        Assert.Equal("F6", KeyChoice.Effective("F6", "F6", "F1", vrInstalled: false));
        Assert.Equal("K", KeyChoice.Effective(" K ", "F6", "F1", vrInstalled: false));
    }

    [Fact]
    public void AvecLmdVr_DefautRemplace()
    {
        Assert.Equal("F1", KeyChoice.Effective("F6", "F6", "F1", vrInstalled: true));
        Assert.Equal("F1", KeyChoice.Effective("f6 ", "F6", "F1", vrInstalled: true)); // casse et espaces ignorés
    }

    [Fact]
    public void AvecLmdVr_ReglagePersonnaliseGarde()
    {
        Assert.Equal("F12", KeyChoice.Effective("F12", "F6", "F1", vrInstalled: true));
        Assert.Equal("F9", KeyChoice.Effective("F9", "F6", "F1", vrInstalled: true)); // même en conflit : choix du joueur
    }

    [Fact]
    public void JeuVr_SansConflit()
    {
        // Jeu de touches retenu avec LMD-VR : F1 course, F2 aspect, F3 panneau, F4 écho, V vue.
        Assert.Empty(KeyChoice.VrConflicts(new[] { "F1", "F2", "F3", "F4", "V" }));
    }

    [Fact]
    public void Conflits_Detectes()
    {
        Assert.Equal(new[] { "F6", "F9" }, KeyChoice.VrConflicts(new[] { "F6", "F8", "f6", "F9", "keypad4" }).Take(2));
        Assert.Contains("keypad4", KeyChoice.VrConflicts(new[] { "keypad4" }));
        Assert.Empty(KeyChoice.VrConflicts(new string[] { null, "" }));
    }
}
