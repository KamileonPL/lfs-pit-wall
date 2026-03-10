namespace LfsPitWall.Server.Models;

/// <summary>
/// Converts LFS color codes (^1, ^2, etc) to HTML with CSS colors
/// LFS uses single character codes:
/// ^0 - Black
/// ^1 - Red
/// ^2 - Green
/// ^3 - Yellow
/// ^4 - Blue
/// ^5 - Magenta
/// ^6 - Cyan
/// ^7 - White
/// ^S - Default/reset
/// ^s - Alternate (same as ^S for our purposes)
/// </summary>
public static class LfsColorConverter
{
    private static readonly Dictionary<char, string> ColorMap = new()
    {
        { '0', "#000000" },  // Black
        { '1', "#FF0000" },  // Red
        { '2', "#00FF00" },  // Green (Lime)
        { '3', "#FFFF00" },  // Yellow
        { '4', "#0000FF" },  // Blue
        { '5', "#FF00FF" },  // Magenta
        { '6', "#00FFFF" },  // Cyan
        { '7', "#FFFFFF" },  // White
    };

    /// <summary>
    /// Converts LFS color-coded string to HTML with span tags and inline styles
    /// Example: "^1Red ^2Green" -> "<span style="color:#FF0000">Red </span><span style="color:#00FF00">Green</span>"
    /// Removes only control characters (CR, LF, TAB, NULL), keeps all special chars and unicode
    /// </summary>
    public static string ConvertToHtml(string lfsText)
    {
        if (string.IsNullOrEmpty(lfsText))
            return lfsText;

        var result = new System.Text.StringBuilder();
        string currentColor = "#FFFFFF"; // Default white
        int i = 0;

        while (i < lfsText.Length)
        {
            // Look for color code (^ followed by any character)
            if (i < lfsText.Length - 1 && lfsText[i] == '^')
            {
                char nextChar = lfsText[i + 1];

                // Check if it's a valid color code (0-7)
                if (char.IsDigit(nextChar) && ColorMap.ContainsKey(nextChar))
                {
                    currentColor = ColorMap[nextChar];
                    i += 2; // Skip both ^ and color character
                    continue;
                }
                else if (nextChar == 'S' || nextChar == 's')
                {
                    // Reset to white
                    currentColor = "#FFFFFF";
                    i += 2; // Skip both ^ and S
                    continue;
                }
                else if (nextChar == '^')
                {
                    // Escaped ^, show single ^
                    result.Append("^");
                    i += 2;
                    continue;
                }
                else
                {
                    // Unknown code - skip the ^ and the next character
                    // This handles ^J, ^G, ^a, etc.
                    i += 2;
                    continue;
                }
            }

            // Regular character - accumulate until next color code
            var textStart = i;
            while (i < lfsText.Length && !(i < lfsText.Length - 1 && lfsText[i] == '^'))
            {
                i++;
            }

            if (i > textStart)
            {
                string text = lfsText.Substring(textStart, i - textStart);
                
                // Filter out ONLY control characters (CR, LF, TAB, NULL, etc)
                // Keep special characters and unicode
                var filtered = new System.Text.StringBuilder();
                foreach (char c in text)
                {
                    // Skip null, CR, LF, TAB and other control chars
                    if (c == '\0' || c == '\r' || c == '\n' || c == '\t')
                        continue;
                    // Skip ASCII control characters (0-31 except we already handled CR/LF/TAB)
                    if (c < 32 && c != '\t')
                        continue;
                    // Keep everything else: printable ASCII, special chars, unicode
                    filtered.Append(c);
                }
                
                text = filtered.ToString();
                
                if (text.Length > 0)
                {
                    // HTML encode the text to prevent injection
                    text = System.Net.WebUtility.HtmlEncode(text);
                    result.Append($"<span style=\"color:{currentColor}\">{text}</span>");
                }
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Removes all LFS color codes, returning clean text
    /// Keeps all special characters and unicode, removes only control chars
    /// </summary>
    public static string RemoveColorCodes(string lfsText)
    {
        if (string.IsNullOrEmpty(lfsText))
            return lfsText;

        var result = new System.Text.StringBuilder();
        int i = 0;

        while (i < lfsText.Length)
        {
            if (i < lfsText.Length - 1 && lfsText[i] == '^')
            {
                char nextChar = lfsText[i + 1];
                // Skip color codes and resets
                if (char.IsDigit(nextChar) || nextChar == 'S' || nextChar == 's' || nextChar == '^')
                {
                    i += 2;
                    continue;
                }
                else
                {
                    // Unknown code - skip both chars
                    i += 2;
                    continue;
                }
            }

            char c = lfsText[i];
            // Skip only control characters (CR, LF, TAB, NULL)
            if (c == '\0' || c == '\r' || c == '\n' || c == '\t')
            {
                i++;
                continue;
            }
            // Skip ASCII control characters (0-31)
            if (c < 32)
            {
                i++;
                continue;
            }
            // Keep everything else: printable ASCII, special chars, unicode
            result.Append(c);
            i++;
        }

        return result.ToString();
    }
}
