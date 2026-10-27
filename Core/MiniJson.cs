using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SebLauncher
{
    /// <summary>Mały parser/serializator JSON (obiekty -> Dictionary, tablice -> List). Obsługuje komentarze // i /* */ oraz przecinki na końcu.</summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            var p = new Parser(text ?? "");
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("Nieoczekiwane znaki po końcu danych");
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string s) { this.s = s; if (s.Length > 0 && s[0] == '﻿') i = 1; }
            public bool End => i >= s.Length;

            public FormatException Error(string msg)
            {
                int line = 1, col = 1;
                for (int k = 0; k < Math.Min(i, s.Length); k++) { if (s[k] == '\n') { line++; col = 1; } else col++; }
                return new FormatException($"{msg} (wiersz {line}, kolumna {col})");
            }

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                    {
                        int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        i = end < 0 ? s.Length : end + 2;
                        continue;
                    }
                    break;
                }
            }

            public object ReadValue()
            {
                if (End) throw Error("Nieoczekiwany koniec danych");
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't' && Match("true")) return true;
                if (c == 'f' && Match("false")) return false;
                if (c == 'n' && Match("null")) return null;
                if (c == '-' || char.IsDigit(c)) return ReadNumber();
                throw Error($"Nieoczekiwany znak '{c}'");
            }

            bool Match(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) == 0) { i += word.Length; return true; }
                return false;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                i++; // {
                while (true)
                {
                    SkipWs();
                    if (End) throw Error("Brak '}'");
                    if (s[i] == '}') { i++; return d; }
                    if (s[i] != '"') throw Error("Oczekiwano nazwy pola w cudzysłowie");
                    var key = ReadString();
                    SkipWs();
                    if (End || s[i] != ':') throw Error("Oczekiwano ':'");
                    i++;
                    SkipWs();
                    d[key] = ReadValue();
                    SkipWs();
                    if (End) throw Error("Brak '}'");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("Oczekiwano ',' albo '}'");
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                i++; // [
                while (true)
                {
                    SkipWs();
                    if (End) throw Error("Brak ']'");
                    if (s[i] == ']') { i++; return l; }
                    l.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("Brak ']'");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Error("Oczekiwano ',' albo ']'");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                i++; // "
                while (true)
                {
                    if (End) throw Error("Niezamknięty tekst");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("Niezamknięty tekst");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("Błędna sekwencja \\u");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default:
                            throw Error($"Błędna sekwencja \\{e} – w ścieżkach używaj podwójnego \\\\");
                    }
                }
            }

            object ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                var t = s.Substring(start, i - start);
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) throw Error("Błędna liczba");
                return d;
            }
        }

        // ---------- zapis ----------
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string str: WriteString(sb, str); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> obj:
                    if (obj.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    int k = 0;
                    foreach (var kv in obj)
                    {
                        sb.Append(' ', (indent + 1) * 2);
                        WriteString(sb, kv.Key);
                        sb.Append(": ");
                        Write(sb, kv.Value, indent + 1);
                        if (++k < obj.Count) sb.Append(',');
                        sb.Append('\n');
                    }
                    sb.Append(' ', indent * 2).Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    var items = new List<object>();
                    foreach (var x in list) items.Add(x);
                    if (items.Count == 0) { sb.Append("[]"); break; }
                    sb.Append("[\n");
                    for (int j = 0; j < items.Count; j++)
                    {
                        sb.Append(' ', (indent + 1) * 2);
                        Write(sb, items[j], indent + 1);
                        if (j < items.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    sb.Append(' ', indent * 2).Append(']');
                    break;
                default:
                    WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- pomocnicze ----------
        public static string Str(IDictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) && v is string s ? s : null;

        public static bool Bool(IDictionary<string, object> d, string key, bool def = false) =>
            d != null && d.TryGetValue(key, out var v) && v is bool b ? b : def;

        public static double Num(IDictionary<string, object> d, string key, double def) =>
            d != null && d.TryGetValue(key, out var v) && v is double n ? n : def;
    }
}
