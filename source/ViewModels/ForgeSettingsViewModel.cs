using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Data;
using ThemeForge.Models;
using ThemeForge.Services;

namespace ThemeForge.ViewModels
{
    /// <summary>
    /// The single view model behind the settings window.
    ///
    /// It replaces both original plugins' settings screens. The important structural change is
    /// that editing never regenerates the user interface from a xaml string: values flow through
    /// <see cref="OptionItemViewModel"/> into the theme state, and from there through
    /// <see cref="ForgeEngine.Apply"/> into either the live resource dictionary or the preview
    /// one. The preview therefore uses the exact same code path as the real thing.
    /// </summary>
    public class ForgeSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly ForgeEngine engine;
        private readonly Action save;
        private readonly ResourceApplier preview = new ResourceApplier();
        private readonly List<OptionItemViewModel> resourceItems = new List<OptionItemViewModel>();

        private ForgeSettings snapshot;
        private ThemeDescriptor selectedTheme;
        private ThemeProfile selectedProfile;
        private string searchText;
        private bool previewVisible = true;
        private bool suspendWrite;
        private bool rebuildQueued;

        public ForgeSettingsViewModel(ForgeEngine engine, Action save)
        {
            this.engine = engine;
            this.save = save;

            AvailableThemes = new ObservableCollection<ThemeDescriptor>();
            Presets = new ObservableCollection<PresetItemViewModel>();
            Groups = new ObservableCollection<OptionGroupViewModel>();
            Resources = new ObservableCollection<OptionItemViewModel>();
            Profiles = new ObservableCollection<ThemeProfile>();
            MissingExtensions = new ObservableCollection<string>();
            MissingRecommendedExtensions = new ObservableCollection<string>();
            UnresolvedKeys = new ObservableCollection<string>();
            ShadowedOverrides = new ObservableCollection<ShadowedOverride>();

            ResetAllCommand = new RelayCommand(ResetAll, () => SelectedTheme != null);
            ReloadCommand = new RelayCommand(Reload);
            MigrateCommand = new RelayCommand(Migrate);
            SaveProfileCommand = new RelayCommand(SaveProfile, () => SelectedTheme != null);
            DeleteProfileCommand = new RelayCommand(DeleteProfile, () => selectedProfile != null);
            ExportCommand = new RelayCommand(Export, () => SelectedTheme != null);
            ImportCommand = new RelayCommand(Import, () => SelectedTheme != null);
            OpenThemeFolderCommand = new RelayCommand(OpenThemeFolder, () => SelectedTheme != null);
            ClearSearchCommand = new RelayCommand(() => SearchText = null);
            ExpandAllCommand = new RelayCommand(() => SetExpanded(true));
            CollapseAllCommand = new RelayCommand(() => SetExpanded(false));
            ClearShadowedCommand = new RelayCommand(ClearShadowed, () => ShadowedOverrides.Count > 0);

            Rebuild();
        }

        /// <summary>Live plugin settings. Bound directly for the global toggles.</summary>
        public ForgeSettings Settings
        {
            get { return engine.Settings; }
        }

        /// <summary>
        /// Override dictionary of the preview instance. The view merges it into the resource
        /// scope of the preview panel, so preview values shadow the application ones for that
        /// subtree only.
        /// </summary>
        public ResourceDictionary PreviewResources
        {
            get { return preview.Root; }
        }

        public ObservableCollection<ThemeDescriptor> AvailableThemes { get; private set; }
        public ObservableCollection<PresetItemViewModel> Presets { get; private set; }
        public ObservableCollection<OptionGroupViewModel> Groups { get; private set; }
        public ObservableCollection<OptionItemViewModel> Resources { get; private set; }
        public ObservableCollection<ThemeProfile> Profiles { get; private set; }
        /// <summary>Add-ons the theme lists under Required and that are not installed.</summary>
        public ObservableCollection<string> MissingExtensions { get; private set; }

        /// <summary>Add-ons the theme only recommends. Absent ones hide a section, they do not break the theme.</summary>
        public ObservableCollection<string> MissingRecommendedExtensions { get; private set; }
        public ObservableCollection<string> UnresolvedKeys { get; private set; }
        public ObservableCollection<ShadowedOverride> ShadowedOverrides { get; private set; }

