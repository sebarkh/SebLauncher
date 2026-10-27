using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SebLauncher
{
    internal class DockWindow : Window
    {
        const double Frame = 24;      // przezroczysty margines na cień
        const double EdgeGap = 6;     // odstęp paska od krawędzi ekranu
        static readonly Color Accent = Color.FromRgb(0x3D, 0xDC, 0x97);
        static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly FontFamily GlyphFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        readonly LauncherApp app;
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<(Border Separator, FrameworkElement Group, List<Tile> Tiles)> groups = new List<(Border, FrameworkElement, List<Tile>)>();

        Border panel;
        TranslateTransform slide;
        StackPanel groupsHost;
        TextBlock caption;
        TextBlock pinGlyph;
        Rect screenRect = Rect.Empty;

        bool isShown, pinned, keyboardMode, renderingHooked;
        Tile pressTile, dragTile;
        Point pressPoint;
        bool dragging;
        int suppressHide;
        DateTime lastHidden = DateTime.MinValue;
        string filter = "";
        int selected = -1;

        sealed class Tile
        {
            public LauncherItem Item;
            public Grid Root;
            public StackPanel Row;
            public ScaleTransform Scale;
            public Border Glow;
            public double Current = 1;
        }

        LauncherConfig Cfg => app.Config;
        bool AtTop => !string.Equals(Cfg.Edge, "Bottom", StringComparison.OrdinalIgnoreCase);
        double S => Cfg.IconSize;

        public DockWindow(LauncherApp app)
        {
            this.app = app;
            Title = "SebLauncher";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = true;
            UseLayoutRounding = true;
            FontFamily = UiFont;
            AllowDrop = true;

            Deactivated += (s, e) => { if (!pinned && suppressHide == 0) HideDock(); };
            PreviewKeyDown += OnKeyDown;
            TextInput += OnTextInput;
            DragOver += OnDragOver;
            Drop += OnDrop;
            SizeChanged += (s, e) => { if (isShown) Recenter(); };

            Build();
        }

        // ================= budowanie UI =================

        public void Rebuild()
        {
            Build();
            if (isShown)
            {
                ApplyFilter();
                UpdateLayout();
                Recenter();
            }
        }

        void Build()
        {
            tiles.Clear();
            groups.Clear();
            double s = S;
            double zoomRoom = (s + 12) * (Cfg.MaxZoom - 1) * 0.92 + 2; // miejsce na powiększenie WEWNĄTRZ paska

            groupsHost = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = AtTop ? new Thickness(0, 0, 0, zoomRoom) : new Thickness(0, zoomRoom, 0, 0)
            };

            foreach (var cat in Cfg.Categories)
            {
                var items = Cfg.Items.Where(i => string.Equals(i.Category, cat, StringComparison.OrdinalIgnoreCase)).ToList();
                if (items.Count == 0) continue;

                Border sep = null;
                if (groups.Count > 0)
                {
                    sep = new Border
                    {
                        Width = 1,
                        Margin = new Thickness(s * 0.22, 10, s * 0.22, 4),
                        Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF))
                    };
                    groupsHost.Children.Add(sep);
                }

                var label = new TextBlock
                {
                    Text = cat.ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(8, AtTop ? 0 : 3, 0, AtTop ? 3 : 0)
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var groupTiles = new List<Tile>();
                foreach (var item in items)
                {
                    var t = CreateTile(item);
                    groupTiles.Add(t);
                    t.Row = row;
                    row.Children.Add(t.Root);
                }

                var group = new StackPanel { Orientation = Orientation.Vertical, Tag = cat, Background = Brushes.Transparent };
                if (AtTop) { group.Children.Add(label); group.Children.Add(row); }
                else { group.Children.Add(row); group.Children.Add(label); }
                groupsHost.Children.Add(group);
                groups.Add((sep, group, groupTiles));
            }

            if (tiles.Count == 0)
            {
                groupsHost.Children.Add(new TextBlock
                {
                    Text = "Pusto. Prawy klik na ikonie w zasobniku → „Skanuj programy i gry…”,\n" +
                           "albo przypnij pasek (pinezka) i przeciągnij tu pliki .exe / skróty.",
                    Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(24, 12, 24, 12),
                    FontSize = 13
                });
            }

            // --- wiersz z podpisem i pinezką ---
            caption = new TextBlock
            {
                FontSize = 13,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 560,
                Margin = new Thickness(30, 0, 30, 0)
            };
            pinGlyph = new TextBlock
            {
                FontFamily = GlyphFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var pinButton = new Border
            {
                Width = 26,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Przypnij – pasek nie chowa się sam (np. do przeciągania plików)",
                Child = pinGlyph
            };
            pinButton.MouseEnter += (s2, e) => pinButton.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            pinButton.MouseLeave += (s2, e) => pinButton.Background = Brushes.Transparent;
            pinButton.MouseLeftButtonUp += (s2, e) => { pinned = !pinned; UpdatePin(); e.Handled = true; };

            var captionRow = new Grid { Height = 24, Margin = new Thickness(2, AtTop ? 0 : 4, 0, AtTop ? 4 : 0) };
            captionRow.Children.Add(caption);
            captionRow.Children.Add(pinButton);

            var content = new StackPanel { Orientation = Orientation.Vertical };
            if (AtTop) { content.Children.Add(captionRow); content.Children.Add(groupsHost); }
            else { content.Children.Add(groupsHost); content.Children.Add(captionRow); }

            slide = new TranslateTransform();
            panel = new Border
            {
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(12 + (s + 12) * (Cfg.MaxZoom - 1) / 2, 8, 12 + (s + 12) * (Cfg.MaxZoom - 1) / 2, 10),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xF0, 0x24, 0x27, 0x2E),
                    Color.FromArgb(0xF0, 0x14, 0x16, 0x1A), 90),
                BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(0x70, Accent.R, Accent.G, Accent.B),
                    Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), AtTop ? 90 : 270),
                BorderThickness = new Thickness(1),
                Child = content,
                RenderTransform = slide,
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.55, Direction = 270, Color = Colors.Black },
                // ikony powiększają się "na zewnątrz" paska – zostaw na to miejsce w oknie
                Margin = new Thickness(Frame),
                MinWidth = 260
            };
            Content = panel;

            UpdatePin();
            UpdateCaption();
        }

        Tile CreateTile(LauncherItem item)
        {
            double s = S;
            var t = new Tile { Item = item };
            int px = (int)Math.Min(256, Math.Max(48, Math.Round(s * Cfg.MaxZoom * 1.5)));
            var img = IconLoader.Get(item.Icon, item.Path, px);

            FrameworkElement visual;
            if (img != null)
            {
                var image = new Image { Source = img, Width = s, Height = s, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                visual = image;
            }
            else
            {
                visual = new Border
                {
                    Width = s,
                    Height = s,
                    CornerRadius = new CornerRadius(s * 0.24),
                    Background = new SolidColorBrush(ColorFor(item.Name)),
                    Child = new TextBlock
                    {
                        Text = string.IsNullOrEmpty(item.Name) ? "?" : item.Name.Substring(0, 1).ToUpperInvariant(),
                        FontSize = s * 0.46,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
            }
            visual.HorizontalAlignment = HorizontalAlignment.Center;
            visual.VerticalAlignment = VerticalAlignment.Center;

            t.Glow = new Border
            {
                CornerRadius = new CornerRadius(s * 0.28),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent
            };

            t.Scale = new ScaleTransform(1, 1);
            t.Root = new Grid
            {
                Width = s + 12,
                Height = s + 12,
                Margin = new Thickness(s * 0.12, 0, s * 0.12, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                RenderTransform = t.Scale,
                RenderTransformOrigin = AtTop ? new Point(0.5, 0.0) : new Point(0.5, 1.0)
            };
            t.Root.Children.Add(t.Glow);
            t.Root.Children.Add(visual);

            t.Root.MouseEnter += (s2, e) => { keyboardMode = false; SetSelected(tiles.IndexOf(t)); };
            t.Root.MouseLeftButtonDown += (s2, e) =>
            {
                pressTile = t;
                pressPoint = e.GetPosition(this);
                e.Handled = true;
            };
            t.Root.MouseMove += (s2, e) =>
            {
                if (pressTile != t || dragging || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - pressPoint.X) > 8 || Math.Abs(p.Y - pressPoint.Y) > 8) BeginDrag(t);
            };
            t.Root.MouseLeftButtonUp += (s2, e) =>
            {
                e.Handled = true;
                if (dragging || pressTile != t) return;
                pressTile = null;
                bool admin = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift);
                LaunchTile(t, admin);
            };
            t.Root.ContextMenu = BuildContextMenu(t);
            tiles.Add(t);
            return t;
        }

        static Color ColorFor(string name)
        {
            Color[] palette =
            {
                Color.FromRgb(0x2E, 0x8B, 0x6E), Color.FromRgb(0x3A, 0x6E, 0xA5), Color.FromRgb(0x8E, 0x4F, 0xA8),
                Color.FromRgb(0xB5, 0x5D, 0x3C), Color.FromRgb(0x5C, 0x6B, 0x7A), Color.FromRgb(0xA8, 0x8A, 0x2E)
            };
            int h = 0;
            foreach (var c in name ?? "") h = unchecked(h * 31 + c);
            return palette[Math.Abs(h % palette.Length)];
        }

        ContextMenu BuildContextMenu(Tile t)
        {
            var m = new ContextMenu();
            MenuItem Item(string header, Action a, bool enabled = true)
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                mi.Click += (s, e) => a();
                return mi;
            }

            var item = t.Item;
            var p = IconLoader.Expand(item.Path);
            bool isFile = File.Exists(p) || Directory.Exists(p);

            m.Items.Add(Item("Uruchom", () => LaunchTile(t, false)));
            m.Items.Add(Item("Uruchom jako administrator", () => LaunchTile(t, true), isFile));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Otwórz lokalizację pliku", () => Launcher.OpenLocation(item),
                isFile || File.Exists(IconLoader.Expand(item.Icon) ?? "")));
            m.Items.Add(Item("Zmień nazwę…", () => Rename(item)));

            var move = new MenuItem { Header = "Przenieś do kategorii" };
            foreach (var cat in Cfg.Categories.Where(c => !string.Equals(c, item.Category, StringComparison.OrdinalIgnoreCase)))
            {
                var c = cat;
                move.Items.Add(Item(c, () => { item.Category = c; Commit(); }));
            }
            move.Items.Add(Item("Nowa kategoria…", () =>
            {
                var name = Ask("Nowa kategoria", "Nazwa kategorii:", "");
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();
                if (!Cfg.Categories.Contains(name, StringComparer.OrdinalIgnoreCase)) Cfg.Categories.Add(name);
                item.Category = name;
                Commit();
            }));
            m.Items.Add(move);
            m.Items.Add(Item("Przesuń w lewo", () => MoveItem(item, -1)));
            m.Items.Add(Item("Przesuń w prawo", () => MoveItem(item, +1)));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Usuń z launchera", () =>
            {
                suppressHide++;
                var r = MessageBox.Show(this, $"Usunąć „{item.Name}” z launchera?\n(Program nie zostanie odinstalowany.)",
                    "SebLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question);
                suppressHide--;
                if (r == MessageBoxResult.Yes) { Cfg.Items.Remove(item); Commit(); }
                Activate();
            }));
            return m;
        }

        void Commit()
        {
            app.SaveConfig();
            Rebuild();
        }

        void MoveItem(LauncherItem item, int dir)
        {
            var list = Cfg.Items;
            int i = list.IndexOf(item);
            int j = i + dir;
            while (j >= 0 && j < list.Count && !string.Equals(list[j].Category, item.Category, StringComparison.OrdinalIgnoreCase)) j += dir;
            if (i < 0 || j < 0 || j >= list.Count) return;
            list[i] = list[j];
            list[j] = item;
            Commit();
            SetSelected(tiles.FindIndex(x => x.Item == item));
        }

        void Rename(LauncherItem item)
        {
            var name = Ask("Zmień nazwę", "Nazwa:", item.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            item.Name = name.Trim();
            Commit();
        }

        string Ask(string title, string prompt, string value)
        {
            suppressHide++;
            try { return InputDialog.Ask(this, title, prompt, value); }
            finally { suppressHide--; Activate(); }
        }

        // ================= pokazywanie / chowanie =================

        public void Toggle()
        {
            if (isShown) HideDock();
            else if ((DateTime.Now - lastHidden).TotalMilliseconds > 350) ShowDock(false);
        }

        public void ShowDock(bool pin)
        {
            if (pin) { pinned = true; UpdatePin(); }
            if (isShown) { ForceActivate(); return; }

            isShown = true;
            filter = "";
            keyboardMode = false;
            ApplyFilter();
            SetSelected(-1);

            Opacity = 0;
            Show();
            UpdateLayout();
            PlaceOnScreen();
            ForceActivate();

            if (!renderingHooked) { CompositionTarget.Rendering += OnRendering; renderingHooked = true; }

            double from = AtTop ? -(panel.ActualHeight + Frame + 10) : panel.ActualHeight + Frame + 10;
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            Opacity = 1;
        }

        public void HideDock()
        {
            if (!isShown) return;
            isShown = false;
            pinned = false;
            UpdatePin();
            lastHidden = DateTime.Now;

            double to = AtTop ? -(panel.ActualHeight + Frame + 10) : panel.ActualHeight + Frame + 10;
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            anim.Completed += (s, e) =>
            {
                if (isShown) return;
                Hide();
                if (renderingHooked) { CompositionTarget.Rendering -= OnRendering; renderingHooked = false; }
                foreach (var t in tiles) { t.Current = 1; t.Scale.ScaleX = t.Scale.ScaleY = 1; }
            };
            slide.BeginAnimation(TranslateTransform.YProperty, anim);
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)));
        }

        void ForceActivate()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) NativeMethods.SetForegroundWindow(h);
            Activate();
            Focus();
            Keyboard.Focus(this);
        }

        void PlaceOnScreen()
        {
            var cur = System.Windows.Forms.Cursor.Position;
            var wa = System.Windows.Forms.Screen.FromPoint(cur).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            screenRect = new Rect(wa.Left / dpi.DpiScaleX, wa.Top / dpi.DpiScaleY, wa.Width / dpi.DpiScaleX, wa.Height / dpi.DpiScaleY);
            Recenter();
        }

        void Recenter()
        {
            if (screenRect.IsEmpty) return;
            Left = screenRect.Left + (screenRect.Width - ActualWidth) / 2;
            Top = AtTop ? screenRect.Top + EdgeGap - Frame
                        : screenRect.Bottom - ActualHeight + Frame - EdgeGap;
        }

        void UpdatePin()
        {
            if (pinGlyph == null) return;
            pinGlyph.Text = pinned ? "" : ""; // PinFill / Pin
            pinGlyph.Foreground = pinned ? new SolidColorBrush(Accent) : new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
        }

        // ================= efekt powiększenia =================

        void OnRendering(object sender, EventArgs e)
        {
            if (!isShown || groupsHost == null) return;
            double radius = S * 2.4;
            double extra = Cfg.MaxZoom - 1;
            bool mouseIn = panel.IsMouseOver && !keyboardMode;
            double mouseX = mouseIn ? Mouse.GetPosition(groupsHost).X : 0;

            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                double target = 1;
                if (t.Root.IsVisible)
                {
                    if (mouseIn)
                    {
                        // środek kafelka w układzie bez transformacji
                        var p = t.Root.TranslatePoint(new Point(0, 0), groupsHost);
                        double centerX = p.X + t.Root.ActualWidth * t.Current / 2;
                        double d = Math.Abs(mouseX - centerX);
                        double f = d < radius ? (Math.Cos(Math.PI * d / radius) + 1) / 2 : 0;
                        target = 1 + extra * f;
                    }
                    else if (keyboardMode && i == selected)
                    {
                        target = 1 + extra * 0.7;
                    }
                }
                t.Current += (target - t.Current) * 0.3;
                if (Math.Abs(t.Current - target) < 0.002) t.Current = target;
                t.Scale.ScaleX = t.Scale.ScaleY = t.Current;
            }
        }

        // ================= zaznaczenie / podpis / filtr =================

        void SetSelected(int index)
        {
            selected = index;
            for (int i = 0; i < tiles.Count; i++)
            {
                bool on = i == selected;
                tiles[i].Glow.BorderBrush = Brushes.Transparent; // bez obramówki zaznaczenia
                tiles[i].Glow.Background = Brushes.Transparent;
            }
            UpdateCaption();
        }

        void UpdateCaption()
        {
            if (caption == null) return;
            string name = selected >= 0 && selected < tiles.Count ? tiles[selected].Item.Name : null;
            if (!string.IsNullOrEmpty(filter))
            {
                caption.Text = "🔍 " + filter + (name != null ? "   →   " + name : "   (brak wyników)");
                caption.Opacity = 1;
            }
            else if (name != null)
            {
                caption.Text = name;
                caption.Opacity = 1;
            }
            else
            {
                caption.Text = tiles.Count > 0 ? "Pisz, aby filtrować  ·  ←/→ i Enter  ·  Ctrl+Shift+Enter = admin" : "SebLauncher";
                caption.Opacity = 0.55;
            }
        }

        void ApplyFilter()
        {
            var f = filter.Trim();
            foreach (var t in tiles)
                t.Root.Visibility = Matches(t.Item.Name, f) ? Visibility.Visible : Visibility.Collapsed;

            bool anyBefore = false;
            foreach (var g in groups)
            {
                bool vis = g.Tiles.Any(t => t.Root.Visibility == Visibility.Visible);
                g.Group.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                if (g.Separator != null) g.Separator.Visibility = vis && anyBefore ? Visibility.Visible : Visibility.Collapsed;
                anyBefore |= vis;
            }

            if (f.Length > 0)
            {
                keyboardMode = true;
                SetSelected(tiles.FindIndex(t => t.Root.Visibility == Visibility.Visible));
            }
            else UpdateCaption();
        }

        static bool Matches(string name, string f)
        {
            if (string.IsNullOrEmpty(f)) return true;
            name ??= "";
            if (name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            // inicjały, np. "tc" -> Total Commander
            var initials = new string(name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w[0]).ToArray());
            return initials.StartsWith(f, StringComparison.CurrentCultureIgnoreCase);
        }

        void MoveSelection(int dir)
        {
            keyboardMode = true;
            var visible = Enumerable.Range(0, tiles.Count).Where(i => tiles[i].Root.Visibility == Visibility.Visible).ToList();
            if (visible.Count == 0) return;
            int pos = visible.IndexOf(selected);
            pos = pos < 0 ? (dir > 0 ? 0 : visible.Count - 1) : (pos + dir + visible.Count) % visible.Count;
            SetSelected(visible[pos]);
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (filter.Length > 0) { filter = ""; ApplyFilter(); SetSelected(-1); }
                    else HideDock();
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Down:
                    MoveSelection(+1); e.Handled = true; break;
                case Key.Left:
                case Key.Up:
                    MoveSelection(-1); e.Handled = true; break;
                case Key.Tab:
                    MoveSelection((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : +1); e.Handled = true; break;
                case Key.Enter:
                    if (selected >= 0 && selected < tiles.Count && tiles[selected].Root.Visibility == Visibility.Visible)
                    {
                        bool admin = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift);
                        LaunchTile(tiles[selected], admin);
                    }
                    e.Handled = true;
                    break;
                case Key.Back:
                    if (filter.Length > 0) { filter = filter.Substring(0, filter.Length - 1); ApplyFilter(); }
                    e.Handled = true;
                    break;
            }
        }

        void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            filter += e.Text;
            ApplyFilter();
            e.Handled = true;
        }

        // ================= uruchamianie =================

        void LaunchTile(Tile t, bool admin)
        {
            try
            {
                Pulse(t);
                Launcher.Launch(t.Item, admin);
                if (Cfg.HideAfterLaunch && !pinned) HideDock();
            }
            catch (Exception ex)
            {
                suppressHide++;
                MessageBox.Show(this, $"Nie udało się uruchomić „{t.Item.Name}”:\n{ex.Message}\n\nŚcieżka: {t.Item.Path}",
                    "SebLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
                suppressHide--;
            }
        }

        void Pulse(Tile t)
        {
            var anim = new ColorAnimation(Color.FromArgb(0x90, Accent.R, Accent.G, Accent.B), Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B),
                TimeSpan.FromMilliseconds(450));
            var brush = new SolidColorBrush();
            t.Glow.Background = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        // ================= zmiana kolejności przeciąganiem ikon =================

        void BeginDrag(Tile t)
        {
            dragging = true;
            dragTile = t;
            t.Root.Opacity = 0.45;
            panel.MouseMove += OnDragMove;
            panel.MouseLeftButtonUp += OnDragUp;
            panel.LostMouseCapture += OnDragLost;
            panel.CaptureMouse();
        }

        void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!dragging || dragTile == null) return;
            double x = e.GetPosition(groupsHost).X;
            foreach (var t in tiles)
            {
                if (t == dragTile || !t.Root.IsVisible) continue;
                var p = t.Root.TranslatePoint(new Point(0, 0), groupsHost);
                double w = t.Root.ActualWidth * t.Current;
                double left = p.X, right = p.X + w;
                if (x < left || x > right) continue;

                bool before = x < (left + right) / 2;
                int targetIndex = t.Row.Children.IndexOf(t.Root);
                int dragIndex = dragTile.Row == t.Row ? t.Row.Children.IndexOf(dragTile.Root) : -100;
                if (before && dragIndex == targetIndex - 1) return;   // już stoi przed
                if (!before && dragIndex == targetIndex + 1) return;  // już stoi za

                // konfiguracja
                var list = Cfg.Items;
                list.Remove(dragTile.Item);
                int idx = list.IndexOf(t.Item);
                list.Insert(before ? idx : idx + 1, dragTile.Item);
                dragTile.Item.Category = t.Item.Category;

                // widok
                dragTile.Row.Children.Remove(dragTile.Root);
                targetIndex = t.Row.Children.IndexOf(t.Root);
                t.Row.Children.Insert(before ? targetIndex : targetIndex + 1, dragTile.Root);
                dragTile.Row = t.Row;
                tiles.Remove(dragTile);
                tiles.Insert(tiles.IndexOf(t) + (before ? 0 : 1), dragTile);
                return;
            }
        }

        void OnDragUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            EndDrag();
        }

        void OnDragLost(object sender, MouseEventArgs e) => EndDrag();

        void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            panel.MouseMove -= OnDragMove;
            panel.MouseLeftButtonUp -= OnDragUp;
            panel.LostMouseCapture -= OnDragLost;
            if (panel.IsMouseCaptured) panel.ReleaseMouseCapture();
            if (dragTile != null) dragTile.Root.Opacity = 1;
            dragTile = null;
            pressTile = null;
            Commit();   // zapis kolejności + odświeżenie paska
        }

        // ================= przeciągnij i upuść =================

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0) return;
            string cat = FindCategory(e.OriginalSource as DependencyObject) ?? Cfg.Categories[0];
            int added = 0;
            foreach (var f in files)
            {
                if (ConfigStore.ContainsPath(Cfg, f)) continue;
                Cfg.Items.Add(new LauncherItem
                {
                    Name = NameForFile(f),
                    Path = f,
                    WorkDir = File.Exists(f) ? Path.GetDirectoryName(f) : null,
                    Category = cat
                });
                added++;
            }
            if (added > 0) Commit();
            e.Handled = true;
        }

        static string NameForFile(string f)
        {
            if (f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var d = FileVersionInfo.GetVersionInfo(f).FileDescription?.Trim();
                    if (!string.IsNullOrEmpty(d) && d.Length <= 40) return d;
                }
                catch { }
            }
            return Directory.Exists(f) ? Path.GetFileName(f.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(f);
        }

        static string FindCategory(DependencyObject d)
        {
            while (d != null)
            {
                if (d is FrameworkElement fe && fe.Tag is string c) return c;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }
    }
}
