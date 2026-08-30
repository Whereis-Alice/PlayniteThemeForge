using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using Playnite.SDK;

namespace ThemeForge
{
    /// <summary>
    /// Loads xaml based string dictionaries, both for the plugin itself and for the
    /// currently active theme.
    ///
    /// Playnite only loads localization files that ship with the application, so an
    /// add-on has to merge its own dictionary into the application resources. English is
    /// always loaded first so that a partially translated language still renders.
    /// </summary>
    public static class Localization
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public static void Load(string folder, string language)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;

            var fallback = LoadDictionary(folder, "en_US");
            if (fallback != null)
            {
                dictionaries.Add(fallback);
            }

            if (string.IsNullOrEmpty(language) || language == "en_US")
            {
                return;
            }

            var localized = LoadDictionary(folder, language);
            if (localized != null)
            {
                dictionaries.Add(localized);
            }
        }

        /// <summary>
        /// Reads "&lt;folder&gt;/Localization/&lt;language&gt;.xaml". Empty strings are dropped so
        /// that untranslated entries fall through to the previously merged dictionary
        /// instead of rendering as blank labels.
        /// </summary>
        public static ResourceDictionary LoadDictionary(string folder, string language)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(language))
            {
                return null;
            }

            var file = Path.Combine(folder, "Localization", language + ".xaml");
            if (!File.Exists(file))
            {
                return null;
            }

            try
            {
                ResourceDictionary dictionary;
                using (var stream = File.OpenRead(file))
                {
                    dictionary = (ResourceDictionary)XamlReader.Load(stream);
                }

                // Note: do not assign dictionary.Source here. The setter reloads the file and
                // would resurrect the empty entries removed below.

                foreach (var key in new System.Collections.ArrayList(dictionary.Keys))
                {
                    var text = dictionary[key] as string;
                    if (text != null && text.Length == 0)
                    {
                        dictionary.Remove(key);
                    }
                }

                return dictionary;
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: failed to parse localization file " + file);
                return null;
            }
        }

        /// <summary>Localized string with a graceful fallback for missing keys.</summary>
        public static string Get(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return fallback;
            }

            try
            {
                var value = ResourceProvider.GetString(key);
                if (!string.IsNullOrEmpty(value) && value != key)
                {
                    return value;
                }
            }
            catch (Exception)
            {
                // ResourceProvider throws when called before the application resources exist.
            }

            return fallback ?? key;
        }
    }
}
