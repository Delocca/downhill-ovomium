using Ovomium.Core.Keys;

namespace Ovomium.Tests;

/// Conflits entre nos touches et celles de LMD-VR.
public class KeyChoiceTests
{
    [Fact]
    public void Defauts_SansConflit()
    {
        // Touches par défaut d'Ovomium : F1 course, F2 aspect, F3 panneau, F4 écho, V vue.
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
