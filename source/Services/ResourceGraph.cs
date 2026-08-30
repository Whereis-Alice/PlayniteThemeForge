using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Playnite.SDK;
using ThemeForge.Models;

namespace ThemeForge.Services
{
    /// <summary>
    /// "Who derives from whom" map for a theme's own resource declarations.
    ///
    /// Helium style themes keep a palette of <c>Color</c> keys and derive every brush from it:
    /// <c>&lt;SolidColorBrush x:Key="GlyphBrush" Color="{DynamicResource GlyphColor}" /&gt;</c>.
    /// An accent preset therefore only ships <c>GlyphColor</c>, so a single key override on
    /// <c>GlyphBrush</c> defeats the preset without ever colliding with one of its key names.
    /// Comparing key names alone cannot see that, which is why the derivation graph is parsed
    /// out of the theme xaml and consulted transitively.
    ///
    /// Only self contained brush and value declarations are indexed. Styles and control
    /// templates reference resources all over the place without deriving from them, and pulling
    /// those edges in would turn the diagnostic into noise.
    /// </summary>
    public class ResourceGraph
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const int MaxDepth = 8;

        /// <summary>Single element declaration with no body: the common palette brush case.</summary>
        private static readonly Regex SelfClosing = new Regex(
            "<[A-Za-z][A-Za-z0-9_.]*\\b(?<attrs>[^<>]*?)/>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        /// <summary>Gradient brushes carry their stops in a body; anything else keeps its body out.</summary>
        private static readonly Regex BrushWithBody = new Regex(
            "<(?<type>[A-Za-z][A-Za-z0-9_.]*Brush)\\b(?<attrs>[^<>]*?)>(?<body>.*?)</\\k<type>>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private static readonly Regex KeyAttribute = new Regex(
            "x:Key\\s*=\\s*\"(?<key>[^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Reference = new Regex(
            "\\{\\s*(?:Dynamic|Static)Resource\\s+(?:ResourceKey\\s*=\\s*)?(?<key>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly DictNoCase<HashSet<string>> direct = new DictNoCase<HashSet<string>>();

        /// <summary>Number of keys that derive from at least one other key.</summary>
        public int Count
        {
            get { return direct.Count; }
        }

        /// <summary>
        /// Indexes every xaml file below <paramref name="rootPath"/>. Preset folders are skipped:
        /// they are the thing being compared against, not part of the theme's own derivation.
        /// </summary>
        public void Build(string rootPath)
        {
            direct.Clear();
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(rootPath, "*.xaml", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                logger.Debug("Theme Forge: cannot enumerate xaml under " + rootPath + ": " + e.Message);
                return;
            }

            foreach (var file in files)
            {
                if (IsExcluded(rootPath, file))
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception e)
                {
                    logger.Debug("Theme Forge: cannot read " + file + ": " + e.Message);
                    continue;
                }

                Index(text);
            }

            logger.Debug("Theme Forge: derivation graph has " + direct.Count + " derived key(s).");
        }

        /// <summary>
        /// Every key reachable from <paramref name="key"/> by following derivations, excluding
        /// the key itself. Depth capped and cycle safe because a broken theme can declare
        /// mutually referencing brushes without WPF ever complaining at parse time.
        /// </summary>
        public HashSet<string> Closure(string key)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(key))
            {
                return seen;
            }

            var frontier = new List<string> { key };
            for (var depth = 0; depth < MaxDepth && frontier.Count > 0; depth++)
            {
                var next = new List<string>();
                foreach (var current in frontier)
                {
                    var edges = direct.Get(current);
                    if (edges == null)
                    {
                        continue;
                    }

                    foreach (var edge in edges)
                    {
                        if (!string.Equals(edge, key, StringComparison.OrdinalIgnoreCase) && seen.Add(edge))
                        {
                            next.Add(edge);
                        }
                    }
                }

                frontier = next;
            }

            return seen;
        }

        private static bool IsExcluded(string rootPath, string file)
        {
            var relative = file.Length > rootPath.Length ? file.Substring(rootPath.Length) : file;
            relative = relative.Replace('/', '\\').Trim('\\');
            return relative.StartsWith("Presets\\", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith("Localization\\", StringComparison.OrdinalIgnoreCase);
        }

        private void Index(string text)
        {
            foreach (Match match in BrushWithBody.Matches(text))
            {
                Record(match.Groups["attrs"].Value, match.Groups["attrs"].Value + match.Groups["body"].Value);
            }

            foreach (Match match in SelfClosing.Matches(text))
            {
                var attrs = match.Groups["attrs"].Value;
                Record(attrs, attrs);
            }
        }

        private void Record(string keySource, string referenceSource)
        {
            var keyMatch = KeyAttribute.Match(keySource);
            if (!keyMatch.Success)
            {
                return;
            }

            var key = keyMatch.Groups["key"].Value;
            foreach (Match reference in Reference.Matches(referenceSource))
            {
                var target = reference.Groups["key"].Value;
                if (string.Equals(target, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var edges = direct.Get(key);
                if (edges == null)
                {
                    edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    direct[key] = edges;
                }

                edges.Add(target);
            }
        }
    }
}
