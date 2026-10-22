using System.Globalization;

namespace DeadCellsMultiplayerMod.PortableCore;

internal readonly record struct CoopDlcInfo(int Bit, uint SteamAppId, string Name);

internal static class CoopDlcMask
{
    public const int RiseOfTheGiant = 1 << 0;
    public const int BadSeed = 1 << 1;
    public const int FatalFalls = 1 << 2;
    public const int TheQueenAndTheSea = 1 << 3;
    public const int ReturnToCastlevania = 1 << 4;
    public const int All = RiseOfTheGiant | BadSeed | FatalFalls | TheQueenAndTheSea | ReturnToCastlevania;

    public const string OwnedTag = "DLCOWN";
    public const string SessionTag = "DLCSET";

    public static readonly CoopDlcInfo[] Known =
    {
        new(RiseOfTheGiant, 1046440, "Rise of the Giant"),
        new(BadSeed, 1204130, "The Bad Seed"),
        new(FatalFalls, 1451460, "Fatal Falls"),
        new(TheQueenAndTheSea, 1580050, "The Queen and the Sea"),
        new(ReturnToCastlevania, 2101430, "Return to Castlevania")
    };

    public static int Normalize(int mask) => mask & All;

    public static int Resolve(int hostPreference, int hostOwned, IEnumerable<int> peersOwned)
    {
        var mask = Normalize(hostPreference) & Normalize(hostOwned);
        foreach (var owned in peersOwned)
            mask &= Normalize(owned);
        return mask;
    }

    public static int CommonOwned(int hostOwned, IEnumerable<int> peersOwned)
    {
        var mask = Normalize(hostOwned);
        foreach (var owned in peersOwned)
            mask &= Normalize(owned);
        return mask;
    }

    public static string Describe(int mask)
    {
        mask = Normalize(mask);
        if (mask == 0)
            return "-";

        var names = new List<string>(Known.Length);
        foreach (var dlc in Known)
        {
            if ((mask & dlc.Bit) != 0)
                names.Add(dlc.Name);
        }

        return string.Join(", ", names);
    }

    public static string BuildOwnedLine(int owned) =>
        string.Create(CultureInfo.InvariantCulture, $"{OwnedTag}|{Normalize(owned)}");

    public static string BuildSessionLine(int mask, int revision) =>
        string.Create(CultureInfo.InvariantCulture, $"{SessionTag}|{Normalize(mask)}|{revision}");

    public static bool TryParseOwned(string payload, out int owned)
    {
        owned = 0;
        var parts = payload.Split('|');
        if (parts.Length < 1 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;
        if ((value & ~All) != 0 || value < 0)
            return false;

        owned = value;
        return true;
    }

    public static bool TryParseSession(string payload, out int mask, out int revision)
    {
        mask = 0;
        revision = 0;
        var parts = payload.Split('|');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rev))
            return false;
        if ((value & ~All) != 0 || value < 0 || rev < 0)
            return false;

        mask = value;
        revision = rev;
        return true;
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP DLC MASK — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Биты как у игры (dc.tool.DLC.installMask): RotG=1, BadSeed=2, FatalFalls=4, Queen=8, Castlevania(Purple)=16.
 *  Steam app id — как в _DLC.getSteamId.
 *  Набор сессии = предпочтение хоста ∧ DLC хоста ∧ DLC каждого клиента: включить можно только общее.
 *  Протокол: DLCOWN|mask (каждый пир о себе), DLCSET|mask|revision (хост → клиенты).
 * ═══════════════════════════════════════
*/
