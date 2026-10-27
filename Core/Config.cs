using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SebLauncher
{
    public class LauncherItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; }
        public string WorkDir { get; set; }
        public string Icon { get; set; }
        public string Category { get; set; } = "Programy";
        public bool Admin { get; set; }
    }

    public class LauncherConfig
    {
        /// <summary>Np. "Ctrl+Alt+Space", "Win+Shift+L", "Ctrl+F12".</summary>
        public string Hotkey { get; set; } = "Ctrl+Alt+Space";
        /// <summary>"Top" albo "Bottom".</summary>
        public string Edge { get; set; } = "Bottom";
        public double IconSize { get; set; } = 52;
        public double MaxZoom { get; set; } = 1.45;
        public bool HideAfterLaunch { get; set; } = true;
        public List<string> ScanRoots { get; set; } = new List<string> { @"E:\Programs" };
        public List<string> Categories { get; set; } = new List<string> { "Programy", "Gry" };
        /// <summary>Źródła pokazywane w oknie skanowania: Programy, Program Files, Wbudowane, Microsoft Store, Menu Start, Gry.</summary>
        public List<string> ScanSources { get; set; } = new List<string> { "Programy", "Gry" };
        public List<LauncherItem> Items { get; set; } = new List<LauncherItem>();
    }

    public static class ConfigStore
    {
        public static string FilePath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.json");

        public static LauncherConfig Load()
        {
            LauncherConfig cfg;
            if (File.Exists(FilePath))
                cfg = FromJson(File.ReadAllText(FilePath, Encoding.UTF8));
            else
            {
                cfg = new LauncherConfig();
                Save(cfg);
            }
            Normalize(cfg);
            return cfg;
        }

        public static LauncherConfig FromJson(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("Plik nie zawiera obiektu JSON.");
            var def = new LauncherConfig();
            var cfg = new LauncherConfig
            {
                Hotkey = MiniJson.Str(root, "Hotkey") ?? def.Hotkey,
                Edge = MiniJson.Str(root, "Edge") ?? def.Edge,
                IconSize = MiniJson.Num(root, "IconSize", def.IconSize),
                MaxZoom = MiniJson.Num(root, "MaxZoom", def.MaxZoom),
                HideAfterLaunch = MiniJson.Bool(root, "HideAfterLaunch", def.HideAfterLaunch)
            };
            if (root.TryGetValue("ScanRoots", out var sr) && sr is List<object> srl)
                cfg.ScanRoots = srl.OfType<string>().ToList();
            if (root.TryGetValue("ScanSources", out var ss) && ss is List<object> ssl)
                cfg.ScanSources = ssl.OfType<string>().ToList();
            if (root.TryGetValue("Categories", out var cr) && cr is List<object> crl)
                cfg.Categories = crl.OfType<string>().ToList();
            if (root.TryGetValue("Items", out var ir) && ir is List<object> irl)
            {
                cfg.Items = irl.OfType<Dictionary<string, object>>().Select(d => new LauncherItem
                {
                    Name = MiniJson.Str(d, "Name") ?? "",
                    Path = MiniJson.Str(d, "Path") ?? "",
                    Args = MiniJson.Str(d, "Args"),
                    WorkDir = MiniJson.Str(d, "WorkDir"),
                    Icon = MiniJson.Str(d, "Icon"),
                    Category = MiniJson.Str(d, "Category"),
                    Admin = MiniJson.Bool(d, "Admin")
                }).ToList();
            }
            return cfg;
        }

        public static string ToJson(LauncherConfig cfg)
        {
            var items = new List<object>();
            foreach (var i in cfg.Items)
            {
                var d = new Dictionary<string, object> { ["Name"] = i.Name, ["Path"] = i.Path };
                if (!string.IsNullOrEmpty(i.Args)) d["Args"] = i.Args;
                if (!string.IsNullOrEmpty(i.WorkDir)) d["WorkDir"] = i.WorkDir;
                if (!string.IsNullOrEmpty(i.Icon)) d["Icon"] = i.Icon;
                d["Category"] = i.Category;
                if (i.Admin) d["Admin"] = true;
                items.Add(d);
            }
            var root = new Dictionary<string, object>
            {
                ["Hotkey"] = cfg.Hotkey,
                ["Edge"] = cfg.Edge,
                ["IconSize"] = cfg.IconSize,
                ["MaxZoom"] = cfg.MaxZoom,
                ["HideAfterLaunch"] = cfg.HideAfterLaunch,
                ["ScanRoots"] = cfg.ScanRoots.Cast<object>().ToList(),
                ["Categories"] = cfg.Categories.Cast<object>().ToList(),
                ["ScanSources"] = cfg.ScanSources.Cast<object>().ToList(),
                ["Items"] = items
            };
            return MiniJson.Serialize(root);
        }

        public static void Normalize(LauncherConfig cfg)
        {
            if (cfg.Items == null) cfg.Items = new List<LauncherItem>();
            if (cfg.Categories == null || cfg.Categories.Count == 0) cfg.Categories = new List<string> { "Programy", "Gry" };
            if (cfg.ScanRoots == null) cfg.ScanRoots = new List<string>();
            if (cfg.ScanSources == null) cfg.ScanSources = new List<string> { "Programy", "Gry" };
            if (cfg.IconSize < 24 || cfg.IconSize > 128) cfg.IconSize = 52;
            if (cfg.MaxZoom < 1.0 || cfg.MaxZoom > 2.0) cfg.MaxZoom = 1.45;
            cfg.Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Path));
            foreach (var item in cfg.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Category)) item.Category = cfg.Categories[0];
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = System.IO.Path.GetFileNameWithoutExtension(item.Path);
                if (!cfg.Categories.Contains(item.Category, StringComparer.OrdinalIgnoreCase))
                    cfg.Categories.Add(item.Category);
            }
        }

        public static void Save(LauncherConfig cfg)
        {
            var json = ToJson(cfg);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        public static bool ContainsPath(LauncherConfig cfg, string path)
        {
            return cfg.Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
