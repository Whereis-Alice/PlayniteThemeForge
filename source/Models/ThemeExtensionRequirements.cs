using System;
using System.Collections.Generic;
using System.IO;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace ThemeForge.Models
{
    /// <summary>
    /// Optional "extensions.yaml" shipped by a theme, listing the add-ons it needs in
    /// order to render every panel.
    /// </summary>
    public class ThemeExtensionRequirements
    {
        public List<string> Required { get; set; } = new List<string>();
        public List<string> Recommended { get; set; } = new List<string>();

        /// <summary>
        /// Optional add-on id to display name map. Playnite only exposes installed add-on ids,
        /// so without this a missing extension would be reported as a bare guid that the user
        /// cannot search for.
        /// </summary>
        public DictNoCase<string> Names { get; set; } = new DictNoCase<string>();

        /// <summary>Friendly label for an add-on id, falling back to the id itself.</summary>
        public string Label(string addonId)
        {
            if (string.IsNullOrEmpty(addonId))
            {
                return addonId;
            }

            var name = Names == null ? null : Names.Get(addonId);
            return string.IsNullOrEmpty(name) ? addonId : name + "  (" + addonId + ")";
        }

        private static ILogger Logger { get { return LogManager.GetLogger(); } }

        public static ThemeExtensionRequirements FromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                return Serialization.FromYamlFile<ThemeExtensionRequirements>(filePath);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Theme Forge: failed to read extension requirements " + filePath);
                return null;
            }
        }

        public bool IsEmpty
        {
            get
            {
                return (Required == null || Required.Count == 0) &&
                       (Recommended == null || Recommended.Count == 0);
            }
        }
    }
}
