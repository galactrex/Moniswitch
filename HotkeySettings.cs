namespace Moniswitch;

internal sealed class HotkeySettings
{
    public int KeyCode { get; set; } = (int)Keys.M;
    public bool Control { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }

    public HotkeyBinding ToBinding()
    {
        var key = (Keys)KeyCode & Keys.KeyCode;
        return HotkeyFormatter.IsSupportedKey(key)
            ? new HotkeyBinding(key, Control, Alt, Shift)
            : HotkeyBinding.Default;
    }

    public void Set(HotkeyBinding binding)
    {
        KeyCode = (int)binding.Key;
        Control = binding.Control;
        Alt = binding.Alt;
        Shift = binding.Shift;
    }
}

internal readonly record struct HotkeyBinding(Keys Key, bool Control, bool Alt, bool Shift)
{
    public static HotkeyBinding Default => new(Keys.M, Control: true, Alt: true, Shift: false);
    public string DisplayText => HotkeyFormatter.Display(this);
    public string DeskflowText => HotkeyFormatter.Deskflow(this);
}

internal static class HotkeyFormatter
{
    private static readonly IReadOnlyDictionary<Keys, (string Display, string Deskflow)> NamedKeys =
        new Dictionary<Keys, (string Display, string Deskflow)>
        {
            [Keys.Back] = ("Backspace", "BackSpace"),
            [Keys.Tab] = ("Tab", "Tab"),
            [Keys.Enter] = ("Enter", "Return"),
            [Keys.Escape] = ("Esc", "Escape"),
            [Keys.Space] = ("Space", "Space"),
            [Keys.PageUp] = ("Page Up", "PageUp"),
            [Keys.PageDown] = ("Page Down", "PageDown"),
            [Keys.End] = ("End", "End"),
            [Keys.Home] = ("Home", "Home"),
            [Keys.Left] = ("Left", "Left"),
            [Keys.Up] = ("Up", "Up"),
            [Keys.Right] = ("Right", "Right"),
            [Keys.Down] = ("Down", "Down"),
            [Keys.Insert] = ("Insert", "Insert"),
            [Keys.Delete] = ("Delete", "Delete"),
            [Keys.OemSemicolon] = (";", "Semicolon"),
            [Keys.Oemplus] = ("=", "Equal"),
            [Keys.Oemcomma] = (",", "Comma"),
            [Keys.OemMinus] = ("-", "Minus"),
            [Keys.OemPeriod] = (".", "Period"),
            [Keys.OemQuestion] = ("/", "Slash"),
            [Keys.Oemtilde] = ("`", "Grave"),
            [Keys.OemOpenBrackets] = ("[", "BracketL"),
            [Keys.OemPipe] = ("\\", "Backslash"),
            [Keys.OemCloseBrackets] = ("]", "BracketR"),
            [Keys.OemQuotes] = ("'", "Apostrophe")
        };

    public static bool TryCreate(Keys keyData, out HotkeyBinding binding, out string error)
    {
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;
        if (IsModifierKey(key))
        {
            binding = default;
            error = string.Empty;
            return false;
        }

        if (!IsSupportedKey(key))
        {
            binding = default;
            error = "That key is not supported by Deskflow. Try a letter, number, arrow, or navigation key.";
            return false;
        }

        var control = modifiers.HasFlag(Keys.Control);
        var alt = modifiers.HasFlag(Keys.Alt);
        var shift = modifiers.HasFlag(Keys.Shift);
        if (!control && !alt && !shift)
        {
            binding = default;
            error = "Include Ctrl, Alt, or Shift so normal typing cannot trigger the switch.";
            return false;
        }

        binding = new HotkeyBinding(key, control, alt, shift);
        error = string.Empty;
        return true;
    }

    public static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.LWin or Keys.RWin;

    public static bool IsSupportedKey(Keys key) =>
        key is >= Keys.A and <= Keys.Z ||
        key is >= Keys.D0 and <= Keys.D9 ||
        key is >= Keys.F1 and <= Keys.F24 ||
        NamedKeys.ContainsKey(key);

    public static string Display(HotkeyBinding binding) =>
        string.Join(" + ", Components(binding, deskflow: false));

    public static string Deskflow(HotkeyBinding binding) =>
        string.Join("+", Components(binding, deskflow: true));

    private static IEnumerable<string> Components(HotkeyBinding binding, bool deskflow)
    {
        if (binding.Control)
        {
            yield return deskflow ? "Control" : "Ctrl";
        }

        if (binding.Alt)
        {
            yield return "Alt";
        }

        if (binding.Shift)
        {
            yield return "Shift";
        }

        if (binding.Key is >= Keys.A and <= Keys.Z)
        {
            yield return binding.Key.ToString();
        }
        else if (binding.Key is >= Keys.D0 and <= Keys.D9)
        {
            yield return ((int)binding.Key - (int)Keys.D0).ToString();
        }
        else if (binding.Key is >= Keys.F1 and <= Keys.F24)
        {
            yield return binding.Key.ToString();
        }
        else if (NamedKeys.TryGetValue(binding.Key, out var names))
        {
            yield return deskflow ? names.Deskflow : names.Display;
        }
    }
}
