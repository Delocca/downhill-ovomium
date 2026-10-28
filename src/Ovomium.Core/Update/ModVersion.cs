using System;
using System.Globalization;

namespace Ovomium.Core.Update;

/// Version du mod « X.Y.Z » (chiffres seulement : pas de préversion « -beta »), comparable.
public readonly struct ModVersion : IComparable<ModVersion>, IEquatable<ModVersion>
{
    public readonly int Major, Minor, Patch;

    public ModVersion(int major, int minor, int patch) { Major = major; Minor = minor; Patch = patch; }

    public static bool TryParse(string s, out ModVersion v)
    {
        v = default;
        var parts = s?.Split('.');
        if (parts == null || parts.Length != 3) return false;
        var n = new int[3];
        for (int i = 0; i < 3; i++)
            if (parts[i].Length is 0 or > 6 || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        v = new ModVersion(n[0], n[1], n[2]);
        return true;
    }

    /// Tag de release GitHub « vX.Y.Z ».
    public static bool TryParseTag(string tag, out ModVersion v)
    {
        v = default;
        return tag != null && tag.Length > 1 && tag[0] == 'v' && TryParse(tag[1..], out v);
    }

    public int CompareTo(ModVersion o) =>
        Major != o.Major ? Major.CompareTo(o.Major) : Minor != o.Minor ? Minor.CompareTo(o.Minor) : Patch.CompareTo(o.Patch);

    public bool Equals(ModVersion o) => CompareTo(o) == 0;
    public override bool Equals(object obj) => obj is ModVersion o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);
    public static bool operator >(ModVersion a, ModVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(ModVersion a, ModVersion b) => a.CompareTo(b) < 0;
    public static bool operator ==(ModVersion a, ModVersion b) => a.Equals(b);
    public static bool operator !=(ModVersion a, ModVersion b) => !a.Equals(b);
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
