using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using ThemeForge.Models;
using ThemeForge.Services;
using ThemeForge.ViewModels;

namespace ThemeForge
{
    /// <summary>
    /// Entry point of the add-on. Keeps a single <see cref="ForgeEngine"/> alive for the
    /// whole session because the engine owns the resource dictionary that is merged into
    /// the running application; recreating it would drop every override.
    /// </summary>
    public class ThemeForgePlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly ForgeEngine engine;
        private ForgeSettingsViewModel viewModel;
        private bool windowOpen;

        public override Guid Id { get; } = Guid.Parse("f0c1a7d2-3b64-4f18-9d5a-2c8e6b41a903");

        /// <summary>Folder the plugin was loaded from, used to find the localization files.</summary>
        public string PluginFolder
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        public ThemeForgePlugin(IPlayniteAPI api) : base(api)
        {
            // Settings have to exist before anything else: the engine reads the stored
            // per theme state straight out of them.
            var settings = LoadPluginSettings<ForgeSettings>() ?? new ForgeSettings();
            engine = new ForgeEngine(api, settings);

            Properties = new GenericPluginProperties { HasSettings = true };

            try
            {
                Localization.Load(PluginFolder, api.ApplicationSettings.Language);
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: localization could not be loaded.");
            }
        }

        /// <summary>Persists the current settings object. Passed to the view model as a callback.</summary>
        public void SaveSettings()
        {
            SavePluginSettings(engine.Settings);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return GetViewModel();
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new Views.SettingsView();
        }

        private ForgeSettingsViewModel GetViewModel()
        {
            if (viewModel == null)
            {
                viewModel = new ForgeSettingsViewModel(engine, SaveSettings);
            }

            return viewModel;
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                engine.Initialize();
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: engine failed to initialize.");
            }

            TryMigrate();
            WarnAboutMissingExtensions();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            engine.Shutdown();
        }

        /// <summary>
        /// One shot import of the settings written by ThemeOptions and ThemeModifier. The
        /// version marker makes sure a user who deliberately reset something does not get
        /// the old values pushed back on every start.
        /// </summary>
        private void TryMigrate()
        {
            if (engine.Settings.MigrationVersion >= LegacyMigration.CurrentVersion)
            {
                return;
            }

            try
            {
                if (!LegacyMigration.HasLegacyData(PlayniteApi.Paths.ExtensionsDataPath))
                {
                    engine.Settings.MigrationVersion = LegacyMigration.CurrentVersion;
                    SaveSettings();
                    return;
                }

                var activeId = engine.Themes.ActiveThemeId;
                var report = LegacyMigration.Run(engine.Settings, PlayniteApi.Paths.ExtensionsDataPath, activeId);
                engine.Settings.MigrationVersion = LegacyMigration.CurrentVersion;
                SaveSettings();
                engine.ApplyActive();

                if (report.Total > 0)
                {
                    var lines = new List<string>();
                    lines.Add(Localization.Get("LOCThemeForgeMigrateTitle", "Import from legacy plugins"));
                    lines.AddRange(report.Notes);
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        "ThemeForge_Migration",
                        string.Join(Environment.NewLine, lines),
                        NotificationType.Info,
                        () => OpenWindow()));
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: legacy import failed.");
            }
        }

        /// <summary>Tells the user which add-ons the active theme expects but cannot find.</summary>
        private void WarnAboutMissingExtensions()
        {
            if (!engine.Settings.NotifyMissingExtensions)
            {
                return;
            }

            try
            {
                var theme = engine.ActiveTheme;
                if (theme == null)
                {
                    return;
                }

                // Only hard requirements deserve a startup toast. Themes routinely list a dozen
                // nice-to-have integrations, and warning about those trains the user to ignore the
                // notification; the Extensions tab still lists them.
                var missing = engine.MissingRequiredExtensions(theme);
                if (missing.Count == 0)
                {
                    return;
                }

                var lines = new List<string>();
                lines.Add(theme.DisplayName);
                lines.Add(Localization.Get("LOCThemeForgeMissingExtensionsHeader", "This theme requires these add-ons, which are not installed:"));
                lines.AddRange(missing);
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "ThemeForge_MissingExtensions",
                    string.Join(Environment.NewLine, lines),
                    NotificationType.Error,
                    () => OpenWindow()));
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: missing add-on check failed.");
            }
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            if (!engine.Settings.ShowTopPanelButton)
            {
                yield break;
            }

            var item = new TopPanelItem();
            item.Title = Localization.Get("LOCThemeForgeName", "Theme Forge");
            // IcoFont "beaker" glyph, part of the icon font Playnite ships with.
            item.Icon = new TextBlock
            {
                Text = char.ConvertFromUtf32(0xeeb5),
                FontSize = 16,
                FontFamily = ResourceProvider.GetResource<System.Windows.Media.FontFamily>("FontIcoFont")
            };
            item.Activated = () => OpenWindow();
            yield return item;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (!engine.Settings.MenuInExtensions)
            {
                yield break;
            }

            var open = new MainMenuItem();
            open.MenuSection = "@" + Localization.Get("LOCThemeForgeName", "Theme Forge");
            open.Description = Localization.Get("LOCThemeForgeName", "Theme Forge");
            open.Action = a => OpenWindow();
            yield return open;

            var reload = new MainMenuItem();
            reload.MenuSection = "@" + Localization.Get("LOCThemeForgeName", "Theme Forge");
            reload.Description = Localization.Get("LOCThemeForgeReload", "Rescan");
            reload.Action = a => GetViewModel().ReloadCommand.Execute(null);
            yield return reload;
        }

        /// <summary>
        /// Shows the editor in its own window. Playnite has no API to open the settings
        /// page of a specific add-on, so the same view is hosted here with an explicit
        /// save/cancel pair around the <see cref="ISettings"/> edit cycle.
        /// </summary>
        public void OpenWindow()
        {
            if (windowOpen)
            {
                return;
            }

            try
            {
                windowOpen = true;
                var vm = GetViewModel();
                vm.BeginEdit();

                var options = new WindowCreationOptions
                {
                    ShowCloseButton = true,
                    ShowMaximizeButton = true,
                    ShowMinimizeButton = false
                };

                var window = PlayniteApi.Dialogs.CreateWindow(options);
                window.Title = Localization.Get("LOCThemeForgeName", "Theme Forge");
                window.Width = 1180;
                window.Height = 780;
                window.MinWidth = 820;
                window.MinHeight = 560;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;

                var host = new Views.ForgeWindowView(window, vm);
                window.Content = host;
                window.DataContext = vm;
                window.ShowDialog();
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: editor window failed to open.");
            }
            finally
            {
                windowOpen = false;
            }
        }
    }
}
