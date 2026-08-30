using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Xml;
using ThemeForge.Models;
using ThemeForge.Services;

namespace Validator
{
    public static class Program
    {
        static int errors = 0;
        static int warns = 0;
        static List<string> log = new List<string>();

        static void E(string m) { errors++; log.Add("ERROR  " + m); }
        static void W(string m) { warns++; log.Add("WARN   " + m); }
        static void I(string m) { log.Add("info   " + m); }

        [STAThread]
        public static int Main(string[] args)
        {
            var root = args.Length > 0 ? args[0] : @"C:\Users\Huli3\Documents\其他\playnite-forge\HeliumNova\source";
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                // Serialization.Init is internal in the SDK package, so seed the backing field.
                var serType = typeof(Playnite.SDK.Data.Serialization);
                var init = serType.GetMethod("Init", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (init != null) { init.Invoke(null, new object[] { new SerializerShim() }); }
                else
                {
                    var fld = serType.GetField("serializer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    if (fld == null) { Console.WriteLine("serializer hook not found"); }
                    else { fld.SetValue(null, new SerializerShim()); }
                }
            }
            catch (Exception ex) { Console.WriteLine("serializer init: " + Flatten(ex)); }
            I("root = " + root);

            ThemeOptionsSchema schema = null;
            try
            {
                schema = ThemeOptionsSchema.FromFile(Path.Combine(root, "themeforge.yaml"));
                if (schema == null) { E("themeforge.yaml: FromFile returned null"); }
            }
            catch (Exception ex) { E("themeforge.yaml parse: " + Flatten(ex)); }

            ThemeDescriptor theme = null;
            try { theme = ThemeDescriptor.FromDirectory(root, "Desktop"); }
            catch (Exception ex) { E("FromDirectory threw: " + Flatten(ex)); }
            if (theme == null)
            {
                E("ThemeDescriptor.FromDirectory returned null");
                try { var t2 = Playnite.SDK.Data.Serialization.FromYamlFile<ThemeDescriptor>(Path.Combine(root, "theme.yaml")); I("theme.yaml direct: Id=" + (t2 == null ? "<null obj>" : (t2.Id ?? "<null id>"))); }
                catch (Exception ex) { E("theme.yaml direct parse: " + Flatten(ex)); }
            }
            else
            {
                I("theme: " + theme.Name + " v" + theme.Version + " api=" + theme.ThemeApiVersion + " id=" + theme.Id);
                I("schema flags: native=" + theme.HasNativeSchema + " themeOptions=" + theme.HasThemeOptionsSchema + " legacy=" + theme.HasLegacySchema);
                schema = theme.Options;
            }
            if (schema == null) { Dump(); return 1; }

            int gc = schema.Groups == null ? 0 : schema.Groups.Count;
            int vc = schema.Variables == null ? 0 : schema.Variables.Count;
            int pc = schema.Presets == null ? 0 : schema.Presets.Count;
            I("counts: groups=" + gc + " variables=" + vc + " presetGroups=" + pc);

            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keyRx = new Regex("x:Key=\"([^\"]+)\"", RegexOptions.Compiled);
            var xamls = Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories);
            foreach (var f in xamls)
            {
                foreach (Match m in keyRx.Matches(File.ReadAllText(f))) { declared.Add(m.Groups[1].Value); }
            }
            I("xaml files=" + xamls.Length + " distinct x:Key=" + declared.Count);

            var locEn = LoadLoc(Path.Combine(root, "Localization", "en_US.xaml"), "en_US");
            var locZh = LoadLoc(Path.Combine(root, "Localization", "zh_CN.xaml"), "zh_CN");
            I("loc keys: en_US=" + locEn.Count + " zh_CN=" + locZh.Count);
            foreach (var k in locEn) { if (!locZh.Contains(k)) E("loc parity: missing in zh_CN: " + k); }
            foreach (var k in locZh) { if (!locEn.Contains(k)) E("loc parity: missing in en_US: " + k); }

            Action<string, string> needLoc = (key, where) =>
            {
                if (string.IsNullOrEmpty(key)) return;
                if (!locEn.Contains(key)) E("LocKey not in en_US: " + key + "   (" + where + ")");
                if (!locZh.Contains(key)) E("LocKey not in zh_CN: " + key + "   (" + where + ")");
            };

            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (schema.Groups != null)
            {
                foreach (var g in schema.Groups)
                {
                    if (string.IsNullOrEmpty(g.Id)) { E("group with empty Id"); continue; }
                    if (!groupIds.Add(g.Id)) E("duplicate group Id: " + g.Id);
                    if (string.IsNullOrEmpty(g.LocKey) && string.IsNullOrEmpty(g.Title)) E("group " + g.Id + " has neither LocKey nor Title");
                    needLoc(g.LocKey, "group " + g.Id);
                    needLoc(g.DescriptionLocKey, "group " + g.Id + " desc");
                }
            }

            var gradients = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LinearGradientBrush", "RadialGradientBrush", "GradientBrush" };
            if (schema.Variables != null)
            {
                foreach (var kv in schema.Variables)
                {
                    var key = kv.Key; var v = kv.Value;
                    if (!declared.Contains(key)) E("variable key not declared in any theme xaml: " + key);
                    if (string.IsNullOrEmpty(v.Type)) { E("variable " + key + " has no Type"); }
                    else
                    {
                        var ty = ValueConverter.ResolveType(v.Type);
                        if (ty == null) E("variable " + key + " unknown Type: " + v.Type);
                        if (gradients.Contains(v.Type)) E("variable " + key + " declares gradient Type " + v.Type);
                    }
                    if (string.IsNullOrEmpty(v.Default)) W("variable " + key + " has no Default");
                    else if (!string.IsNullOrEmpty(v.Type))
                    {
                        try { var parsed = ValueConverter.Parse(v.Type, v.Default); if (parsed == null) E("variable " + key + " Default [" + v.Default + "] parsed to null as " + v.Type); }
                        catch (Exception ex) { E("variable " + key + " Default [" + v.Default + "] not parseable as " + v.Type + ": " + ex.Message); }
                    }
                    if (string.IsNullOrEmpty(v.Group)) E("variable " + key + " has no Group");
                    else if (!groupIds.Contains(v.Group.Trim())) E("variable " + key + " references unknown Group: " + v.Group);
                    if (!string.IsNullOrEmpty(v.GroupLocKey)) W("variable " + key + " sets GroupLocKey");
                    if (string.IsNullOrEmpty(v.LocKey) && string.IsNullOrEmpty(v.Title)) E("variable " + key + " has neither LocKey nor Title");
                    needLoc(v.LocKey, "variable " + key);
                    needLoc(v.DescriptionLocKey, "variable " + key + " desc");
                    if (v.Choices != null)
                    {
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var c in v.Choices)
                        {
                            if (c.Value == null) { E("variable " + key + " choice with null Value"); continue; }
                            if (!seen.Add(c.Value)) E("variable " + key + " duplicate choice Value: " + c.Value);
                            needLoc(c.LocKey, "variable " + key + " choice " + c.Value);
                            if (!string.IsNullOrEmpty(v.Type))
                            {
                                try { ValueConverter.Parse(v.Type, c.Value); }
                                catch (Exception ex) { E("variable " + key + " choice [" + c.Value + "] not parseable as " + v.Type + ": " + ex.Message); }
                            }
                        }
                        if (!string.IsNullOrEmpty(v.Default) && !v.Choices.Any(c => string.Equals(c.Value, v.Default, StringComparison.Ordinal)))
                            E("variable " + key + " Default [" + v.Default + "] is not among its Choices");
                    }
                    if (v.Slider != null)
                    {
                        if (v.Slider.MaxValue <= v.Slider.MinValue) E("variable " + key + " slider Max<=Min (" + v.Slider.Min + ".." + v.Slider.Max + ")");
                        double dv;
                        if (double.TryParse(v.Default, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dv))
                        {
                            if (dv < v.Slider.MinValue || dv > v.Slider.MaxValue) E("variable " + key + " Default " + v.Default + " outside slider range " + v.Slider.Min + ".." + v.Slider.Max);
                        }
                    }
                }
            }

