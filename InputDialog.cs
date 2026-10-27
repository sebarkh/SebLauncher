using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SebLauncher
{
    internal class InputDialog : Window
    {
        readonly TextBox box;

        InputDialog(string title, string prompt, string value)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.ToolWindow;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = false;

            box = new TextBox { Text = value ?? "", Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(4, 3, 4, 3) };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Anuluj", Width = 80, IsCancel = true };
            ok.Click += (s, e) => { DialogResult = true; };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = prompt });
            root.Children.Add(box);
            root.Children.Add(buttons);
            Content = root;

            Loaded += (s, e) => { box.SelectAll(); box.Focus(); Keyboard.Focus(box); };
        }

        public static string Ask(Window owner, string title, string prompt, string value)
        {
            var d = new InputDialog(title, prompt, value);
            if (owner != null && owner.IsVisible) d.Owner = owner;
            return d.ShowDialog() == true ? d.box.Text : null;
        }
    }
}
