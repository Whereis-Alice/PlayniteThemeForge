using System;
using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using ThemeForge.Models;

namespace ThemeForge.Services
{
    /// <summary>
    /// One editable entry discovered in the running application's resource tree.
    /// <see cref="BaselineValue"/> is the value the theme itself provides, captured before
    /// any Theme Forge override is attached, so "reset" always has something to go back to.
    /// </summary>
    public class ResourceEntry
    {
        public string Key { get; set; }
        public Type ValueType { get; set; }
        public string TypeName { get; set; }
        public ValueKind Kind { get; set; }
        public string BaselineValue { get; set; }

        public override string ToString()
        {
            return Key;
        }
    }

    /// <summary>
    /// Snapshot of every resource key that can meaningfully be edited.
    ///
    /// The original ThemeModifier shipped a hand maintained list of 24 brushes plus a
    /// per-theme yaml file, so anything the author forgot was simply unreachable. Here the
    /// live resource tree is walked instead: whatever the theme actually defines shows up,
    /// and the yaml files only decide what is *promoted* to the friendly options tab.
    /// </summary>
    public class ResourceRegistry
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly DictNoCase<ResourceEntry> byKey = new DictNoCase<ResourceEntry>();
        private readonly List<ResourceEntry> ordered = new List<ResourceEntry>();

        public IList<ResourceEntry> Entries
        {
            get { return ordered; }
        }

        public int Count
        {
            get { return ordered.Count; }
        }

        public ResourceEntry Find(string key)
        {
            return byKey.Get(key);
        }

        /// <summary>
        /// Rebuilds the snapshot. <paramref name="exclude"/> is the override dictionary owned
        /// by Theme Forge; skipping it keeps the captured baseline free of our own edits.
        /// </summary>
        public void Capture(ResourceDictionary exclude)
        {
            byKey.Clear();
            ordered.Clear();

            if (Application.Current == null)
            {
                return;
            }

            var owner = new DictNoCase<ResourceDictionary>();
            var order = new List<string>();
            Collect(Application.Current.Resources, exclude, owner, order, new HashSet<ResourceDictionary>(), 0);

            foreach (var key in order)
            {
                var dictionary = owner.Get(key);
                if (dictionary == null)
                {
                    continue;
                }

                object value;
                try
                {
                    // Indexing resolves a single deferred value; a broken theme entry
                    // must not abort the whole scan.
                    value = dictionary[key];
                }
                catch (Exception e)
                {
                    logger.Debug("Theme Forge: skipping resource " + key + ": " + e.Message);
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                var type = value.GetType();
                var kind = ValueConverter.KindOf(type);
                if (kind == ValueKind.Unknown)
                {
                    continue;
                }

                var entry = new ResourceEntry
                {
                    Key = key,
                    ValueType = type,
                    TypeName = ValueConverter.TypeNameOf(value),
                    Kind = kind,
                    BaselineValue = ValueConverter.Format(value)
                };

                byKey[key] = entry;
                ordered.Add(entry);
            }

            ordered.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));
            logger.Info("Theme Forge: captured " + ordered.Count + " editable resources.");
        }

        /// <summary>Current effective value including overrides, or null when unresolvable.</summary>
        public static object Live(string key)
        {
            if (string.IsNullOrEmpty(key) || Application.Current == null)
            {
                return null;
            }

            try
            {
                return Application.Current.TryFindResource(key);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Best guess at the CLR type behind a resource key, used to parse user input.</summary>
        public Type TypeOf(string key, string declaredType)
        {
            var resolved = ValueConverter.ResolveType(declaredType);
            if (resolved != null)
            {
                return resolved;
            }

            var entry = Find(key);
            if (entry != null)
            {
                return entry.ValueType;
            }

            var live = Live(key);
            return live == null ? null : live.GetType();
        }

        /// <summary>
        /// Walks merged dictionaries before own keys, mirroring WPF lookup precedence, so the
        /// last writer of a key is the one that actually wins at runtime.
        /// </summary>
        private static void Collect(
            ResourceDictionary dictionary,
            ResourceDictionary exclude,
            DictNoCase<ResourceDictionary> owner,
            List<string> order,
            HashSet<ResourceDictionary> visited,
            int depth)
        {
            if (dictionary == null || depth > 16)
            {
                return;
            }

            if (exclude != null && ReferenceEquals(dictionary, exclude))
            {
                return;
            }

            if (!visited.Add(dictionary))
            {
                return;
            }

            var merged = dictionary.MergedDictionaries;
            if (merged != null)
            {
                foreach (var child in merged)
                {
                    Collect(child, exclude, owner, order, visited, depth + 1);
                }
            }

            foreach (var raw in dictionary.Keys)
            {
                var key = raw as string;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                // Localization entries are strings too, but they belong to the translator,
                // not to the theme tinkerer.
                if (key.StartsWith("LOC", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!owner.ContainsKey(key))
                {
                    order.Add(key);
                }

                owner[key] = dictionary;
            }
        }
    }
}
