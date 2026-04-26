using System.Windows.Input;

namespace SnapForge.Services;

public static class HotkeyParser
{
    public static bool TryParseCombination(string comboText, out int modifiers, out int virtualKey, out string normalizedCombo)
    {
        modifiers = 0;
        virtualKey = 0;
        normalizedCombo = string.Empty;

        if (string.IsNullOrWhiteSpace(comboText))
        {
            return false;
        }

        string[] parts = comboText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (parts.Length == 1)
        {
            if (!TryParseKeyToken(parts[0], out Key singleKey))
            {
                return false;
            }

            modifiers = 0x0004;
            virtualKey = KeyInterop.VirtualKeyFromKey(singleKey);
            if (virtualKey == 0)
            {
                return false;
            }

            normalizedCombo = BuildComboText(modifiers, singleKey);
            return true;
        }

        string keyToken = parts[^1];
        if (!TryParseKeyToken(keyToken, out Key key))
        {
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string token = parts[i].ToLowerInvariant();
            modifiers |= token switch
            {
                "ctrl" or "control" => 0x0002,
                "alt" => 0x0001,
                "shift" => 0x0004,
                "win" or "windows" or "meta" => 0x0008,
                _ => 0
            };
        }

        if (modifiers == 0)
        {
            modifiers = 0x0004;
        }

        virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return false;
        }

        normalizedCombo = BuildComboText(modifiers, key);
        return true;
    }

    public static string BuildComboText(int modifiers, Key key)
    {
        List<string> mods = [];
        if ((modifiers & 0x0004) != 0)
        {
            mods.Add("Shift");
        }

        if ((modifiers & 0x0002) != 0)
        {
            mods.Add("Ctrl");
        }

        if ((modifiers & 0x0001) != 0)
        {
            mods.Add("Alt");
        }

        if ((modifiers & 0x0008) != 0)
        {
            mods.Add("Win");
        }

        mods.Add(key.ToString());
        return string.Join('+', mods);
    }

    public static bool TryParseKeyToken(string token, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string cleaned = token.Trim();
        if (Enum.TryParse(cleaned, true, out Key parsed))
        {
            key = parsed;
            return true;
        }

        if (cleaned.Length == 1)
        {
            char ch = char.ToUpperInvariant(cleaned[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                key = Key.A + (ch - 'A');
                return true;
            }

            if (char.IsDigit(ch))
            {
                key = Key.D0 + (ch - '0');
                return true;
            }
        }

        return false;
    }

    public static string FormatFromWin32(int modifiers, int virtualKey)
    {
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return BuildComboText(modifiers, key);
    }
}
