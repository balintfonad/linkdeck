using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Desktop.Services;

namespace LinkDeck.Desktop.ViewModels;

public sealed record GroupOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Quick-add dialog: name, type (auto-detected, changeable), target, group.</summary>
public sealed partial class QuickAddViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> TypeNames = ["Web page", "Folder, file or program", "Remote desktop (RDP)"];

    private readonly DeckService _deck;
    private readonly IAppCatalog? _apps;
    private List<AppCandidate> _allApps = [];
    private bool _typeChosenByUser;
    private bool _settingType;
    private bool _groupChosenByUser;
    private bool _settingGroup;

    public QuickAddViewModel(DeckService deck, ItemDraft? draft = null, string? groupId = null, IAppCatalog? apps = null)
    {
        _deck = deck;
        _apps = apps;
        Groups = deck.Config.Groups.Select(g => new GroupOption(g.Id, g.Name)).ToList();
        if (Groups.Count == 0)
        {
            Groups = [new GroupOption("general", "General")];
        }

        var chosen = Groups.FirstOrDefault(g => g.Id == groupId);
        _groupChosenByUser = chosen is not null;
        SetGroup(chosen ?? Groups[0]);

        if (draft is not null)
        {
            _typeChosenByUser = true;
            Target = draft.Target;
            Name = draft.Name;
            Username = draft.Username;
            Args = draft.Args;
            WorkingDir = draft.WorkingDir;
            SetType(draft.Type);
        }
    }

    public IReadOnlyList<string> Types => TypeNames;

    public IReadOnlyList<GroupOption> Groups { get; }

    /// <summary>True while the user types a name for a new group instead of picking one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewGroupButtonText))]
    public partial bool IsCreatingGroup { get; set; }

    public string NewGroupButtonText => IsCreatingGroup ? "Existing…" : "New…";

    [ObservableProperty]
    public partial string NewGroupName { get; set; } = "";

    [RelayCommand]
    private void ToggleNewGroup()
    {
        IsCreatingGroup = !IsCreatingGroup;
        NewGroupName = "";
    }

    /// <summary>Fills the form from a browsed file or folder (shortcuts, .rdp and .url are understood).</summary>
    public void ApplyDraft(ItemDraft draft)
    {
        _typeChosenByUser = true;
        SetType(draft.Type);
        Target = draft.Target;
        Name = draft.Name;
        Username = draft.Username;
        Args = draft.Args;
        WorkingDir = draft.WorkingDir;
    }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Target { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRdp), nameof(IsPath), nameof(TargetLabel), nameof(TargetWatermark))]
    public partial int TypeIndex { get; set; } = (int)ItemType.Url;

    [ObservableProperty]
    public partial string? Username { get; set; }

    [ObservableProperty]
    public partial GroupOption? SelectedGroup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>Filter text for the app picker.</summary>
    [ObservableProperty]
    public partial string AppQuery { get; set; } = "";

    [ObservableProperty]
    public partial AppCandidate? SelectedApp { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingApps { get; set; }

    public bool HasAppPicker => _apps is not null;

    /// <summary>Running apps first, then Start-menu apps, filtered by <see cref="AppQuery"/>.</summary>
    public System.Collections.ObjectModel.ObservableCollection<AppCandidate> Apps { get; } = [];

    /// <summary>Loads the app lists once, in the background.</summary>
    public async Task LoadAppsAsync()
    {
        if (_apps is null || _allApps.Count > 0 || IsLoadingApps)
        {
            return;
        }

        IsLoadingApps = true;
        try
        {
            var apps = _apps;
            _allApps = await Task.Run(() =>
            {
                var running = apps.GetRunningApps();
                var runningTargets = running.Select(a => a.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return running.Concat(apps.GetInstalledApps().Where(a => !runningTargets.Contains(a.Target))).ToList();
            });
            FilterApps();
        }
        finally
        {
            IsLoadingApps = false;
        }
    }

    partial void OnAppQueryChanged(string value) => FilterApps();

    partial void OnSelectedAppChanged(AppCandidate? value)
    {
        if (value is null)
        {
            return;
        }

        _typeChosenByUser = true;
        SetType(ItemType.Path);
        Target = value.Target;
        Name = value.Name;
        Args = value.Args;
        WorkingDir = value.WorkingDir;
    }

    private void FilterApps()
    {
        var q = AppQuery.Trim();
        Apps.Clear();
        foreach (var app in _allApps.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
        {
            Apps.Add(app);
        }
    }

    /// <summary>Program arguments (from a dropped shortcut or the app picker). Always shown, so a shortcut cannot hide them.</summary>
    [ObservableProperty]
    public partial string? Args { get; set; }

    [ObservableProperty]
    public partial string? WorkingDir { get; set; }

    public ItemType Type => (ItemType)TypeIndex;

    public bool IsRdp => Type == ItemType.Rdp;

    public bool IsPath => Type == ItemType.Path;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsReadOnly => _deck.IsReadOnly;

    public string TargetLabel => Type switch
    {
        ItemType.Url => "Address",
        ItemType.Rdp => "Computer (host or host:port)",
        _ => "Folder, file or program path",
    };

    public string TargetWatermark => Type switch
    {
        ItemType.Url => "https://example.com",
        ItemType.Rdp => "server.example.com:3389",
        _ => @"C:\Users\me\Documents",
    };

    /// <summary>The saved item, after a successful <see cref="Save"/>.</summary>
    public Item? SavedItem { get; private set; }

    /// <summary>Validates and saves. Returns true when the dialog can close.</summary>
    public bool Save()
    {
        var draft = new ItemDraft
        {
            Type = Type,
            Name = string.IsNullOrWhiteSpace(Name) ? SuggestName() : Name,
            Target = Target,
            Username = Username,
            Args = Args,
            WorkingDir = WorkingDir,
        };
        var (item, error) = draft.ToItem();
        if (item is null)
        {
            Error = error;
            return false;
        }

        var groupId = SelectedGroup?.Id;
        if (IsCreatingGroup)
        {
            if (string.IsNullOrWhiteSpace(NewGroupName))
            {
                Error = "Enter a name for the new group, or pick an existing one.";
                return false;
            }

            var group = _deck.AddGroup(NewGroupName);
            if (group is null)
            {
                Error = "Could not create the group.";
                return false;
            }

            groupId = group.Id;
        }

        if (!_deck.AddItem(groupId, item))
        {
            Error = _deck.IsReadOnly ? "The configuration is read-only right now." : "Could not save the configuration.";
            return false;
        }

        SavedItem = item;
        Error = null;
        return true;
    }

    partial void OnTargetChanged(string value)
    {
        Error = null;
        SelectGroupForType();
        if (!_typeChosenByUser)
        {
            SetType(ItemDraft.DetectType(value));
        }
    }

    partial void OnTypeIndexChanged(int value)
    {
        if (!_settingType)
        {
            _typeChosenByUser = true;
        }

        SelectGroupForType();
    }

    private void SetType(ItemType type)
    {
        _settingType = true;
        TypeIndex = (int)type;
        _settingType = false;
        SelectGroupForType();
    }

    partial void OnSelectedGroupChanged(GroupOption? value)
    {
        if (!_settingGroup)
        {
            _groupChosenByUser = true;
        }
    }

    /// <summary>Unless the user picked a group, new items go to the group for their kind (Web, Apps, …) when it exists.</summary>
    private void SelectGroupForType()
    {
        if (_groupChosenByUser)
        {
            return;
        }

        var id = ItemCategories.DefaultGroupId(ItemCategories.OfDraft(Type, Target));
        if (Groups.FirstOrDefault(g => g.Id == id) is { } match)
        {
            SetGroup(match);
        }
    }

    private void SetGroup(GroupOption group)
    {
        _settingGroup = true;
        SelectedGroup = group;
        _settingGroup = false;
    }

    private string SuggestName()
    {
        var t = Target.Trim();
        return Type switch
        {
            ItemType.Url => DropParser.NameForUrl(t, null),
            ItemType.Path => Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(Environment.ExpandEnvironmentVariables(t))) is { Length: > 0 } n ? n : t,
            _ => t,
        };
    }
}
