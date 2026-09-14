using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VisibilityPlus;

/// <summary>How bound hotkeys reveal their group. Serialized as int (Hold = 0).</summary>
public enum HotkeyMode
{
    Hold,
    Toggle,
    Toggle30s,
}

/// <summary>Hold-to-show keybind, stored as raw VK codes and polled via GetAsyncKeyState.</summary>
public static class HoldKeybind
{
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // Alt
    public const int VK_ESCAPE = 0x1B;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static readonly int[] CapturableKeys = BuildCapturableKeys();

    private static int[] BuildCapturableKeys()
    {
        var keys = new List<int>();
        for (int vk = 0x30; vk <= 0x39; vk++) keys.Add(vk); // 0-9
        for (int vk = 0x41; vk <= 0x5A; vk++) keys.Add(vk); // A-Z
        for (int vk = 0x60; vk <= 0x69; vk++) keys.Add(vk); // numpad 0-9
        for (int vk = 0x70; vk <= 0x7B; vk++) keys.Add(vk); // F1-F12
        keys.AddRange([0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E]); // space, pgup/pgdn, end/home, arrows, insert/delete
        return keys.ToArray();
    }

    public static bool TryCapture(out int key, out bool ctrl, out bool shift, out bool alt)
    {
        ctrl = IsDown(VK_CONTROL);
        shift = IsDown(VK_SHIFT);
        alt = IsDown(VK_MENU);
        foreach (int vk in CapturableKeys)
        {
            if (IsDown(vk))
            {
                key = vk;
                return true;
            }
        }
        key = 0;
        return false;
    }

    public static string KeyName(int vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
        if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1);
        return vk switch
        {
            0x20 => "Space",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            _ => $"VK{vk:X2}",
        };
    }

    public static string ComboName(int vk, bool ctrl, bool shift, bool alt)
        => (ctrl ? "Ctrl+" : string.Empty)
         + (shift ? "Shift+" : string.Empty)
         + (alt ? "Alt+" : string.Empty)
         + KeyName(vk);
}