        public RelayCommand ResetAllCommand { get; private set; }
        public RelayCommand ReloadCommand { get; private set; }
        public RelayCommand MigrateCommand { get; private set; }
        public RelayCommand SaveProfileCommand { get; private set; }
        public RelayCommand DeleteProfileCommand { get; private set; }
        public RelayCommand ExportCommand { get; private set; }
        public RelayCommand ImportCommand { get; private set; }
        public RelayCommand OpenThemeFolderCommand { get; private set; }
        public RelayCommand ClearSearchCommand { get; private set; }
        public RelayCommand ExpandAllCommand { get; private set; }
        public RelayCommand CollapseAllCommand { get; private set; }
        public RelayCommand ClearShadowedCommand { get; private set; }

        public ThemeDescriptor SelectedTheme
        {
            get { return selectedTheme; }
            set
            {
                if (ReferenceEquals(selectedTheme, value) || value == null)
                {
                    return;
                }

                selectedTheme = value;
                OnPropertyChanged("SelectedTheme");
                OnPropertyChanged("IsActiveTheme");
                OnPropertyChanged("ThemeSummary");
                Rebuild();
            }
        }

        public bool IsActiveTheme
        {
            get { return engine.Themes.IsActive(selectedTheme); }
        }

        /// <summary>One line description of what the selected theme exposes, shown in the header.</summary>
        public string ThemeSummary
        {
            get
            {
                if (selectedTheme == null)
                {
                    return null;
                }

                var parts = new List<string>();
                if (selectedTheme.HasNativeSchema)
                {
                    parts.Add("themeforge.yaml");
                }

                if (selectedTheme.HasThemeOptionsSchema)
                {
                    parts.Add("options.yaml");
                }

                if (selectedTheme.HasLegacySchema)
                {
                    parts.Add("thememodifier.yaml");
                }

                if (parts.Count == 0)
                {
                    parts.Add(Localization.Get("LOCThemeForgeNoSchema", "no option file"));
                }

                if (!IsActiveTheme)
                {
                    parts.Add(Localization.Get("LOCThemeForgeNotActive", "not the active theme - changes are saved but not previewed live"));
                }

                return string.Join(" | ", parts);
            }
        }

        public string SearchText
        {
            get { return searchText; }
            set
            {
                if (string.Equals(searchText, value, StringComparison.Ordinal))
                {
                    return;
                }

                searchText = value;
                OnPropertyChanged("SearchText");
                ApplyFilter();
            }
        }

        /// <summary>Proxy so the checkbox both persists and refreshes the lists.</summary>
        public bool ShowOnlyModified
        {
            get { return Settings.ShowOnlyModified; }
            set
            {
                if (Settings.ShowOnlyModified == value)
                {
                    return;
                }

                Settings.ShowOnlyModified = value;
                OnPropertyChanged("ShowOnlyModified");
                ApplyFilter();
            }
        }

        /// <summary>
        /// When false the resource tab lists colour and brush resources only. That is the set the
        /// old ThemeModifier could edit, and it keeps the first impression manageable; the full
        /// list on a rich theme runs to well over a thousand entries.
        /// </summary>
        public bool ShowAllResources
        {
            get { return Settings.ShowAllResources; }
            set
            {
                if (Settings.ShowAllResources == value)
                {
                    return;
                }

                Settings.ShowAllResources = value;
                OnPropertyChanged("ShowAllResources");
                BuildResourceList();
            }
        }

        public bool PreviewVisible
        {
            get { return previewVisible; }
            set { SetValue(ref previewVisible, value); }
        }

        public ThemeProfile SelectedProfile
        {
            get { return selectedProfile; }
            set
            {
                if (ReferenceEquals(selectedProfile, value))
                {
                    return;
                }

                selectedProfile = value;
                OnPropertyChanged("SelectedProfile");

                if (value != null && !suspendWrite)
                {
                    CurrentState.RestoreProfile(value);
                    ApplyChanges();
                    QueueRebuild();
                }
            }
        }

        public int ModifiedCount
        {
            get
            {
                var count = Groups.Sum(g => g.ModifiedCount) + resourceItems.Count(i => i.IsModified);
                return count + Presets.Count(p => p.IsModified);
            }
        }

        public bool HasModifications
        {
            get { return ModifiedCount > 0; }
        }

        /// <summary>"12 of 340 shown" style hint under the resources filter.</summary>
        public string ResourceSummary
        {
            get
            {
                return string.Format(
                    Localization.Get("LOCThemeForgeResourceCount", "{0} of {1}"),
                    Resources.Count,
                    engine.Registry.Count);
            }
        }

        public bool HasMissingExtensions
        {
            get { return MissingExtensions.Count > 0; }
        }

        public bool HasMissingRecommendedExtensions
        {
            get { return MissingRecommendedExtensions.Count > 0; }
        }

        public bool HasAnyMissingExtensions
        {
            get { return HasMissingExtensions || HasMissingRecommendedExtensions; }
        }

