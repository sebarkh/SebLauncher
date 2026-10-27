using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SebLauncher
{
    internal static class Launcher
    {
        public static void Launch(LauncherItem item, bool asAdmin)
        {
            var path = IconLoader.Expand(item.Path);
            var psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                // aplikacje z Microsoft Store / wbudowane – uruchamiane przez Eksplorator (shell:AppsFolder\AUMID)
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                return;
            }
            if (!string.IsNullOrWhiteSpace(item.Args)) psi.Arguments = IconLoader.Expand(item.Args);

            string wd = !string.IsNullOrWhiteSpace(item.WorkDir) ? IconLoader.Expand(item.WorkDir)
                      : (File.Exists(path) ? Path.GetDirectoryName(path) : null);
            if (!string.IsNullOrEmpty(wd) && Directory.Exists(wd)) psi.WorkingDirectory = wd;
            if (asAdmin || item.Admin) psi.Verb = "runas";

            try { Process.Start(psi); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* anulowane UAC */ }
        }

        public static void OpenLocation(LauncherItem item)
        {
            var p = IconLoader.Expand(item.Path);
            if (!File.Exists(p) && !Directory.Exists(p)) p = IconLoader.Expand(item.Icon);
            if (string.IsNullOrEmpty(p) || (!File.Exists(p) && !Directory.Exists(p))) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + p + "\"") { UseShellExecute = true });
        }
    }

    internal static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "SebLauncher";

        static string ExePath => Program.ExePath;

        public static bool IsEnabled()
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            var v = k?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(v) && v.Trim('"').Equals(ExePath, StringComparison.OrdinalIgnoreCase);
        }

        public static void Set(bool enabled)
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) k.SetValue(ValueName, "\"" + ExePath + "\"");
            else k.DeleteValue(ValueName, false);
        }
    }

    internal sealed class HotkeyManager : IDisposable
    {
        const int Id = 0x5EB1;
        readonly HwndSource source;
        Action callback;
        bool registered;

        public HotkeyManager()
        {
            var p = new HwndSourceParameters("SebLauncherHotkey")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
                WindowStyle = 0,
                Width = 0,
                Height = 0
            };
            source = new HwndSource(p);
            source.AddHook(WndProc);
        }

        public bool Register(string text, Action onPressed, out string error)
        {
            error = null;
            Unregister();
            callback = onPressed;
            if (!TryParse(text, out uint mods, out uint vk))
            {
                error = $"Nie rozumiem skrótu „{text}”. Przykłady: Ctrl+Alt+Space, Win+Shift+L, Ctrl+F12.";
                return false;
            }
            registered = NativeMethods.RegisterHotKey(source.Handle, Id, mods | NativeMethods.MOD_NOREPEAT, vk);
            if (!registered) error = $"Skrót {text} jest zajęty przez inny program. Zmień „Hotkey” w launcher.json.";
            return registered;
        }

        public static bool TryParse(string text, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var raw in text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; continue;
                    case "alt": mods |= NativeMethods.MOD_ALT; continue;
                    case "shift": mods |= NativeMethods.MOD_SHIFT; continue;
                    case "win": case "windows": mods |= NativeMethods.MOD_WIN; continue;
                }
                Key key;
                if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                else if (!Enum.TryParse(raw, true, out key)) return false;
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            return vk != 0;
        }

        public void Unregister()
        {
            if (registered) NativeMethods.UnregisterHotKey(source.Handle, Id);
            registered = false;
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == Id)
            {
                callback?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Unregister();
            source.RemoveHook(WndProc);
            source.Dispose();
        }
    }
}
