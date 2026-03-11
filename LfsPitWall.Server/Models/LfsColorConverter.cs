namespace LfsPitWall.Server.Models;

/// <summary>
/// Decodes LFS text control sequences and converts LFS color codes to HTML.
/// </summary>
public static class LfsColorConverter
{
    private const string DefaultColor = "#6B8E23";

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
        { '8', DefaultColor },
    };

    private static readonly Dictionary<char, char> EscapeMap = new()
    {
        { 'v', '|' },
        { 'a', '*' },
        { 'c', ':' },
        { 'd', '\\' },
        { 's', '/' },
        { 'q', '?' },
        { 't', '"' },
        { 'l', '<' },
        { 'r', '>' },
        { '^', '^' },
    };

    private static readonly Dictionary<char, int> CodePageMap = new()
    {
        { 'L', 1252 },
        { 'G', 28597 },
        { 'C', 1251 },
        { 'J', 932 },
        { 'E', 28592 },
        { 'T', 28599 },
        { 'B', 28603 },
        { 'H', 936 },
        { 'S', 949 },
        { 'K', 950 },
    };

    static LfsColorConverter()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Decodes raw LFS bytes using inline code page switches from ColorCodes.txt.
    /// Color control codes are preserved for later HTML conversion.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        var result = new System.Text.StringBuilder();
        var pendingBytes = new List<byte>();
        var originalEncoding = System.Text.Encoding.GetEncoding(1252);
        var currentEncoding = originalEncoding;

        void FlushPending()
        {
            if (pendingBytes.Count == 0)
                return;

            result.Append(currentEncoding.GetString(pendingBytes.ToArray()));
            pendingBytes.Clear();
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            byte current = bytes[i];
            if (current == 0)
                break;

            if (current == '^' && i < bytes.Length - 1 && bytes[i + 1] != 0)
            {
                char next = (char)bytes[i + 1];

                if (CodePageMap.TryGetValue(next, out var codePage))
                {
                    FlushPending();
                    currentEncoding = System.Text.Encoding.GetEncoding(codePage);
                    i++;
                    continue;
                }

                if (next == '9')
                {
                    FlushPending();
                    currentEncoding = originalEncoding;
                    result.Append('^').Append(next);
                    i++;
                    continue;
                }

                if (EscapeMap.TryGetValue(next, out var escapedChar))
                {
                    FlushPending();
                    result.Append(escapedChar);
                    i++;
                    continue;
                }

                if (ColorMap.ContainsKey(next))
                {
                    FlushPending();
                    result.Append('^').Append(next);
                    i++;
                    continue;
                }
            }

            pendingBytes.Add(current);
        }

        FlushPending();
        return result.ToString().TrimEnd('\0').Trim();
    }

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
        string? currentColor = null;
        int i = 0;

        while (i < lfsText.Length)
        {
            // Look for color code (^ followed by any character)
            if (i < lfsText.Length - 1 && lfsText[i] == '^')
            {
                char nextChar = lfsText[i + 1];

                if (ColorMap.TryGetValue(nextChar, out var color))
                {
                    currentColor = color;
                    i += 2; // Skip both ^ and color character
                    continue;
                }
                if (nextChar == '9')
                {
                    currentColor = null;
                    i += 2;
                    continue;
                }
                if (EscapeMap.TryGetValue(nextChar, out var escapedChar))
                {
                    AppendHtmlText(result, escapedChar.ToString(), currentColor);
                    i += 2;
                    continue;
                }
                if (CodePageMap.ContainsKey(nextChar))
                {
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
                    AppendHtmlText(result, text, currentColor);
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
                if (ColorMap.ContainsKey(nextChar) || nextChar == '9' || CodePageMap.ContainsKey(nextChar))
                {
                    i += 2;
                    continue;
                }
                if (EscapeMap.TryGetValue(nextChar, out var escapedChar))
                {
                    result.Append(escapedChar);
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

    private static void AppendHtmlText(System.Text.StringBuilder result, string text, string? color)
    {
        if (string.IsNullOrEmpty(text))
            return;

        text = System.Net.WebUtility.HtmlEncode(text);
        if (string.IsNullOrEmpty(color))
        {
            result.Append(text);
            return;
        }

        result.Append($"<span style=\"color:{color}\">{text}</span>");
    }
}
