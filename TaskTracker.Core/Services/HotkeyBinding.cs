namespace TaskTracker.Core.Services
{
    /// <summary>
    /// A global hotkey as text — "Ctrl+Alt+T" — parsed into the modifier and virtual-key
    /// codes RegisterHotKey wants.
    ///
    /// In Core rather than beside the P/Invoke so the parsing is testable on any platform;
    /// the WPF head keeps the registration. The hotkey used to be a const, which made a
    /// clash with another application unfixable without a rebuild.
    /// </summary>
    public record HotkeyBinding(uint Modifiers, uint VirtualKey, string Text)
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;

        public const string Default = "Ctrl+Alt+T";

        /// <summary>
        /// Parses "Ctrl+Alt+T". Returns null when the text names no key, an unknown key,
        /// or no modifier — a modifierless global hotkey would swallow that key for every
        /// application on the desktop.
        /// </summary>
        public static HotkeyBinding? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            uint modifiers = 0;
            uint? key = null;
            var parts = new List<string>();

            foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0)
                    continue;

                switch (token.ToLowerInvariant())
                {
                    case "ctrl" or "control":
                        modifiers |= ModControl;
                        parts.Add("Ctrl");
                        continue;
                    case "alt":
                        modifiers |= ModAlt;
                        parts.Add("Alt");
                        continue;
                    case "shift":
                        modifiers |= ModShift;
                        parts.Add("Shift");
                        continue;
                    case "win" or "windows":
                        modifiers |= ModWin;
                        parts.Add("Win");
                        continue;
                }

                // Anything not a modifier is the key, and there may be only one.
                if (key != null)
                    return null;
                key = ParseKey(token);
                if (key == null)
                    return null;
                parts.Add(token.ToUpperInvariant());
            }

            if (key == null || modifiers == 0)
                return null;

            return new HotkeyBinding(modifiers, key.Value, string.Join("+", parts));
        }

        /// <summary>Letters, digits and F1-F24 — enough for a quick-capture shortcut.</summary>
        private static uint? ParseKey(string token)
        {
            if (token.Length == 1)
            {
                var c = char.ToUpperInvariant(token[0]);
                if (c is >= 'A' and <= 'Z')
                    return c;
                if (c is >= '0' and <= '9')
                    return c;
                return null;
            }

            if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') &&
                int.TryParse(token.AsSpan(1), out var number) && number is >= 1 and <= 24)
            {
                return (uint)(0x70 + number - 1); // VK_F1 = 0x70
            }

            return null;
        }
    }
}
