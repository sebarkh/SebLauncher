using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SebLauncher
{
    internal class ScanWindow : Window
    {
        sealed class Row
        {
            public ScanResult R;
            public bool Existing;
            public Grid Root;
            public CheckBox Check;
            public TextBox NameBox;
            public ComboBox PathBox;
            public ComboBox CategoryBox;
            public Image Icon;
            public Group Group;
        }

        sealed class Group
        {
            public string Source;
            public string FilterKey;
            public StackPanel Panel;
            public List<Row> Rows = new List<Row>();
        }

        // klucz filtra -> opis przełącznika
        static readonly (string Key, string Label)[] Filters =
        {
            ("Programy", null),                 // etykieta = ScanRoots
            ("Program Files", "C:\\Program Files (64 i 32 bit)"),
            ("Wbudowane", "Wbudowane w Windows"),
            ("Microsoft Store", "Microsoft Store"),
            ("Menu Start", "Pozostałe z menu Start"),
            ("Gry", "Gry (Steam, GOG, Epic, Ubisoft)")
        };

        static readonly string[] SourceOrder = { "Programy", "Program Files", "Wbudowane", "Microsoft Store", "Menu Start", "Steam", "GOG", "Epic", "Ubisoft" };

        readonly LauncherApp app;
        readonly List<Row> rows = new List<Row>();
        readonly List<Group> groups = new List<Group>();
        readonly Dictionary<string, CheckBox> filterBoxes = new Dictionary<string, CheckBox>();
        readonly StackPanel list = new StackPanel { Margin = new Thickness(12, 0, 12, 12) };
        readonly TextBlock status = new TextBlock { Margin = new Thickness(14, 12, 14, 6), FontSize = 13, TextWrapping = TextWrapping.Wrap };
        readonly TextBox search = new TextBox { Width = 220, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2) };
        readonly Button addButton = new Button { Content = "Dodaj zaznaczone", Padding = new Thickness(14, 5, 14, 5), IsDefault = true, IsEnabled = false };

        public ScanWindow(LauncherApp app)
        {
            this.app = app;
            Title = "SebLauncher – dodaj programy i gry";
            Width = 980;
            Height = 720;
            MinWidth = 700;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            // --- pasek filtrów ---
            var filterBar = new WrapPanel { Margin = new Thickness(14, 0, 14, 8) };
            filterBar.Children.Add(new TextBlock { Text = "Pokaż:", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (var f in Filters)
            {
                var label = f.Label ?? string.Join(", ", app.Config.ScanRoots);
                var cb = new CheckBox
                {
                    Content = label,
                    Tag = label,
                    IsChecked = app.Config.ScanSources.Contains(f.Key, StringComparer.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 3, 16, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                cb.Checked += (s, e) => FiltersChanged();
                cb.Unchecked += (s, e) => FiltersChanged();
                filterBoxes[f.Key] = cb;
                filterBar.Children.Add(cb);
            }

            var searchBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 6) };
            searchBar.Children.Add(new TextBlock { Text = "Szukaj:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            searchBar.Children.Add(search);
            search.TextChanged += (s, e) => ApplyVisibility();

            var top = new StackPanel();
            top.Children.Add(status);
            top.Children.Add(filterBar);
            top.Children.Add(searchBar);

            // --- przyciski ---
            var selectVisible = new Button { Content = "Zaznacz widoczne", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
            var selectNone = new Button { Content = "Odznacz wszystko", Padding = new Thickness(10, 5, 10, 5) };
            var cancel = new Button { Content = "Anuluj", Padding = new Thickness(14, 5, 14, 5), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            selectVisible.Click += (s, e) => { foreach (var r in rows.Where(r => !r.Existing && r.Root.IsVisible)) r.Check.IsChecked = true; };
            selectNone.Click += (s, e) => { foreach (var r in rows.Where(r => !r.Existing)) r.Check.IsChecked = false; };
            cancel.Click += (s, e) => Close();
            addButton.Click += (s, e) => AddSelected();

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(selectVisible);
            left.Children.Add(selectNone);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(addButton);
            right.Children.Add(cancel);
            var bottom = new Grid { Margin = new Thickness(12) };
            bottom.Children.Add(left);
            bottom.Children.Add(right);

            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            dock.Children.Add(top);
            dock.Children.Add(bottom);
            dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;

            status.Text = "Skanowanie programów, menu Start i bibliotek gier…";
            Loaded += async (s, e) => await RunScan();
        }

        static string FilterKeyFor(string source) =>
            source == "Steam" || source == "GOG" || source == "Epic" || source == "Ubisoft" ? "Gry" : source;

        async Task RunScan()
        {
            var roots = app.Config.ScanRoots.ToList();
            var errors = new List<string>();
            List<ScanResult> found = await Task.Run(() =>
            {
                var all = new List<ScanResult>();
                void Try(string what, Func<IEnumerable<ScanResult>> f)
                {
                    try { all.AddRange(f()); }
                    catch (Exception ex) { lock (errors) errors.Add(what + ": " + ex.Message); }
                }
                Try("E:\\Programs", () => Scanner.ScanPrograms(roots));
                Try("Program Files", Scanner.ScanProgramFiles);
                Try("menu Start", () => Scanner.ScanStartMenu(roots.Concat(Scanner.ProgramFilesRoots())));
                Try("gry", Scanner.ScanGames);
                return all;
            });

            foreach (var src in SourceOrder)
            {
                var part = found.Where(f => f.Source == src).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (part.Count == 0) continue;
                var g = new Group { Source = src, FilterKey = FilterKeyFor(src), Panel = new StackPanel() };
                g.Panel.Children.Add(new TextBlock
                {
                    Text = HeaderFor(src) + "  (" + part.Count + ")",
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 14, 0, 6)
                });
                foreach (var r in part)
                {
                    var row = CreateRow(r);
                    row.Group = g;
                    g.Rows.Add(row);
                    g.Panel.Children.Add(row.Root);
                }
                groups.Add(g);
                list.Children.Add(g.Panel);
            }

            // liczniki na przełącznikach
            foreach (var f in Filters)
            {
                int n = groups.Where(g => g.FilterKey == f.Key).Sum(g => g.Rows.Count);
                filterBoxes[f.Key].Content = filterBoxes[f.Key].Tag + "  (" + n + ")";
            }

            int fresh = rows.Count(r => !r.Existing);
            status.Text = (rows.Count == 0
                    ? "Nic nie znaleziono."
                    : $"Znaleziono {rows.Count} pozycji ({fresh} nowych). Zaznacz, co ma trafić do launchera. " +
                      "Przy programach z folderów możesz wybrać inny plik .exe z listy i poprawić nazwę.")
                + (errors.Count > 0 ? "\nNie wszystko się udało: " + string.Join("; ", errors) : "");
            ApplyVisibility();
        }

        string HeaderFor(string src)
        {
            switch (src)
            {
                case "Programy": return string.Join(", ", app.Config.ScanRoots);
                case "Program Files": return "C:\\Program Files i Program Files (x86)";
                case "Wbudowane": return "Wbudowane w Windows";
                case "Menu Start": return "Pozostałe z menu Start";
                default: return src;
            }
        }

        void FiltersChanged()
        {
            app.Config.ScanSources = filterBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
            app.SaveConfig();
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            var q = search.Text.Trim();
            foreach (var g in groups)
            {
                bool on = filterBoxes.TryGetValue(g.FilterKey, out var cb) && cb.IsChecked == true;
                int visibleRows = 0;
                foreach (var r in g.Rows)
                {
                    bool match = q.Length == 0
                        || r.NameBox.Text.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                        || r.R.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    r.Root.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    if (match) visibleRows++;
                }
                g.Panel.Visibility = on && visibleRows > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateAddButton();
        }

        Row CreateRow(ScanResult r)
        {
            var row = new Row { R = r, Existing = ConfigStore.ContainsPath(app.Config, r.Path) };

            row.Check = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsChecked = row.Existing, IsEnabled = !row.Existing };
            row.Check.Checked += (s, e) => UpdateAddButton();
            row.Check.Unchecked += (s, e) => UpdateAddButton();

            row.Icon = new Image { Width = 24, Height = 24, Margin = new Thickness(8, 0, 8, 0), Source = IconLoader.Get(r.Icon, r.Path, 48) };
            RenderOptions.SetBitmapScalingMode(row.Icon, BitmapScalingMode.HighQuality);

            row.NameBox = new TextBox { Text = r.Name, Width = 240, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !row.Existing };

            FrameworkElement pathElement;
            if (r.Candidates.Count > 1 && !row.Existing)
            {
                row.PathBox = new ComboBox { ItemsSource = r.Candidates, SelectedItem = r.Path, Margin = new Thickness(8, 0, 8, 0) };
                row.PathBox.SelectionChanged += (s, e) =>
                {
                    if (!(row.PathBox.SelectedItem is string p)) return;
                    row.NameBox.Text = Scanner.NiceName(p, Path.GetFileName(Path.GetDirectoryName(p)));
                    row.Icon.Source = IconLoader.Get(null, p, 48);
                };
                pathElement = row.PathBox;
            }
            else
            {
                var shown = r.Path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)
                    ? "aplikacja: " + r.Path.Substring("shell:AppsFolder\\".Length)
                    : r.Path;
                pathElement = new TextBlock
                {
                    Text = (row.Existing ? "[już dodane]  " : "") + shown,
                    Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = row.Existing ? Brushes.Gray : Brushes.DimGray,
                    ToolTip = r.Path
                };
            }

            var cats = app.Config.Categories.ToList();
            if (!cats.Contains(r.Category, StringComparer.OrdinalIgnoreCase)) cats.Add(r.Category);
            row.CategoryBox = new ComboBox
            {
                ItemsSource = cats,
                SelectedItem = cats.First(c => string.Equals(c, r.Category, StringComparison.OrdinalIgnoreCase)),
                Width = 110,
                IsEnabled = !row.Existing
            };

            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(row.Check, 0);
            Grid.SetColumn(row.Icon, 1);
            Grid.SetColumn(row.NameBox, 2);
            Grid.SetColumn(pathElement, 3);
            Grid.SetColumn(row.CategoryBox, 4);
            grid.Children.Add(row.Check);
            grid.Children.Add(row.Icon);
            grid.Children.Add(row.NameBox);
            grid.Children.Add(pathElement);
            grid.Children.Add(row.CategoryBox);
            row.Root = grid;

            rows.Add(row);
            return row;
        }

        IEnumerable<Row> SelectedRows() =>
            rows.Where(r => !r.Existing && r.Check.IsChecked == true && r.Group != null && r.Group.Panel.Visibility == Visibility.Visible);

        void UpdateAddButton()
        {
            int n = SelectedRows().Count();
            addButton.Content = n > 0 ? $"Dodaj zaznaczone ({n})" : "Dodaj zaznaczone";
            addButton.IsEnabled = n > 0;
        }

        void AddSelected()
        {
            var cfg = app.Config;
            int added = 0;
            foreach (var row in SelectedRows().ToList())
            {
                var path = (row.PathBox?.SelectedItem as string) ?? row.R.Path;
                if (ConfigStore.ContainsPath(cfg, path)) continue;
                var cat = (row.CategoryBox.SelectedItem as string) ?? row.R.Category;
                if (!cfg.Categories.Contains(cat, StringComparer.OrdinalIgnoreCase)) cfg.Categories.Add(cat);
                bool isFolderScan = row.R.Source == "Programy" || row.R.Source == "Program Files";
                cfg.Items.Add(new LauncherItem
                {
                    Name = string.IsNullOrWhiteSpace(row.NameBox.Text) ? row.R.Name : row.NameBox.Text.Trim(),
                    Path = path,
                    Args = row.R.Args,
                    WorkDir = isFolderScan ? Path.GetDirectoryName(path) : row.R.WorkDir,
                    Icon = isFolderScan ? null : row.R.Icon,
                    Category = cat
                });
                added++;
            }
            if (added > 0)
            {
                app.SaveConfig();
                app.Dock.Rebuild();
                app.Tray.ShowBalloon("SebLauncher", $"Dodano {added} pozycji.");
            }
            Close();
        }
    }
}