            if (schema.Presets != null)
            {
                foreach (var kv in schema.Presets) { WalkPreset(kv.Key, kv.Value, root, needLoc, declared); }
            }

            foreach (var f in xamls)
            {
                try { var doc = new XmlDocument(); doc.Load(f); }
                catch (Exception ex) { E("malformed xml: " + Rel(root, f) + " : " + ex.Message); }
            }

            var plain = new List<string>();
            var presetDir = Path.Combine(root, "Presets");
            if (Directory.Exists(presetDir)) plain.AddRange(Directory.GetFiles(presetDir, "*.xaml", SearchOption.AllDirectories));
            foreach (var f in plain)
            {
                try
                {
                    using (var s = File.OpenRead(f))
                    {
                        var ctx = new ParserContext();
                        ctx.BaseUri = new Uri(f);
                        var res = XamlReader.Load(s, ctx) as ResourceDictionary;
                        if (res == null) E("not a ResourceDictionary: " + Rel(root, f));
                    }
                }
                catch (Exception ex) { E("XamlReader failed: " + Rel(root, f) + " : " + Flatten(ex)); }
            }
            I("preset xaml parsed: " + plain.Count);

            var extPath = Path.Combine(root, "extensions.yaml");
            if (!File.Exists(extPath)) W("extensions.yaml missing");
            else
            {
                try
                {
                    var req = ThemeExtensionRequirements.FromFile(extPath);
                    if (req == null) E("extensions.yaml parsed to null");
                    else I("extensions.yaml: required=" + req.Required.Count + " recommended=" + req.Recommended.Count + " names=" + req.Names.Count);
                }
                catch (Exception ex) { E("extensions.yaml parse: " + Flatten(ex)); }
            }

