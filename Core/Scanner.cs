using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SebLauncher
{
    public class ScanResult
    {
        public string Source { get; set; } = "";      // "Programy", "Steam", "GOG", "Epic", "Ubisoft"
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; }
        public string WorkDir { get; set; }
        public string Icon { get; set; }
        public string Category { get; set; } = "Programy";
        public List<string> Candidates { get; set; } = new List<string>();
    }

    public static class Scanner
    {
        static readonly Regex ExcludeExe = new Regex(
            @"^(unins|uninst|uninstall|setup|install|vc_?redist|vcredist|dxsetup|dotnet|ndp\d|crashpad|crashreport|crashhandler|bugreport|errorreport|elevate|notification_helper|squirrel|createdump)" +
            @"|(installer|updater|update|helper|crashhandler|reporter|_x86|uninstall)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly string[] SkipDirs =
        {
            "Programs_old", "$Recycle.Bin", "System Volume Information", "Common Files", "WindowsApps", "ModifiableWindowsApps",
            "Windows Defender", "Windows Mail", "Windows Media Player", "Windows NT", "Windows Photo Viewer",
            "Windows Portable Devices", "Windows Security", "Windows Sidebar", "WindowsPowerShell", "Microsoft Update Health Tools",
            "Reference Assemblies", "MSBuild", "Uninstall Information", "Internet Explorer", "dotnet", "Microsoft.NET",
            "PackageManagement", "Windows Kits", "Microsoft SDKs", "IIS", "IIS Express", "Microsoft Visual Studio\\Installer"
        };

        // ---------- Programy (E:\Programs) ----------
        public static List<ScanResult> ScanPrograms(IEnumerable<string> roots, string source = "Programy")
        {
            var results = new List<ScanResult>();
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                IEnumerable<string> dirs;
                try { dirs = Directory.EnumerateDirectories(root); } catch { continue; }

                foreach (var dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var folder = System.IO.Path.GetFileName(dir);
                    if (SkipDirs.Any(x => folder.Equals(x, StringComparison.OrdinalIgnoreCase)) || folder.StartsWith("Programs_old", StringComparison.OrdinalIgnoreCase)) continue;

                    var exes = FindExes(dir, 3);
                    if (exes.Count == 0) continue;

                    var ranked = exes
                        .Select(e => new { Exe = e, Score = ScoreExe(e, dir, folder) })
                        .OrderByDescending(x => x.Score)
                        .Select(x => x.Exe)
                        .ToList();

                    var best = ranked[0];
                    results.Add(new ScanResult
                    {
                        Source = source,
                        Name = NiceName(best, folder),
                        Path = best,
                        WorkDir = System.IO.Path.GetDirectoryName(best),
                        Category = "Programy",
                        Candidates = ranked.Take(15).ToList()
                    });
                }
            }
            return results;
        }

        static List<string> FindExes(string dir, int depth)
        {
            var list = new List<string>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (!f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var n = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (!ExcludeExe.IsMatch(n)) list.Add(f);
                }
                if (depth > 1)
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        var sn = System.IO.Path.GetFileName(sub).ToLowerInvariant();
                        if (sn == "locales" || sn == "resources" || sn == "swiftshader" || sn == "redist" ||
                            sn == "_commonredist" || sn == "uninstall" || sn == "updater" || sn == "temp" || sn == "cache")
                            continue;
                        list.AddRange(FindExes(sub, depth - 1));
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>2 = GUI, 3 = konsola, 0 = nieznane.</summary>
        static int PeSubsystem(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 0x40 || br.ReadUInt16() != 0x5A4D) return 0; // "MZ"
                    fs.Position = 0x3C;
                    int pe = br.ReadInt32();
                    if (pe <= 0 || pe + 0x5C + 2 > fs.Length) return 0;
                    fs.Position = pe;
                    if (br.ReadUInt32() != 0x00004550) return 0;          // "PE\0\0"
                    fs.Position = pe + 4 + 20 + 68;                         // OptionalHeader.Subsystem (PE32 i PE32+)
                    return br.ReadUInt16();
                }
            }
            catch { return 0; }
        }

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        static double ScoreExe(string exe, string rootDir, string folder)
        {
            double score = 0;
            var name = Norm(System.IO.Path.GetFileNameWithoutExtension(exe));
            var fold = Norm(folder);
            if (name == fold) score += 100;
            else if (name.Length > 1 && fold.Length > 1 && (fold.Contains(name) || name.Contains(fold))) score += 50;

            var rel = exe.Substring(Math.Min(exe.Length, rootDir.TrimEnd('\\').Length + 1));
            int level = rel.Count(c => c == '\\');
            score += level == 0 ? 25 : (level == 1 ? 10 : 0);
            if (name.EndsWith("64")) score += 5;
            if (name.Contains("cli") || name.Contains("console") || name.Contains("cmd")) score -= 15;
            if (name.Contains("fullscreen") || name.Contains("server") || name.Contains("service")) score -= 5;
            if (name.Contains("desktop") || name.Contains("gui")) score += 3;
            var sub = PeSubsystem(exe);
            if (sub == 2) score += 30;        // aplikacja okienkowa
            else if (sub == 3) score -= 20;   // aplikacja konsolowa

            try
            {
                var len = new FileInfo(exe).Length;
                score += Math.Log10(Math.Max(len, 1)) * 3;
                var vi = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) score += 3;
            }
            catch { }
            return score;
        }

        public static string NiceName(string exe, string fallback)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                var d = vi.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(d) && d.Length <= 40 && d.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) < 0) return d;
                var p = vi.ProductName?.Trim();
                if (!string.IsNullOrEmpty(p) && p.Length <= 40) return p;
            }
            catch { }
            return fallback;
        }


        // ---------- C:\Program Files i Program Files (x86) ----------
        public static List<ScanResult> ScanProgramFiles()
        {
            return ScanPrograms(ProgramFilesRoots(), "Program Files");
        }

        public static List<string> ProgramFilesRoots()
        {
            var roots = new List<string>();
            void Add(string p)
            {
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !roots.Contains(p, StringComparer.OrdinalIgnoreCase)) roots.Add(p);
            }
            Add(Environment.GetEnvironmentVariable("ProgramW6432"));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            return roots;
        }

        // ---------- menu Start: Microsoft Store, wbudowane, pozostałe ----------
        static readonly string[] SkipStartNames = { "uninstall", "odinstaluj", "readme", "help", "pomoc", "release notes", "website", "documentation", "dokumentacja" };
        static readonly string[] RunnableExt = { ".exe", ".msc", ".cpl", ".lnk", ".bat", ".cmd" };

        /// <summary>excludeRoots – foldery już pokrywane innymi źródłami (E:\Programs, Program Files).</summary>
        public static List<ScanResult> ScanStartMenu(IEnumerable<string> excludeRoots)
        {
            var res = new List<ScanResult>();
            var exclude = excludeRoots.Where(r => !string.IsNullOrEmpty(r))
                .Select(r => r.TrimEnd('\\') + "\\").ToList();
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var app in GetStartApps())
            {
                var name = app.Key?.Trim();
                var id = app.Value?.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                var lower = name.ToLowerInvariant();
                if (SkipStartNames.Any(x => lower.Contains(x))) continue;
                if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

                string path = ResolveKnownFolder(id);
                if (path == null && id.Length > 2 && id[1] == ':') path = id;
                if (path != null && !File.Exists(path)) path = null;

                ScanResult r;
                if (path != null)
                {
                    if (Array.IndexOf(RunnableExt, System.IO.Path.GetExtension(path).ToLowerInvariant()) < 0) continue;
                    if (exclude.Any(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
                    bool builtIn = path.StartsWith(winDir, StringComparison.OrdinalIgnoreCase);
                    r = new ScanResult
                    {
                        Source = builtIn ? "Wbudowane" : "Menu Start",
                        Name = name,
                        Path = path,
                        WorkDir = System.IO.Path.GetDirectoryName(path),
                        Category = "Programy"
                    };
                }
                else
                {
                    int bang = id.IndexOf('!');
                    string family = bang > 0 ? id.Substring(0, bang) : id;
                    bool microsoft = family.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
                                  || family.StartsWith("windows.", StringComparison.OrdinalIgnoreCase)
                                  || family.StartsWith("MSTeams", StringComparison.OrdinalIgnoreCase)
                                  || family.StartsWith("Clipchamp", StringComparison.OrdinalIgnoreCase);
                    string source = bang > 0 ? (microsoft ? "Wbudowane" : "Microsoft Store")
                                             : (microsoft ? "Wbudowane" : "Menu Start");
                    r = new ScanResult
                    {
                        Source = source,
                        Name = name,
                        Path = "shell:AppsFolder\\" + id,
                        Category = "Programy"
                    };
                }
                if (seen.Add(r.Source + "|" + r.Path)) res.Add(r);
            }
            return res;
        }

        static string ResolveKnownFolder(string appId)
        {
            if (!appId.StartsWith("{")) return null;
            int close = appId.IndexOf('}');
            if (close < 0) return null;
            var guid = appId.Substring(1, close - 1).ToUpperInvariant();
            var rest = appId.Substring(close + 1).TrimStart('\\');
            string baseDir = null;
            switch (guid)
            {
                case "6D809377-6AF0-444B-8957-A3773F02200E": baseDir = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); break;
                case "905E63B6-C1BF-494E-B29C-65B732D3D21A": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); break;
                case "7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86); break;
                case "1AC14E77-02E7-4E5D-B744-2EB1AE5198B7": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.System); break;
                case "D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86); break;
                case "F38BF404-1D43-42F2-9305-67DE0B28FC23": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows); break;
                case "F1B32785-6FBA-4FCF-9D55-7B8E7F157091": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); break;
                case "3EB685DB-65F9-4CF6-A03A-E3EF65729F3D": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); break;
                case "62AB5D82-FDC1-4DC3-A9DD-070D1D495D97": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); break;
                case "5E6C858F-0E22-4760-9AFE-EA3317B67173": baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); break;
            }
            return string.IsNullOrEmpty(baseDir) ? null : System.IO.Path.Combine(baseDir, rest);
        }

        /// <summary>Lista z menu Start (Get-StartApps): nazwa -> AppID.</summary>
        public static List<KeyValuePair<string, string>> GetStartApps()
        {
            var list = new List<KeyValuePair<string, string>>();
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
                if (string.IsNullOrWhiteSpace(output)) return list;
                var parsed = MiniJson.Parse(output);
                var items = parsed as List<object> ?? new List<object> { parsed };
                foreach (var o in items.OfType<Dictionary<string, object>>())
                    list.Add(new KeyValuePair<string, string>(MiniJson.Str(o, "Name"), MiniJson.Str(o, "AppID")));
            }
            return list;
        }

        // ---------- Gry ----------
        public static List<ScanResult> ScanGames()
        {
            var all = new List<ScanResult>();
            void Try(Func<List<ScanResult>> f) { try { all.AddRange(f()); } catch (Exception ex) { Debug.WriteLine(ex); } }
            Try(ScanSteam);
            Try(ScanGog);
            Try(ScanEpic);
            Try(ScanUbisoft);
            return all;
        }

        static readonly Regex VdfPath = new Regex("\"path\"\\s+\"(?<p>[^\"]+)\"", RegexOptions.IgnoreCase);
        static string AcfValue(string text, string key)
        {
            var m = Regex.Match(text, "\"" + key + "\"\\s+\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value.Replace("\\\\", "\\") : null;
        }

        public static List<ScanResult> ScanSteam()
        {
            var res = new List<ScanResult>();
            string steamPath = null;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                steamPath = k?.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steamPath)) return res;
            steamPath = steamPath.Replace('/', '\\');

            var libs = new List<string> { steamPath };
            var vdf = System.IO.Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in VdfPath.Matches(File.ReadAllText(vdf)))
                    libs.Add(m.Groups["p"].Value.Replace("\\\\", "\\"));
            }

            var seen = new HashSet<string>();
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = System.IO.Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    string text;
                    try { text = File.ReadAllText(acf); } catch { continue; }
                    var id = AcfValue(text, "appid");
                    var name = AcfValue(text, "name");
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || !seen.Add(id)) continue;
                    if (Regex.IsMatch(name, "Redistributable|Steamworks|Proton|Steam Linux Runtime|SteamVR|Soundtrack", RegexOptions.IgnoreCase)) continue;

                    res.Add(new ScanResult
                    {
                        Source = "Steam",
                        Name = name,
                        Path = "steam://rungameid/" + id,
                        Icon = FindSteamIcon(steamPath, id) ?? FindGameExe(System.IO.Path.Combine(apps, "common", AcfValue(text, "installdir") ?? "")),
                        Category = "Gry"
                    });
                }
            }
            return res;
        }

        static readonly Regex Hex40 = new Regex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase);
        static string FindSteamIcon(string steamPath, string id)
        {
            var cache = System.IO.Path.Combine(steamPath, "appcache", "librarycache");
            var old = System.IO.Path.Combine(cache, id + "_icon.jpg");
            if (File.Exists(old)) return old;
            var dir = System.IO.Path.Combine(cache, id);
            if (Directory.Exists(dir))
            {
                var icon = Directory.EnumerateFiles(dir, "*.jpg")
                    .FirstOrDefault(f => Hex40.IsMatch(System.IO.Path.GetFileNameWithoutExtension(f)));
                if (icon != null) return icon;
                foreach (var n in new[] { "logo.png", "header.jpg", "library_600x900.jpg" })
                {
                    var p = System.IO.Path.Combine(dir, n);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        static string FindGameExe(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            try
            {
                var folder = Norm(System.IO.Path.GetFileName(dir.TrimEnd('\\')));
                return FindExes(dir, 3)
                    .OrderByDescending(e => (Norm(System.IO.Path.GetFileNameWithoutExtension(e)) == folder ? 1e12 : 0) + new FileInfo(e).Length)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        public static List<ScanResult> ScanGog()
        {
            var res = new List<ScanResult>();
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (root == null) return res;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                if (k == null) continue;
                var name = k.GetValue("gameName") as string;
                var exe = k.GetValue("exe") as string;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
                if (!string.IsNullOrEmpty(k.GetValue("dependsOn") as string)) continue; // pomija DLC
                res.Add(new ScanResult
                {
                    Source = "GOG",
                    Name = name,
                    Path = exe,
                    Args = k.GetValue("launchParam") as string,
                    WorkDir = (k.GetValue("workingDir") as string) ?? System.IO.Path.GetDirectoryName(exe),
                    Category = "Gry"
                });
            }
            return res;
        }

        public static List<ScanResult> ScanEpic()
        {
            var res = new List<ScanResult>();
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(dir)) return res;
            foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
            {
                try
                {
                    var r = MiniJson.Parse(File.ReadAllText(f)) as Dictionary<string, object>;
                    if (r == null) continue;
                    string S(string n) => MiniJson.Str(r, n);
                    bool B(string n) => MiniJson.Bool(r, n);

                    if (B("bIsIncompleteInstall")) continue;
                    var name = S("DisplayName");
                    var ns = S("CatalogNamespace");
                    var id = S("CatalogItemId");
                    var app = S("AppName");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(app)) continue;
                    if (S("MainGameAppName") is string main && !string.Equals(main, app, StringComparison.OrdinalIgnoreCase)) continue; // DLC

                    var loc = S("InstallLocation");
                    var exe = S("LaunchExecutable");
                    string icon = null;
                    if (!string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(exe))
                    {
                        var full = System.IO.Path.Combine(loc, exe);
                        if (File.Exists(full)) icon = full;
                    }
                    res.Add(new ScanResult
                    {
                        Source = "Epic",
                        Name = name,
                        Path = $"com.epicgames.launcher://apps/{ns}%3A{id}%3A{app}?action=launch&silent=true",
                        Icon = icon,
                        Category = "Gry"
                    });
                }
                catch { }
            }
            return res;
        }

        public static List<ScanResult> ScanUbisoft()
        {
            var res = new List<ScanResult>();
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (root == null) return res;
            foreach (var id in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(id);
                var dir = (k?.GetValue("InstallDir") as string)?.Replace('/', '\\');
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                string name = null, icon = null;
                using (var u = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install " + id))
                {
                    name = u?.GetValue("DisplayName") as string;
                    var di = (u?.GetValue("DisplayIcon") as string)?.Trim('"');
                    if (!string.IsNullOrEmpty(di))
                    {
                        var comma = di.LastIndexOf(',');
                        if (comma > 2) di = di.Substring(0, comma);
                        if (File.Exists(di)) icon = di;
                    }
                }
                if (string.IsNullOrEmpty(name)) name = System.IO.Path.GetFileName(dir.TrimEnd('\\'));

                res.Add(new ScanResult
                {
                    Source = "Ubisoft",
                    Name = name,
                    Path = "uplay://launch/" + id + "/0",
                    Icon = icon ?? FindGameExe(dir),
                    Category = "Gry"
                });
            }
            return res;
        }
    }
}
