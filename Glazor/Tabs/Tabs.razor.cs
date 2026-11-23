using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Glazor.Tabs;

/// <summary>
/// A tab set: one label for each <see cref="Tab"/> in its content, and the panel of the current one. See the Tabs section of
/// docs/Technical_Documentation.md.
/// </summary>
public partial class Tabs
{
    [Inject] private ILogger<Tabs> Logger { get; set; } = null!;

    /// <summary>Where the labels go around the panel.</summary>
    [Parameter]
    public LabelsPosition LabelsPosition { get; set; } = LabelsPosition.Top;

    /// <summary>Required. The <see cref="Tab"/> components.</summary>
    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; }

    /// <summary>Raised when the user or <see cref="SetCurrentTabAsync(string)"/> changes the current tab, with its name and the previous one.</summary>
    [Parameter]
    public EventCallback<TabChangedEvent> OnTabChanged { get; set; }

    /// <summary>Refuses changing the current tab, while a form is dirty for example.</summary>
    [Parameter]
    public bool CurrentTabFrozen { get; set; } = false;

    /// <summary>The prefix of the generated ids that tie each tab to its panel; a new <see cref="Guid"/> by default.</summary>
    [Parameter]
    public string IdPrefix { get; set; } = Guid.NewGuid().ToString();

    private List<Tab> TabList { get; set; } = [];
    private Dictionary<string, ElementReference> ElementReferences { get; set; } = [];

    // A tab re-created under its name registers before the one it replaces leaves, so names are checked at the render
    internal void AddTab(Tab tab)
    {
        TabList.Add(tab);
        ElementReferences[tab.Name] = default;
    }

    internal void RemoveTab(Tab tab)
    {
        Tab? replacement = TabList.FirstOrDefault(t => t != tab && t.Name == tab.Name);
        if (CurrentTab == tab)
        {
            CurrentTab = replacement;
        }
        var removed = TabList.Remove(tab);
        if (removed && replacement == null)
        {
            ElementReferences.Remove(tab.Name);
        }
    }

    private void CheckUniqueNames()
    {
        string? name = TabList.GroupBy(t => t.Name).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (name != null)
            throw new InvalidOperationException($"Two tabs are named \"{name}\": a tab's Name must be unique in its Tabs");
    }

    /// <summary>
    /// The name of the tab shown, or null when there is none. It follows a hidden tab giving way, which raises no
    /// <see cref="OnTabChanged"/>.
    /// </summary>
    public string? CurrentTabName => CurrentTab?.Name;

    private Tab? _currentTab;
    internal Tab? CurrentTab {
        get
        {
            if (_currentTab is null || _currentTab.Hidden)   // a hidden tab gives way, a disabled one stays current
            {
                _currentTab = TabList.FirstOrDefault(t => !t.Disabled && !t.Hidden);
            }
            return _currentTab;
        }
        private set
        {
            if (_currentTab != value)
            {
                _currentTab = value;
                StateHasChanged();
            }
        }
    }

    private async Task<bool> SetCurrentTabAsync(Tab tab)
    {
        if (tab.Hidden)
        {
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInformation("Tab with name {TabName} is hidden", tab.Name);
            return false;
        }
        if (tab.Disabled)
        {
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInformation("Tab with name {TabName} is disabled", tab.Name);
            return false;
        }
        if (tab == CurrentTab)
        {
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInformation("Tab with name {TabName} is already the current tab", tab.Name);
            return false;
        }
        if (CurrentTabFrozen)
        {
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInformation("Current tab is frozen, cannot change tab to {TabName}", tab.Name);
            return false;
        }

        string? previousTabName = CurrentTab?.Name;
        CurrentTab = tab;
        await OnTabChanged.InvokeAsync(new TabChangedEvent(tab.Name, previousTabName));
        return true;
    }

    /// <summary>Makes the tab named <paramref name="tabName"/> current and raises <c>OnTabChanged</c>.</summary>
    /// <returns>False, with a log line, when there is no such tab, or it is hidden, disabled or already current, or the current
    /// tab is frozen.</returns>
    public async Task<bool> SetCurrentTabAsync(string tabName)
    {
        var tab = TabList.FirstOrDefault(t => t.Name == tabName);
        if (tab == null)
        {
            Logger.LogWarning("Tab with name {TabName} not found", tabName);
            return false;
        }
        return await SetCurrentTabAsync(tab);
    }

    private async Task MoveFocusNextAsync(KeyAction.KeyEvent evt)
    {
        var elIdx = GetTabIndex(evt);
        if (elIdx == null)
        {
            return;
        }
        await MoveFocusAsync(MoveFocusMode.Next, elIdx.Value);
    }

    private async Task MoveFocusPrevAsync(KeyAction.KeyEvent evt)
    {
        var elIdx = GetTabIndex(evt);
        if (elIdx == null)
        {
            return;
        }
        await MoveFocusAsync(MoveFocusMode.Previous, elIdx.Value);
    }

    private async Task SelectFocusedTabAsync(KeyAction.KeyEvent evt)
    {
        var elIdx = GetTabIndex(evt);
        if (elIdx == null)
        {
            return;
        }
        await SetCurrentTabAsync(TabList[elIdx.Value]);
    }

    private async Task MoveFocusStartAsync()
    {
        await MoveFocusAsync(MoveFocusMode.Start, null);
    }

    private async Task MoveFocusEndAsync()
    {
        await MoveFocusAsync(MoveFocusMode.End, null);
    }

    private int? GetTabIndex(KeyAction.KeyEvent evt)
    {
        if (evt.TargetId == null)
        {
            Logger.LogError("Cannot get tab index: null TargetId");
            return null;
        }
        var elIdx = TabList.FindIndex(tab => tab.TabId == evt.TargetId);
        if (elIdx == -1)
        {
            Logger.LogError("Cannot get tab index: no tab with TargetId = {TargetId}", evt.TargetId);
            return null;
        }

        return elIdx;
    }

    private async Task MoveFocusAsync(MoveFocusMode mode, int? currentIdx, int? alreadyVisitedIdx = null)
    {
        try
        {
            if (TabList.Count == 0)
            {
                Logger.LogError("Cannot move focus: TabList is empty; current tab idx = {currentIdx}, mode = {mode}", currentIdx, mode);
                return;
            }
            if (currentIdx != null && currentIdx == alreadyVisitedIdx)
            {
                Logger.LogWarning("Cannot move focus: no selectable tab");
                return;
            }
            // item1 is the target index; item2 is the further move mode when primary target is unreachable
            (int, MoveFocusMode) target = mode switch
            {
                MoveFocusMode.Next => ((currentIdx.RequireNotNull() + 1) % TabList.Count, MoveFocusMode.Next),
                MoveFocusMode.Previous => ((currentIdx.RequireNotNull() + TabList.Count - 1) % TabList.Count, MoveFocusMode.Previous),
                MoveFocusMode.Start => (0, MoveFocusMode.Next),
                MoveFocusMode.End => (TabList.Count - 1, MoveFocusMode.Previous),
                _ => throw new NotImplementedException($"Invalid move mode: {mode}"),
            };
            var tab = TabList[target.Item1];
            if (tab.Hidden)
            {
                await MoveFocusAsync(target.Item2, target.Item1, alreadyVisitedIdx ?? currentIdx);
                return;
            }
            var tabRef = ElementReferences[tab.Name];
            await tabRef.FocusAsync();
        }
        catch (Exception ex)
        {
            // this, or it will only be logged on browser console
            Logger.LogError(ex, "Error during keyboard navigation on tabs");
            throw;
        }
    }

    private enum MoveFocusMode
    {
        Next, 
        Previous, 
        Start, 
        End,
    }
}

/// <summary>Where the tab labels go around the panels; the <c>Vert</c> positions also turn their text vertical.</summary>
public enum LabelsPosition
{
    /// <summary>Above the panels.</summary>
    Top,
    /// <summary>Left of the panels.</summary>
    Left,
    /// <summary>Left of the panels, the text vertical.</summary>
    LeftVert,
    /// <summary>Right of the panels.</summary>
    Right,
    /// <summary>Right of the panels, the text vertical.</summary>
    RightVert,
    /// <summary>Below the panels.</summary>
    Bottom,
}

/// <summary>What <c>OnTabChanged</c> reports: the tab now current, and the one before it, null when there was none.</summary>
/// <param name="TabName">The tab now current.</param>
/// <param name="PreviousTabName">The tab current before, null when there was none.</param>
public record TabChangedEvent(string TabName, string? PreviousTabName);
