using System.Text.Json;

namespace Ovomium.Core.Update;

/// Lecture tolérante d'un champ JSON : valeur du bon type, ou défaut.
internal static class JsonRead
{
    public static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static bool Flag(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
