using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using Playnite.SDK;
using ThemeForge.Models;

namespace ThemeForge.Services
{
    /// <summary>
    /// Owns a single override <see cref="ResourceDictionary"/> and keeps it in sync with the
    /// user's choices.
    ///
    /// Layout of the override dictionary:
    ///   root.MergedDictionaries -> xaml files contributed by the selected presets
    ///   root (own keys)         -> individual constant / resource overrides
    /// WPF resolves a dictionary's own keys before its merged children, so a hand tweaked
    /// value always beats the preset file it came from. Both plugins we replace rebuilt a
    /// brand new dictionary from a generated xaml string on every change, which forced a
    /// restart for anything bound with StaticResource and flickered for DynamicResource.
    /// Mutating one long lived dictionary in place avoids both problems.
    /// </summary>
    public class ResourceApplier
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly ResourceDictionary root = new ResourceDictionary();
        private List<string> currentFiles = new List<string>();
        private string currentThemePath;
        private bool attached;

        /// <summary>
        /// The dictionary to merge somewhere. The live instance merges it into
        /// Application.Current.Resources; the preview instance merges it into a single control.
        /// </summary>
        public ResourceDictionary Root
        {
            get { return root; }
        }

        public bool IsAttached
        {
            get { return attached; }
        }

        public void Attach()
        {
            if (attached || Application.Current == null)
            {
                return;
            }

            Application.Current.Resources.MergedDictionaries.Add(root);
            attached = true;
        }

        public void Detach()
        {
            if (!attached || Application.Current == null)
            {
                attached = false;
                return;
            }

            Application.Current.Resources.MergedDictionaries.Remove(root);
            attached = false;
        }

        /// <summary>Drops every override but keeps the dictionary attached.</summary>
        public void Clear()
        {
            root.MergedDictionaries.Clear();
            root.Clear();
            currentFiles = new List<string>();
        }

        /// <summary>
        /// Replaces the merged preset files. Unchanged selections are a no-op so that editing a
        /// slider does not re-parse every preset file on each keystroke.
        /// </summary>
        public void SetFiles(string themePath, IEnumerable<string> files)
        {
            var wanted = (files == null ? new List<string>() : files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList());

            if (string.Equals(themePath, currentThemePath, StringComparison.OrdinalIgnoreCase) &&
                wanted.Count == currentFiles.Count &&
                !wanted.Where((file, index) => !string.Equals(file, currentFiles[index], StringComparison.OrdinalIgnoreCase)).Any())
            {
                return;
            }

            currentThemePath = themePath;
            currentFiles = wanted;

            root.MergedDictionaries.Clear();
            foreach (var file in wanted)
            {
                var dictionary = LoadXamlFile(themePath, file);
                if (dictionary != null)
                {
                    root.MergedDictionaries.Add(dictionary);
                }
            }
        }

        /// <summary>
        /// Pushes the given values into the override dictionary, removing keys that are no
        /// longer overridden. Values that fail to parse are skipped rather than written as
        /// null, so a half typed colour cannot blank out a brush mid-edit.
        /// </summary>
        public int ApplyValues(VariablesValues values, Func<string, Type> typeResolver)
        {
            var desired = new DictNoCase<object>();

            if (values != null)
            {
                foreach (var pair in values)
                {
                    if (pair.Value == null || string.IsNullOrEmpty(pair.Value.Value))
                    {
                        continue;
                    }

                    var type = ValueConverter.ResolveType(pair.Value.Type);
                    if (type == null && typeResolver != null)
                    {
                        type = typeResolver(pair.Key);
                    }

                    var parsed = ValueConverter.ParseAs(type, pair.Value.Value);
                    if (parsed != null)
                    {
                        desired[pair.Key] = parsed;
                    }
                }
            }

            foreach (var stale in root.Keys.OfType<string>().Where(key => !desired.ContainsKey(key)).ToList())
            {
                root.Remove(stale);
            }

            var changed = 0;
            foreach (var pair in desired)
            {
                if (root.Contains(pair.Key) && AreEquivalent(root[pair.Key], pair.Value))
                {
                    continue;
                }

                root[pair.Key] = pair.Value;
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// Two resource values are treated as identical when they round-trip to the same text.
        /// Brushes and thicknesses do not implement value equality, and rewriting an unchanged
        /// entry would still raise a resource invalidation storm across the whole window.
        /// </summary>
        private static bool AreEquivalent(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.GetType() != right.GetType())
            {
                return false;
            }

            if (left.Equals(right))
            {
                return true;
            }

            var leftText = ValueConverter.Format(left);
            return leftText != null && leftText == ValueConverter.Format(right);
        }

        /// <summary>
        /// Loads a loose xaml resource dictionary from the theme folder.
        ///
        /// A ParserContext carrying the file's own absolute uri is supplied so that relative
        /// references inside the file (images, nested dictionaries) resolve. Loading from a bare
        /// stream leaves the base uri empty, which is why preset files that reference images used
        /// to throw on some themes.
        /// </summary>
        public static ResourceDictionary LoadXamlFile(string themePath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(themePath ?? string.Empty, relativePath);

            if (!File.Exists(fullPath))
            {
                logger.Warn("Theme Forge: preset file not found: " + fullPath);
                return null;
            }

            try
            {
                using (var stream = File.OpenRead(fullPath))
                {
                    var context = new ParserContext { BaseUri = new Uri(fullPath, UriKind.Absolute) };
                    return XamlReader.Load(stream, context) as ResourceDictionary;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: failed to load resource file " + fullPath);
                return null;
            }
        }
    }
}
