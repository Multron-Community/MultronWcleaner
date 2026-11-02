using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MultronWinCleaner.Processes
{
    public static class VisualEffects
    {
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;
        private const uint SPI_GETANIMATION = 0x0048;
        private const uint SPI_SETANIMATION = 0x0049;

        private sealed record Effect(string Name, uint Get, uint Set, bool Wanted, bool SetByUiParam);

        private static readonly Effect[] Effects =
        {
            new("ClientAreaAnimation", 0x1042, 0x1043, false, false),
            new("MenuAnimation", 0x1002, 0x1003, false, false),
            new("SelectionFade", 0x1014, 0x1015, false, false),
            new("ToolTipAnimation", 0x1016, 0x1017, false, false),
            new("CursorShadow", 0x101A, 0x101B, false, false),
            new("DropShadow", 0x1024, 0x1025, false, false),
            new("ComboBoxAnimation", 0x1004, 0x1005, true, false),
            new("ListBoxSmoothScrolling", 0x1006, 0x1007, true, false),
            new("DragFullWindows", 0x0026, 0x0025, true, true),
            new("FontSmoothing", 0x004A, 0x004B, true, true),
        };

        public const string MinimizeAnimation = "MinimizeAnimation";

        [StructLayout(LayoutKind.Sequential)]
        private struct AnimationInfo
        {
            public uint Size;
            public int MinAnimate;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint uiParam, ref int pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint uiParam, ref AnimationInfo pvParam, uint winIni);

        public static Dictionary<string, int> Read()
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Effect effect in Effects)
            {
                int value = 0;
                if (SystemParametersInfo(effect.Get, 0, ref value, 0))
                    values[effect.Name] = value != 0 ? 1 : 0;
            }
            var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
            if (SystemParametersInfo(SPI_GETANIMATION, info.Size, ref info, 0))
                values[MinimizeAnimation] = info.MinAnimate != 0 ? 1 : 0;
            return values;
        }

        public static Dictionary<string, int> Wanted()
        {
            var values = Effects.ToDictionary(e => e.Name, e => e.Wanted ? 1 : 0, StringComparer.Ordinal);
            values[MinimizeAnimation] = 0;
            return values;
        }

        public static bool Matches(Dictionary<string, int> wanted)
        {
            var current = Read();
            return wanted.All(w => !current.TryGetValue(w.Key, out int v) || v == w.Value);
        }

        public static void Write(Dictionary<string, int> values)
        {
            foreach (Effect effect in Effects)
            {
                if (!values.TryGetValue(effect.Name, out int value))
                    continue;
                bool ok = effect.SetByUiParam
                    ? SystemParametersInfo(effect.Set, (uint)value, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)
                    : SystemParametersInfo(effect.Set, 0, new IntPtr(value), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                if (!ok)
                    throw new InvalidOperationException(effect.Name + ": " + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            if (values.TryGetValue(MinimizeAnimation, out int minimize))
            {
                var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>(), MinAnimate = minimize };
                if (!SystemParametersInfo(SPI_SETANIMATION, info.Size, ref info, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
                    throw new InvalidOperationException(MinimizeAnimation + ": " + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }
    }
}
