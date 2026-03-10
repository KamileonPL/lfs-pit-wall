using System.Text;

namespace LfsPitWall.Server.Helpers;

/// <summary>
/// Utility methods for parsing and formatting driver/car names from InSim protocol data.
/// </summary>
public static class DriverNameHelper
{
    /// <summary>
    /// Formats player name: extracts group prefix and wraps in slashes.
    /// Example: "SRP Kamileon" → "/SRP/ Kamileon"
    /// Names with brackets or slashes are kept as-is: "[FM]TJ" → "[FM]TJ"
    /// </summary>
    public static string FormatPlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        if (name.StartsWith('[') || name.StartsWith('/') || name.StartsWith('<'))
            return name;

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var prefix = parts[0];
            bool isValidGroupName = !string.IsNullOrEmpty(prefix) &&
                                   prefix.Length <= 10 &&
                                   prefix.All(c => char.IsLetterOrDigit(c));

            if (isValidGroupName)
            {
                string rest = string.Join(" ", parts.Skip(1));
                return $"/{prefix}/ {rest}";
            }
        }

        return name;
    }

    /// <summary>
    /// Parses CName bytes from IS_NPL packet.
    /// Standard cars (XRG, XFG, RB4) are decoded as ASCII.
    /// Mod cars are decoded as little-endian 3-byte hex ID (e.g., 38A066).
    /// </summary>
    public static string ParseCarName(byte[] cname)
    {
        bool isAsciiString = true;
        for (int i = 0; i < cname.Length - 1; i++)
        {
            byte b = cname[i];
            if (b == 0) break;
            if (b < 32 || b > 126)
            {
                isAsciiString = false;
                break;
            }
        }

        if (isAsciiString)
        {
            var result = Encoding.ASCII.GetString(cname).TrimEnd('\0').Trim();
            return string.IsNullOrEmpty(result) ? "???" : result;
        }

        uint modId = cname[0] | ((uint)cname[1] << 8) | ((uint)cname[2] << 16);
        return modId.ToString("X6");
    }
}
