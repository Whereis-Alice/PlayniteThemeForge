using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using ThemeForge.Models;

namespace ThemeForge.Services
{
    /// <summary>
    /// Finds installed themes for the mode Playnite is currently running in and tells which one
    /// is active.
    ///
    /// Both user installed themes (under the configuration folder) and the ones bundled with
    /// Playnite are listed; a user theme with the same id shadows the bundled one, exactly like
    /// Playnite itself resolves them.
    /// </summary>
    public class ThemeRepository
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private readonly List<ThemeDescriptor> themes = new List<ThemeDescriptor>();

        public ThemeRepository(IPlayniteAPI api)
        {
            this.api = api;
        }

        public IList<ThemeDescriptor> Themes
        {
            get { return themes; }
        }

        /// <summary>"Desktop" or "Fullscreen", matching the theme folder layout.</summary>
        public string Mode
        {
            get { return api.ApplicationInfo.Mode == ApplicationMode.Fullscreen ? "Fullscreen" : "Desktop"; }
        }

        public string ActiveThemeId
        {
            get
            {
                return api.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? api.ApplicationSettings.FullscreenTheme
                    : api.ApplicationSettings.DesktopTheme;
            }
        }

        public ThemeDescriptor Active
        {
            get
            {
                var id = ActiveThemeId;
                return themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        public ThemeDescriptor Find(string themeId)
        {
            return themes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsActive(ThemeDescriptor theme)
        {
            return theme != null && string.Equals(theme.Id, ActiveThemeId, StringComparison.OrdinalIgnoreCase);
        }

        public void Refresh()
        {
            themes.Clear();

            var mode = Mode;
            var roots = new List<string>
            {
                Path.Combine(api.Paths.ApplicationPath ?? string.Empty, "Themes", mode),
                Path.Combine(api.Paths.ConfigurationPath ?? string.Empty, "Themes", mode)
            };

            var language = api.ApplicationSettings.Language;

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var directory in Directory.GetDirectories(root))
                {
                    ThemeDescriptor theme;
                    try
                    {
                        theme = ThemeDescriptor.FromDirectory(directory, mode);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Theme Forge: cannot inspect theme folder " + directory);
                        continue;
                    }

                    if (theme == null)
                    {
                        continue;
                    }

                    theme.Localize(language);

                    // Later roots (user folder) shadow bundled themes with the same id.
                    var duplicate = themes.FindIndex(t => string.Equals(t.Id, theme.Id, StringComparison.OrdinalIgnoreCase));
                    if (duplicate >= 0)
                    {
                        themes[duplicate] = theme;
                    }
                    else
                    {
                        themes.Add(theme);
                    }
                }
            }

            themes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
            logger.Info("Theme Forge: found " + themes.Count + " " + mode + " themes.");
        }

        /// <summary>
        /// Add-on ids a theme asks for that are not installed. Themes increasingly rely on data
        /// from other extensions, and a missing one shows up as an empty panel with no hint about
        /// why, so Theme Forge surfaces the gap explicitly.
        /// </summary>
        public List<string> MissingExtensions(ThemeDescriptor theme, bool includeRecommended)
        {
            var missing = new List<string>();
            if (theme == null || theme.Extensions == null)
            {
                return missing;
            }

            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (api.Addons != null && api.Addons.Addons != null)
            {
                foreach (var addon in api.Addons.Addons)
                {
                    installed.Add(addon);
                }
            }

            var wanted = new List<string>();
            if (theme.Extensions.Required != null)
            {
                wanted.AddRange(theme.Extensions.Required);
            }

            if (includeRecommended && theme.Extensions.Recommended != null)
            {
                wanted.AddRange(theme.Extensions.Recommended);
            }

            foreach (var id in wanted)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!installed.Contains(id.Trim()))
                {
                    missing.Add(id.Trim());
                }
            }

            return missing;
        }
    }
}
