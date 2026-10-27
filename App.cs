using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace SebLauncher
{
    public static class Program
    {
        const string MutexName = "SebLauncher_SingleInstance_7F3A";
        const string EventName = "SebLauncher_ShowEvent_7F3A";

        public static string ExePath => System.Reflection.Assembly.GetEntryAssembly()?.Location
                                         ?? Process.GetCurrentProcess().MainModule.FileName;

        [STAThread]
        public static void Main()
        {
            // pełna obsługa DPI per-monitor w WPF (.NET Framework 4.8) – musi być ustawione przed załadowaniem WPF
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            Run();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Run()
        {
            using var mutex = new Mutex(true, MutexName, out bool created);
            if (!created)
            {
                // druga kopia -> poproś pierwszą o pokazanie paska i wyjdź
                try { using var ev = EventWaitHandle.OpenExisting(EventName); ev.Set(); } catch { }
                return;
            }

            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            var app = new LauncherApp();
            var listener = new Thread(() =>
            {
                try
                {
                    while (showEvent.WaitOne())
                    {
                        if (app.Exiting) break;
                        app.Dispatcher.BeginInvoke(new Action(() => app.ShowDock(false)));
                    }
                }
                catch { }
            }) { IsBackground = true };
            listener.Start();
            app.Run();
        }
    }

    public class LauncherApp : Application
    {
        public volatile bool Exiting;
        public LauncherConfig Config { get; private set; }
        internal DockWindow Dock { get; private set; }
        internal TrayIcon Tray { get; private set; }
        HotkeyManager hotkey;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(ex.Exception.ToString(), "SebLauncher – błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            try { Config = ConfigStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show("Nie można wczytać launcher.json:\n" + ex.Message + "\n\nUruchamiam z pustą konfiguracją (plik nie zostanie nadpisany, dopóki czegoś nie zmienisz).",
                    "SebLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
                Config = new LauncherConfig();
            }

            Dock = new DockWindow(this);
            Tray = new TrayIcon(this);
            hotkey = new HotkeyManager();
            RegisterHotkey();

            if (Config.Items.Count == 0)
                Tray.ShowBalloon("SebLauncher działa",
                    $"Skrót: {Config.Hotkey} albo klik w ikonę w zasobniku.\nPrawy klik na ikonie → „Skanuj programy i gry…”.");
        }

        void RegisterHotkey()
        {
            if (!hotkey.Register(Config.Hotkey, () => Dock.Toggle(), out string err))
                Tray.ShowBalloon("Skrót klawiszowy niedostępny", err);
        }

        public void ShowDock(bool pinned) => Dock.ShowDock(pinned);
        public void ToggleDock() => Dock.Toggle();

        public void SaveConfig()
        {
            try { ConfigStore.Save(Config); }
            catch (Exception ex)
            {
                MessageBox.Show("Nie udało się zapisać launcher.json:\n" + ex.Message, "SebLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void ReloadConfig()
        {
            try
            {
                Config = ConfigStore.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd w launcher.json:\n" + ex.Message, "SebLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            IconLoader.ClearCache();
            RegisterHotkey();
            Dock.Rebuild();
            Tray.ShowBalloon("SebLauncher", "Konfiguracja przeładowana.");
        }

        public void SetEdge(string edge)
        {
            Config.Edge = edge;
            SaveConfig();
            Dock.HideDock();
            Dock.Rebuild();
        }

        public void OpenScan()
        {
            Dock.HideDock();
            var w = new ScanWindow(this);
            w.Show();
            w.Activate();
        }

        public void EditConfig()
        {
            SaveConfig();
            try { Process.Start(new ProcessStartInfo(ConfigStore.FilePath) { UseShellExecute = true }); }
            catch { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + ConfigStore.FilePath + "\"") { UseShellExecute = true }); }
        }

        public void Quit()
        {
            Exiting = true;
            hotkey?.Dispose();
            Tray?.Dispose();
            Shutdown();
        }
    }

    internal sealed class TrayIcon : IDisposable
    {
        readonly System.Windows.Forms.NotifyIcon icon;
        readonly System.Windows.Forms.ToolStripMenuItem autostartItem;

        public TrayIcon(LauncherApp app)
        {
            icon = new System.Windows.Forms.NotifyIcon
            {
                Text = "SebLauncher",
                Icon = LoadIcon(),
                Visible = true
            };
            icon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) app.ToggleDock();
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Pokaż launcher", null, (s, e) => app.ShowDock(false));
            menu.Items.Add("Pokaż przypięty (do przeciągania plików)", null, (s, e) => app.ShowDock(true));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Skanuj programy i gry…", null, (s, e) => app.OpenScan());
            menu.Items.Add("Edytuj launcher.json", null, (s, e) => app.EditConfig());
            menu.Items.Add("Przeładuj konfigurację", null, (s, e) => app.ReloadConfig());
            var edge = new System.Windows.Forms.ToolStripMenuItem("Położenie paska");
            var edgeBottom = new System.Windows.Forms.ToolStripMenuItem("Na dole (nad paskiem zadań)");
            var edgeTop = new System.Windows.Forms.ToolStripMenuItem("Na górze ekranu");
            edgeBottom.Click += (s, e) => app.SetEdge("Bottom");
            edgeTop.Click += (s, e) => app.SetEdge("Top");
            edge.DropDownItems.Add(edgeBottom);
            edge.DropDownItems.Add(edgeTop);
            menu.Items.Add(edge);
            autostartItem = new System.Windows.Forms.ToolStripMenuItem("Uruchamiaj z Windows");
            autostartItem.Click += (s, e) =>
            {
                try { Autostart.Set(!autostartItem.Checked); } catch { }
                autostartItem.Checked = Autostart.IsEnabled();
            };
            menu.Items.Add(autostartItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Zamknij", null, (s, e) => app.Quit());
            menu.Opening += (s, e) =>
            {
                autostartItem.Checked = Autostart.IsEnabled();
                bool top = string.Equals(app.Config.Edge, "Top", StringComparison.OrdinalIgnoreCase);
                edgeTop.Checked = top;
                edgeBottom.Checked = !top;
            };
            icon.ContextMenuStrip = menu;
        }

        static System.Drawing.Icon LoadIcon()
        {
            try
            {
                var exe = Program.ExePath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) return ico;
                }
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        public void ShowBalloon(string title, string text) =>
            icon.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.Info);

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