            var legPath = Path.Combine(root, "thememodifier.yaml");
            if (File.Exists(legPath))
            {
                try
                {
                    var leg = LegacyConstantsSchema.FromFile(legPath);
                    if (leg == null) E("thememodifier.yaml parsed to null");
                    else
                    {
                        var s2 = leg.ToSchema();
                        I("thememodifier.yaml: variables=" + (s2.Variables == null ? 0 : s2.Variables.Count));
                        if (s2.Variables != null)
                        {
                            foreach (var kv in s2.Variables) { if (!declared.Contains(kv.Key)) E("legacy key not declared in any theme xaml: " + kv.Key); }
                        }
                    }
                }
                catch (Exception ex) { E("thememodifier.yaml parse: " + Flatten(ex)); }
            }

            foreach (var lang in new[] { "en_US", "zh_CN" })
            {
                try
                {
                    var fresh = ThemeDescriptor.FromDirectory(root, "Desktop");
                    if (fresh == null) { E("Localize(" + lang + "): descriptor null"); continue; }
                    fresh.Localize(lang);
                    int unresolved = 0;
                    foreach (var kv in fresh.Options.Variables) { if (kv.Value.Title != null && kv.Value.Title.StartsWith("LOC")) { unresolved++; if (unresolved < 8) E("Localize(" + lang + ") unresolved title: " + kv.Key + " -> " + kv.Value.Title); } }
                    if (unresolved == 0) I("Localize(" + lang + ") ok");
                    else I("Localize(" + lang + ") unresolved count = " + unresolved);
                }
                catch (Exception ex) { E("Localize(" + lang + ") threw: " + Flatten(ex)); }
            }

            Dump();
            return errors == 0 ? 0 : 1;
        }

        static void WalkPreset(string path, Preset p, string root, Action<string,string> needLoc, HashSet<string> declared)
        {
            if (string.IsNullOrEmpty(p.LocKey) && string.IsNullOrEmpty(p.Name)) E("preset " + path + " has neither LocKey nor Name");
            needLoc(p.LocKey, "preset " + path);
            needLoc(p.DescriptionLocKey, "preset " + path + " desc");
            if (p.Files != null)
            {
                foreach (var rel in p.Files)
                {
                    var full = Path.Combine(root, rel.Replace("/", "\\"));
                    if (!File.Exists(full)) E("preset " + path + " file missing: " + rel);
                }
            }
            if (p.Constants != null)
            {
                foreach (var kv in p.Constants)
                {
                    if (!declared.Contains(kv.Key)) E("preset " + path + " constant key not declared: " + kv.Key);
                    var vv = kv.Value;
                    if (string.IsNullOrEmpty(vv.Type)) { E("preset " + path + " constant " + kv.Key + " has no Type"); continue; }
                    if (ValueConverter.ResolveType(vv.Type) == null) E("preset " + path + " constant " + kv.Key + " unknown Type: " + vv.Type);
                    var raw = vv.Value;
                    if (raw != null && raw.IndexOf("{", StringComparison.Ordinal) >= 0) continue;
                    try { ValueConverter.Parse(vv.Type, raw); }
                    catch (Exception ex) { E("preset " + path + " constant " + kv.Key + " value [" + raw + "] not parseable as " + vv.Type + ": " + ex.Message); }
                }
            }
            if (p.Presets != null)
            {
                bool hasDefault = false;
                foreach (var kv in p.Presets)
                {
                    if (kv.Key != null && kv.Key.EndsWith("default", StringComparison.OrdinalIgnoreCase)) hasDefault = true;
                    WalkPreset(path + "." + kv.Key, kv.Value, root, needLoc, declared);
                }
                if (hasDefault) W("preset group " + path + " declares its own Default child");
            }
        }

        static HashSet<string> LoadLoc(string file, string label)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(file)) { E("missing localisation file: " + label); return set; }
            try
            {
                using (var s = File.OpenRead(file))
                {
                    var ctx = new ParserContext();
                    ctx.BaseUri = new Uri(file);
                    var rd = XamlReader.Load(s, ctx) as ResourceDictionary;
                    if (rd == null) { E(label + " is not a ResourceDictionary"); return set; }
                    foreach (var k in rd.Keys) { var ks = k as string; if (ks != null) set.Add(ks); }
                }
            }
            catch (Exception ex) { E("failed to load " + label + ": " + Flatten(ex)); }
            return set;
        }

        static string Rel(string root, string f) { return f.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar) : f; }

        static string Flatten(Exception ex)
        {
            var sb = new StringBuilder();
            while (ex != null) { if (sb.Length > 0) sb.Append(" <- "); sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message); ex = ex.InnerException; }
            return sb.ToString();
        }

        static void Dump()
        {
            foreach (var l in log) { if (l.StartsWith("info")) Console.WriteLine(l); }
            foreach (var l in log) { if (l.StartsWith("WARN")) Console.WriteLine(l); }
            foreach (var l in log) { if (l.StartsWith("ERROR")) Console.WriteLine(l); }
            Console.WriteLine("=== errors=" + errors + " warnings=" + warns + " ===");
        }
    }
}
