using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThemeForge.Models;

namespace ThemeForge.ViewModels
{
    /// <summary>
    /// A choice between the alternatives of one preset group ("Layout", "Cover style", ...).
    /// </summary>
    public class PresetItemViewModel : ObservableObject
    {
        private readonly Action<PresetItemViewModel> onChanged;
        private Preset selected;

        public PresetItemViewModel(Preset group, Preset initial, Action<PresetItemViewModel> onChanged)
        {
            Group = group;
            this.onChanged = onChanged;
            selected = initial != null ? initial : group.OptionsList.FirstOrDefault();
        }

        public Preset Group { get; private set; }

        public List<Preset> Options
        {
            get { return Group.OptionsList; }
        }

        public string Title
        {
            get { return string.IsNullOrWhiteSpace(Group.Name) ? Group.Id : Group.Name; }
        }

        public string Description
        {
            get { return Group.Description; }
        }

        public bool HasDescription
        {
            get { return !string.IsNullOrWhiteSpace(Group.Description); }
        }

        public Preset Selected
        {
            get { return selected; }
            set
            {
                if (ReferenceEquals(selected, value) || value == null)
                {
                    return;
                }

                selected = value;
                OnPropertyChanged("Selected");
                OnPropertyChanged("PreviewImage");
                OnPropertyChanged("HasPreview");
                OnPropertyChanged("SelectedDescription");
                OnPropertyChanged("IsModified");
                OnPropertyChanged("NeedRestart");

                if (onChanged != null)
                {
                    onChanged(this);
                }
            }
        }

        /// <summary>Dot path to persist, or null when the synthetic default is selected.</summary>
        public string SelectedPath
        {
            get { return selected == null || selected.IsSynthetic ? null : selected.Id; }
        }

        public string SelectedDescription
        {
            get { return selected == null ? null : selected.Description; }
        }

        public bool IsModified
        {
            get { return SelectedPath != null; }
        }

        public bool NeedRestart
        {
            get { return selected != null && selected.NeedRestart; }
        }

        /// <summary>Image shipped by the theme for the chosen option; drives the preset preview.</summary>
        public string PreviewImage
        {
            get
            {
                if (selected != null && !string.IsNullOrEmpty(selected.Preview))
                {
                    return selected.Preview;
                }

                return Group.Preview;
            }
        }

        public bool HasPreview
        {
            get { return !string.IsNullOrEmpty(PreviewImage); }
        }

        public void Reset()
        {
            Selected = Group.OptionsList.FirstOrDefault(p => p.IsSynthetic) ?? Group.OptionsList.FirstOrDefault();
        }
    }

    /// <summary>
    /// A collapsible section of related options.
    ///
    /// <see cref="Source"/> holds every item that belongs to the group while
    /// <see cref="Items"/> holds the ones currently passing the search filter. Keeping both
    /// means typing in the search box never loses state (expanded flag, pending edits) and the
    /// modified counter still reflects the whole group.
    /// </summary>
    public class OptionGroupViewModel : ObservableObject
    {
        private bool isExpanded = true;
        private bool isVisible = true;

        public OptionGroupViewModel(string id, string title)
        {
            Id = id;
            Title = title;
            Source = new List<OptionItemViewModel>();
            Items = new ObservableCollection<OptionItemViewModel>();
        }

        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public int Order { get; set; }

        /// <summary>Every item in the group, filter independent.</summary>
        public List<OptionItemViewModel> Source { get; private set; }

        /// <summary>Items passing the current filter; this is what the view binds to.</summary>
        public ObservableCollection<OptionItemViewModel> Items { get; private set; }

        public bool IsExpanded
        {
            get { return isExpanded; }
            set { SetValue(ref isExpanded, value); }
        }

        /// <summary>False when the filter left the group empty, so the header can be hidden.</summary>
        public bool IsVisible
        {
            get { return isVisible; }
            set { SetValue(ref isVisible, value); }
        }

        public bool HasDescription
        {
            get { return !string.IsNullOrWhiteSpace(Description); }
        }

        public int ModifiedCount
        {
            get { return Source.Count(i => i.IsModified); }
        }

        public bool HasModified
        {
            get { return ModifiedCount > 0; }
        }

        public string Header
        {
            get
            {
                var count = Items.Count == Source.Count
                    ? Source.Count.ToString()
                    : Items.Count + "/" + Source.Count;

                // Spelled out rather than "(4, 2 *)": the compact form was unreadable in testing.
                var text = Title + "     " + string.Format(
                    Localization.Get("LOCThemeForgeGroupCount", "{0} items"), count);

                var modified = ModifiedCount;
                if (modified > 0)
                {
                    text += "  -  " + string.Format(
                        Localization.Get("LOCThemeForgeGroupModified", "{0} modified"), modified);
                }

                return text;
            }
        }

        /// <summary>Repopulates <see cref="Items"/> from <see cref="Source"/>.</summary>
        public void ApplyFilter(Func<OptionItemViewModel, bool> predicate)
        {
            Items.Clear();
            foreach (var item in Source)
            {
                if (predicate == null || predicate(item))
                {
                    Items.Add(item);
                }
            }

            IsVisible = Items.Count > 0;
            RefreshCounters();
        }

        public void RefreshCounters()
        {
            OnPropertyChanged("ModifiedCount");
            OnPropertyChanged("HasModified");
            OnPropertyChanged("Header");
        }
    }
}