        public bool HasUnresolvedKeys
        {
            get { return UnresolvedKeys.Count > 0; }
        }

        public bool HasShadowedOverrides
        {
            get { return ShadowedOverrides.Count > 0; }
        }

        public bool HasPresets
        {
            get { return Presets.Count > 0; }
        }

        public bool HasOptions
        {
            get { return Groups.Count > 0; }
        }

        /// <summary>True when data from one of the replaced plugins is still lying around.</summary>
        public bool LegacyDataAvailable
        {
            get { return LegacyMigration.HasLegacyData(engine.Api.Paths.ExtensionsDataPath); }
        }

        public string PluginVersion
        {
            get
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "1.0.0" : version.ToString(3);
            }
        }

        public string HostVersion
        {
            get { return engine.Api.ApplicationInfo.ApplicationVersion.ToString(); }
        }

        private ThemeState CurrentState
        {
            get { return Settings.State(selectedTheme == null ? null : selectedTheme.Id); }
        }

        // ------------------------------------------------------------------ building

        /// <summary>Rebuilds every list from the current theme and saved state.</summary>
        public void Rebuild()
        {
            rebuildQueued = false;
            suspendWrite = true;
            try
            {
                BuildThemes();
                BuildPresets();
                BuildOptionGroups();
                BuildResourceList();
                BuildProfiles();
                BuildDiagnostics();
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: failed to build the settings view.");
            }
            finally
            {
                suspendWrite = false;
            }

            ApplyChanges();
            RefreshCounters();
        }

        /// <summary>
        /// Defers a rebuild to the end of the current input cycle. Changing a preset has to
        /// rebuild the very combo box that raised the event, and doing that synchronously from
        /// inside a selection change leaves WPF with a detached selection.
        /// </summary>
        private void QueueRebuild()
        {
            if (rebuildQueued || Application.Current == null)
            {
                Rebuild();
                return;
            }

            rebuildQueued = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(Rebuild), DispatcherPriority.Background);
        }

        private void BuildThemes()
        {
            var previousId = selectedTheme == null ? engine.Themes.ActiveThemeId : selectedTheme.Id;

            AvailableThemes.Clear();
            foreach (var theme in engine.Themes.Themes)
            {
                AvailableThemes.Add(theme);
            }

            selectedTheme = engine.Themes.Find(previousId)
                ?? engine.Themes.Active
                ?? AvailableThemes.FirstOrDefault();

            OnPropertyChanged("SelectedTheme");
            OnPropertyChanged("IsActiveTheme");
            OnPropertyChanged("ThemeSummary");
        }

        private void BuildPresets()
        {
            Presets.Clear();

            if (selectedTheme == null || selectedTheme.Options == null || selectedTheme.Options.Presets == null)
            {
                OnPropertyChanged("HasPresets");
                return;
            }

            AddPresetLevel(selectedTheme.Options.Presets, CurrentState.SelectedPresets);
            OnPropertyChanged("HasPresets");
        }

        /// <summary>
        /// Turns one level of the preset tree into choice rows, descending only into the option
        /// that is currently selected. A nested group only makes sense while its parent is chosen.
        /// </summary>
        private void AddPresetLevel(ThemeForge.Models.Presets level, List<string> selection)
        {
            if (level == null)
            {
                return;
            }

            foreach (var pair in level)
            {
                var node = pair.Value;
                if (node == null || !node.IsGroup)
                {
                    continue;
                }

                var initial = ResolveSelectedOption(node, selection);
                var item = new PresetItemViewModel(node, initial, OnPresetChanged);
                Presets.Add(item);

                if (item.Selected != null && item.Selected.IsGroup)
                {
                    AddPresetLevel(item.Selected.Presets, selection);
                }
            }
        }

        private static Preset ResolveSelectedOption(Preset group, List<string> selection)
        {
            if (selection == null)
            {
                return null;
            }

            foreach (var option in group.OptionsList)
            {
                if (option.IsSynthetic || option.Id == null)
                {
                    continue;
                }

                if (selection.Any(path => string.Equals(path, option.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    return option;
                }
            }

            return null;
        }

        private void BuildOptionGroups()
        {
            Groups.Clear();

            if (selectedTheme == null || selectedTheme.Options == null || selectedTheme.Options.Variables == null)
            {
                OnPropertyChanged("HasOptions");
                return;
            }

            var presetConstants = selectedTheme.Options.Presets == null
                ? new VariablesValues()
                : selectedTheme.Options.Presets.GetConstants(CurrentState.SelectedPresets);

            var byId = new DictNoCase<OptionGroupViewModel>();
            var declared = selectedTheme.Options.Groups;

            foreach (var pair in selectedTheme.Options.Variables)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                var item = CreateVariableItem(pair.Key, pair.Value, presetConstants);
                var groupKey = string.IsNullOrWhiteSpace(pair.Value.Group)
                    ? Localization.Get("LOCThemeForgeGroupGeneral", "General")
                    : pair.Value.Group.Trim();

                OptionGroupViewModel group;
                if (!byId.TryGetValue(groupKey, out group))
                {
                    group = new OptionGroupViewModel(groupKey, groupKey);

                    // Pick up icon / ordering / description when the theme declared the group.
                    var meta = declared == null
                        ? null
                        : declared.FirstOrDefault(g =>
                              string.Equals(g.Id, groupKey, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(g.Title, groupKey, StringComparison.OrdinalIgnoreCase));

                    if (meta != null)
                    {
                        group = new OptionGroupViewModel(meta.Id ?? groupKey, string.IsNullOrEmpty(meta.Title) ? groupKey : meta.Title);
                        group.Description = meta.Description;
                        group.Icon = meta.Icon;
                        group.Order = meta.Order;
                    }

                    byId[groupKey] = group;
                    Groups.Add(group);
                }

                group.Source.Add(item);
            }

            // Declared order first, then alphabetical, so an author can promote the interesting
            // groups without having to give every single one an explicit index.
            var sorted = Groups
                .OrderBy(g => g.Order)
                .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Groups.Clear();
            foreach (var group in sorted)
            {
                Groups.Add(group);
            }

            ApplyFilter();
            OnPropertyChanged("HasOptions");
        }

        private void BuildResourceList()
        {
            resourceItems.Clear();
            Resources.Clear();

            if (selectedTheme == null)
            {
                OnPropertyChanged("ResourceSummary");
                return;
            }

            var state = CurrentState;

            // Keys the theme declared belong to the options tab. Listing them here as well would
            // let the same key be stored in two places with conflicting values.
            var declaredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedTheme.Options != null && selectedTheme.Options.Variables != null)
            {
                foreach (var key in selectedTheme.Options.Variables.Keys)
                {
                    declaredKeys.Add(key);
                }
            }

            foreach (var entry in engine.Registry.Entries)
            {
                if (declaredKeys.Contains(entry.Key))
                {
                    continue;
                }

                var isOverridden = state.Resources.ContainsKey(entry.Key);
                if (!Settings.ShowAllResources && !isOverridden && !IsColourLike(entry.Kind))
                {
                    continue;
                }

                resourceItems.Add(CreateResourceItem(entry));
            }

            // Overrides for keys the current theme no longer defines still need to be reachable,
            // otherwise the user cannot remove them after switching themes.
            foreach (var pair in state.Resources)
            {
                if (declaredKeys.Contains(pair.Key) || engine.Registry.Find(pair.Key) != null)
                {
                    continue;
                }

                resourceItems.Add(CreateOrphanItem(pair.Key, pair.Value));
            }

            ApplyFilter();
            OnPropertyChanged("ResourceSummary");
        }

        private static bool IsColourLike(ValueKind kind)
        {
            return kind == ValueKind.Color || kind == ValueKind.Brush || kind == ValueKind.GradientBrush;
        }

        private void BuildProfiles()
        {
            Profiles.Clear();
            if (selectedTheme == null)
            {
                return;
            }

            var state = CurrentState;
            if (state.Profiles != null)
            {
                foreach (var profile in state.Profiles)
                {
                    Profiles.Add(profile);
                }
            }

            selectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Name, state.ActiveProfile, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged("SelectedProfile");
        }

        private void BuildDiagnostics()
        {
            MissingExtensions.Clear();
            MissingRecommendedExtensions.Clear();
            UnresolvedKeys.Clear();
            ShadowedOverrides.Clear();

            if (selectedTheme == null)
            {
                OnPropertyChanged("HasMissingExtensions");
                OnPropertyChanged("HasMissingRecommendedExtensions");
                OnPropertyChanged("HasAnyMissingExtensions");
                OnPropertyChanged("HasUnresolvedKeys");
                OnPropertyChanged("HasShadowedOverrides");
                return;
            }

            var missingRequired = new HashSet<string>(engine.MissingRequiredExtensions(selectedTheme), StringComparer.OrdinalIgnoreCase);
            foreach (var id in engine.MissingExtensions(selectedTheme))
            {
                var label = selectedTheme.Extensions == null ? id : selectedTheme.Extensions.Label(id);
                if (missingRequired.Contains(id))
                {
                    MissingExtensions.Add(label);
                }
                else
                {
                    MissingRecommendedExtensions.Add(label);
                }
            }

            if (IsActiveTheme)
            {
                foreach (var key in engine.UnresolvedKeys(selectedTheme))
                {
                    UnresolvedKeys.Add(key);
                }
            }

            var state = CurrentState;
            var resourceScope = Localization.Get("LOCThemeForgeShadowedScopeResource", "resource override");
            var variableScope = Localization.Get("LOCThemeForgeShadowedScopeVariable", "theme option");
            foreach (var finding in engine.ShadowedPresetKeys(selectedTheme, state))
            {
                var isResource = state.Resources != null && state.Resources.ContainsKey(finding.Key);
                var entry = isResource ? state.Resources.Get(finding.Key) : (state.Variables == null ? null : state.Variables.Get(finding.Key));
                ShadowedOverrides.Add(new ShadowedOverride
                {
                    Key = finding.Key,
                    Value = entry == null ? string.Empty : entry.Value,
                    Scope = isResource ? resourceScope : variableScope,
                    Reason = ReasonLabel(finding)
                });
            }

            OnPropertyChanged("HasMissingExtensions");
            OnPropertyChanged("HasMissingRecommendedExtensions");
            OnPropertyChanged("HasAnyMissingExtensions");
            OnPropertyChanged("HasUnresolvedKeys");
            OnPropertyChanged("HasShadowedOverrides");
        }

        /// <summary>
        /// Human readable cause for one diagnosed override. "Derived" is the interesting one:
        /// it names the palette key the preset actually shipped, which is the piece of
        /// information that makes an otherwise baffling result obvious.
        /// </summary>
        private static string ReasonLabel(ShadowFinding finding)
        {
            switch (finding.Reason)
            {
                case ShadowReason.Derived:
                    var template = Localization.Get("LOCThemeForgeShadowReasonDerived", "masks preset key {0}");
                    return string.Format(template, finding.ViaKey);
                case ShadowReason.Redundant:
                    return Localization.Get("LOCThemeForgeShadowReasonRedundant", "same as the theme default");
                default:
                    return Localization.Get("LOCThemeForgeShadowReasonDirect", "the preset supplies this key");
            }
        }

        // ------------------------------------------------------------------ item factories

        private OptionItemViewModel CreateVariableItem(string key, Variable variable, VariablesValues presetConstants)
        {
            var type = engine.TypeOf(selectedTheme, key);
            var entry = engine.Registry.Find(key);

            var kind = ValueConverter.KindOf(variable.Type);
            if (kind == ValueKind.Unknown)
            {
                kind = entry != null ? entry.Kind : ValueConverter.KindOf(type);
            }

            if (kind == ValueKind.Unknown)
            {
                kind = variable.Choices != null && variable.Choices.Count > 0 ? ValueKind.Choice : ValueKind.Text;
            }

            var choices = variable.Choices != null && variable.Choices.Count > 0 ? variable.Choices : ChoicesFor(type);

            var item = new OptionItemViewModel(key, OnItemChanged)
            {
                Title = variable.Title,
                Description = variable.Description,
                GroupName = variable.Group,
                Kind = kind,
                TypeName = FirstNonEmpty(variable.Type, entry == null ? null : entry.TypeName, type == null ? null : type.Name),
                ValueType = type,
                Slider = variable.Slider,
                Choices = choices,
                IsResource = false,
                IsMissing = entry == null && ResourceRegistry.Live(key) == null,
                NeedRestart = variable.NeedRestart
            };

            item.DefaultValue = Normalize(type, kind, engine.BaselineOf(selectedTheme, key, presetConstants));

            var stored = CurrentState.Variables.Get(key);
            item.Initialize(stored == null ? null : Normalize(type, kind, stored.Value));
            return item;
        }

        private OptionItemViewModel CreateResourceItem(ResourceEntry entry)
        {
            var item = new OptionItemViewModel(entry.Key, OnItemChanged)
            {
                Title = entry.Key,
                Kind = entry.Kind,
                TypeName = entry.TypeName,
                ValueType = entry.ValueType,
                Choices = ChoicesFor(entry.ValueType),
                IsResource = true,
                DefaultValue = entry.BaselineValue,
                GroupName = entry.TypeName
            };

            var stored = CurrentState.Resources.Get(entry.Key);
            item.Initialize(stored == null ? null : Normalize(entry.ValueType, entry.Kind, stored.Value));
            return item;
        }

        /// <summary>
        /// Row for a saved override whose key no longer exists in the theme. Shown so it can be
        /// reset; without it the value would stay in the settings file forever.
        /// </summary>
        private OptionItemViewModel CreateOrphanItem(string key, VariableValue value)
        {
            var type = ValueConverter.ResolveType(value == null ? null : value.Type);
            var item = new OptionItemViewModel(key, OnItemChanged)
            {
                Title = key,
                Kind = type == null ? ValueKind.Text : ValueConverter.KindOf(type),
                TypeName = value == null ? null : value.Type,
                ValueType = type,
                IsResource = true,
                IsMissing = true,
                DefaultValue = null,
                GroupName = value == null ? null : value.Type
            };

            item.Initialize(value == null ? null : value.Value);
            return item;
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Rewrites a value into the canonical text form of its type.
        ///
        /// Without this a theme default written as "true" would never compare equal to the
        /// captured "True", and every boolean option would show up as modified. Free text and
        /// gradients are left alone: round-tripping a gradient through the xaml writer produces a
        /// technically equal but unreadable blob.
        /// </summary>
        private static string Normalize(Type type, ValueKind kind, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || type == null || kind == ValueKind.Text || kind == ValueKind.GradientBrush)
            {
                return raw;
            }

            var parsed = ValueConverter.ParseAs(type, raw);
            if (parsed == null)
            {
                return raw;
            }

            var text = ValueConverter.Format(parsed);
            return string.IsNullOrEmpty(text) ? raw : text;
        }

        /// <summary>Choice list derived from an enum type, translated when a string exists.</summary>
        private static List<VariableChoice> ChoicesFor(Type type)
        {
            if (type == null || !type.IsEnum)
            {
                return null;
            }

            var choices = new List<VariableChoice>();
            foreach (var name in Enum.GetNames(type))
            {
                choices.Add(new VariableChoice
                {
                    Value = name,
                    Title = Localization.Get("LOCThemeForgeEnum" + name, name)
                });
            }

            return choices;
        }

        // ------------------------------------------------------------------ filtering

        private void ApplyFilter()
        {
            foreach (var group in Groups)
            {
                group.ApplyFilter(Matches);
            }

            Resources.Clear();
            foreach (var item in resourceItems)
            {
                if (Matches(item))
                {
                    Resources.Add(item);
                }
            }

            OnPropertyChanged("ResourceSummary");
        }

        private bool Matches(OptionItemViewModel item)
        {
            if (Settings.ShowOnlyModified && !item.IsModified)
            {
                return false;
            }

            var query = searchText;
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            query = query.Trim();
            return Contains(item.Key, query)
                || Contains(item.Title, query)
                || Contains(item.Description, query)
                || Contains(item.GroupName, query)
                || Contains(item.TypeName, query);
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ write through

        private void OnItemChanged(OptionItemViewModel item)
        {
            if (suspendWrite || selectedTheme == null)
            {
                return;
            }

            var state = CurrentState;
            var bag = item.IsResource ? state.Resources : state.Variables;

            if (item.IsModified && !item.HasError)
            {
                bag.Set(item.Key, item.TypeName, item.Value);
            }
            else
            {
                bag.Remove(item.Key);
            }

            // A hand edited value no longer matches whatever profile it came from.
            if (state.ActiveProfile != null)
            {
                state.ActiveProfile = null;
                suspendWrite = true;
                SelectedProfile = null;
                suspendWrite = false;
            }

            ApplyChanges();
            RefreshCounters();
        }

        private void OnPresetChanged(PresetItemViewModel item)
        {
            if (suspendWrite || selectedTheme == null)
            {
                return;
            }

            CollectPresetSelection();
            ApplyChanges();

            // Presets can contribute constants, which changes the defaults every option row
            // compares against, so the option list has to be rebuilt rather than just refreshed.
            QueueRebuild();
        }

        private void CollectPresetSelection()
        {
            var paths = new List<string>();
            foreach (var item in Presets)
            {
                if (item.SelectedPath != null)
                {
                    paths.Add(item.SelectedPath);
                }
            }

            CurrentState.SelectedPresets = paths;
        }

        /// <summary>
        /// Pushes the current state into the preview always, and into the running application
        /// only when live apply is on and the edited theme is the one in use.
        /// </summary>
        private void ApplyChanges()
        {
            if (selectedTheme == null)
            {
                return;
            }

            try
            {
                engine.Apply(preview, selectedTheme, CurrentState);

                if (Settings.LiveApply && IsActiveTheme)
                {
                    engine.ApplyActive();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: failed to apply changes.");
            }
        }

        private void RefreshCounters()
        {
            foreach (var group in Groups)
            {
                group.RefreshCounters();
            }

            OnPropertyChanged("ModifiedCount");
            OnPropertyChanged("HasModifications");
        }

        private void SetExpanded(bool expanded)
        {
            foreach (var group in Groups)
            {
                group.IsExpanded = expanded;
            }
        }

        // ------------------------------------------------------------------ commands

        private void ResetAll()
        {
            if (selectedTheme == null)
            {
                return;
            }

            var answer = engine.Api.Dialogs.ShowMessage(
                Localization.Get("LOCThemeForgeResetAllPrompt", "Discard every customization for this theme?"),
                Localization.Get("LOCThemeForgeName", "Theme Forge"),
                MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var state = CurrentState;
            state.SelectedPresets = new List<string>();
            state.Variables = new VariablesValues();
            state.Resources = new VariablesValues();
            state.ActiveProfile = null;

            Rebuild();
        }

        /// <summary>
        /// Drops every listed override, leaving the presets themselves untouched. This is the
        /// one-click way out of the "I picked a preset and nothing changed" trap without resetting
        /// unrelated customizations. Redundant entries are cleared too: they only restate the
        /// theme default, so removing them changes nothing visually and keeps the saved profile
        /// from freezing a value the theme may legitimately change in a future update.
        /// </summary>
        private void ClearShadowed()
        {
            if (selectedTheme == null || ShadowedOverrides.Count == 0)
            {
                return;
            }

            var state = CurrentState;
            foreach (var item in ShadowedOverrides.ToList())
            {
                if (state.Resources != null)
                {
                    state.Resources.Remove(item.Key);
                }

                if (state.Variables != null)
                {
                    state.Variables.Remove(item.Key);
                }
            }

            Rebuild();
        }

        private void Reload()
        {
            engine.Reload();
            Rebuild();
        }

        private void Migrate()
        {
            var report = LegacyMigration.Run(Settings, engine.Api.Paths.ExtensionsDataPath, engine.Themes.ActiveThemeId);

            // Reporting four "0" counters looks like a failure to the user. When nothing at all was
            // found, say so in one plain sentence and skip the breakdown entirely.
            if (report.Total == 0)
            {
                engine.Api.Dialogs.ShowMessage(
                    Localization.Get("LOCThemeForgeMigrateNothing", "No legacy settings were found to import."),
                    Localization.Get("LOCThemeForgeMigrateTitle", "Import from legacy plugins"));
                return;
            }

            var lines = new List<string>
            {
                Localization.Get("LOCThemeForgeMigrateOptionValues", "Theme option values") + ": " + report.ThemeOptionsValues,
                Localization.Get("LOCThemeForgeMigratePresets", "Preset selections") + ": " + report.ThemeOptionsPresets,
                Localization.Get("LOCThemeForgeMigrateBrushes", "Colours and brushes") + ": " + report.ThemeModifierBrushes,
                Localization.Get("LOCThemeForgeMigrateConstants", "Theme constants") + ": " + report.ThemeModifierConstants
            };

            if (report.SkippedGradients > 0)
            {
                lines.Add(Localization.Get("LOCThemeForgeMigrateGradientsSkipped",
                    "Gradient brushes were skipped and have to be recreated") + ": " + report.SkippedGradients);
            }

            if (report.Notes != null && report.Notes.Count > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(report.Notes);
            }

            engine.Api.Dialogs.ShowMessage(
                string.Join(Environment.NewLine, lines),
                Localization.Get("LOCThemeForgeMigrateTitle", "Import from legacy plugins"));

            Rebuild();
        }

        private void SaveProfile()
        {
            var state = CurrentState;
            var suggestion = state.ActiveProfile ?? Localization.Get("LOCThemeForgeProfileDefaultName", "My look");

            var input = engine.Api.Dialogs.SelectString(
                Localization.Get("LOCThemeForgeProfileNamePrompt", "Profile name"),
                Localization.Get("LOCThemeForgeName", "Theme Forge"),
                suggestion);

            if (!input.Result || string.IsNullOrWhiteSpace(input.SelectedString))
            {
                return;
            }

            var name = input.SelectedString.Trim();
            var profile = state.CaptureProfile(name);

            if (state.Profiles == null)
            {
                state.Profiles = new List<ThemeProfile>();
            }

            var existing = state.Profiles.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                state.Profiles[existing] = profile;
            }
            else
            {
                state.Profiles.Add(profile);
            }

            state.ActiveProfile = name;
            BuildProfiles();
        }

        private void DeleteProfile()
        {
            var state = CurrentState;
            if (selectedProfile == null || state.Profiles == null)
            {
                return;
            }

            state.Profiles.RemoveAll(p => string.Equals(p.Name, selectedProfile.Name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(state.ActiveProfile, selectedProfile.Name, StringComparison.OrdinalIgnoreCase))
            {
                state.ActiveProfile = null;
            }

            BuildProfiles();
        }

        private void Export()
        {
            if (selectedTheme == null)
            {
                return;
            }

            var target = engine.Api.Dialogs.SaveFile("Yaml file|*.yaml");
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            try
            {
                var export = ThemeExport.FromState(selectedTheme, CurrentState);
                File.WriteAllText(target, Serialization.ToYaml(export));
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: export failed.");
                engine.Api.Dialogs.ShowErrorMessage(e.Message, Localization.Get("LOCThemeForgeName", "Theme Forge"));
            }
        }

        private void Import()
        {
            if (selectedTheme == null)
            {
                return;
            }

            var source = engine.Api.Dialogs.SelectFile("Yaml file|*.yaml");
            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            try
            {
                var import = Serialization.FromYaml<ThemeExport>(File.ReadAllText(source));
                if (import == null)
                {
                    return;
                }

                // Importing a file exported from another theme usually means a lot of dead keys,
                // so make the mismatch explicit instead of silently producing a broken look.
                if (!string.IsNullOrEmpty(import.ThemeId) &&
                    !string.Equals(import.ThemeId, selectedTheme.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var answer = engine.Api.Dialogs.ShowMessage(
                        Localization.Get("LOCThemeForgeImportMismatch", "This file was exported from a different theme") +
                        Environment.NewLine + (import.ThemeName ?? import.ThemeId) +
                        Environment.NewLine + Environment.NewLine +
                        Localization.Get("LOCThemeForgeImportContinue", "Import anyway?"),
                        Localization.Get("LOCThemeForgeName", "Theme Forge"),
                        MessageBoxButton.YesNo);

                    if (answer != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                import.ApplyTo(CurrentState);
                Rebuild();
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: import failed.");
                engine.Api.Dialogs.ShowErrorMessage(e.Message, Localization.Get("LOCThemeForgeName", "Theme Forge"));
            }
        }

        private void OpenThemeFolder()
        {
            if (selectedTheme == null || string.IsNullOrEmpty(selectedTheme.RootPath))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", selectedTheme.RootPath);
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: cannot open theme folder.");
            }
        }

        // ------------------------------------------------------------------ ISettings

        /// <summary>
        /// Called when the settings window opens. A clone is kept so Cancel can restore it; the
        /// engine keeps working against the live instance the whole time, which is what makes
        /// live preview possible without a second code path.
        /// </summary>
        public void BeginEdit()
        {
            snapshot = Serialization.GetClone(Settings);
            Rebuild();
        }

        public void EndEdit()
        {
            snapshot = null;
            PruneEmptyStates();

            if (save != null)
            {
                save();
            }

            engine.ApplyActive();
        }

        public void CancelEdit()
        {
            if (snapshot != null)
            {
                Settings.CopyFrom(snapshot);
                snapshot = null;
            }

            preview.Clear();
            engine.ApplyActive();
            Rebuild();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            foreach (var group in Groups)
            {
                foreach (var item in group.Source)
                {
                    if (item.HasError)
                    {
                        errors.Add(item.DisplayTitle + ": " + item.ValueError);
                    }
                }
            }

            foreach (var item in resourceItems)
            {
                if (item.HasError)
                {
                    errors.Add(item.Key + ": " + item.ValueError);
                }
            }

            return errors.Count == 0;
        }

        /// <summary>
        /// Drops state entries that carry nothing, so uninstalling a theme after a look at its
        /// options does not leave the settings file growing forever.
        /// </summary>
        private void PruneEmptyStates()
        {
            if (Settings.Themes == null)
            {
                return;
            }

            foreach (var key in Settings.Themes.Keys.ToList())
            {
                var state = Settings.Themes[key];
                if (state == null || (state.IsEmpty && (state.Profiles == null || state.Profiles.Count == 0)))
                {
                    Settings.Themes.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// One override the diagnostics tab wants to talk about: it either replaces a key a selected
    /// preset supplies, masks a palette key the preset supplies, or does nothing at all. Surfaced
    /// because a forgotten single-key override silently defeats every preset the user picks
    /// afterwards, which reads as "presets do nothing".
    /// </summary>
    public class ShadowedOverride
    {
        public string Key { get; set; }

        public string Value { get; set; }

        /// <summary>Localized scope label: individual resource override or theme variable.</summary>
        public string Scope { get; set; }

        /// <summary>Localized explanation of why this override is listed.</summary>
        public string Reason { get; set; }
    }
}
