using System.Windows.Input;

namespace Launchy.Models;

public enum HotkeyAction
{
    OpenLauncher
}

public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {(int)(key - Key.NumPad0)}",
        Key.Enter => "Enter",
        Key.Back => "Backspace",
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        _ => key.ToString()
    };
}

public sealed record HotkeyActionInfo(HotkeyAction Action, string Name, string Description, Hotkey Default);

public static class HotkeyActions
{
    /// <summary>Every configurable shortcut. Add new keybinds here.</summary>
    public static IReadOnlyList<HotkeyActionInfo> All { get; } =
    [
        new(HotkeyAction.OpenLauncher, "Open launcher", "Shows or hides the command prompt.",
            new Hotkey(ModifierKeys.Alt, Key.Q))
    ];
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public Dictionary<HotkeyAction, Hotkey> Hotkeys { get; set; } = [];

    public Hotkey HotkeyFor(HotkeyActionInfo info) => Hotkeys.GetValueOrDefault(info.Action, info.Default);
}
