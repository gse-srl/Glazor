using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Glazor.Misc;

/// <summary>
/// A container for popups, only one can be open at time, that means that opening one means closing the previous one. To
/// open it use the Show methods or the Open property.
/// </summary>
public partial class Popup : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; init; } = null!;
    [Inject] ILogger<Popup> Logger { get; init; } = null!;

    /// <summary>Required. The popup's content.</summary>
    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; } = null!;

    /// <summary>
    /// Declarative visibility: true shows the popup and false closes it, as <see cref="ShowAsync()"/> and <see cref="CloseAsync"/> do.
    /// Esc or a click outside closes it without changing the parent's value: reset it in <see cref="OnClose"/>, or setting it
    /// true again does not reopen the popup.
    /// </summary>
    [Parameter]
    public bool Open { get; set; }

    /// <summary>Classes added to the <c>dialog</c> element's own.</summary>
    [Parameter]
    public string Class { get; set; } = "";

    /// <summary>Styles added to the <c>dialog</c> element's own.</summary>
    [Parameter]
    public string Style { get; set; } = "";

    /// <summary>How the popup may be closed: by default <see cref="ClosedBy.Any"/>, Esc, <see cref="CloseAsync"/> or a click outside.</summary>
    [Parameter]
    public ClosedBy ClosedBy { get; set; } = ClosedBy.Any;
    
    /// <summary>Raised however the popup closed.</summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    /// <summary>
    /// Any other attribute goes on the <c>&lt;dialog&gt;</c>: <c>aria-label</c>, <c>role</c>, <c>data-*</c>.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private bool _lastOpen;
    // Set when Open changed since the last render that applied it. Detected at parameter time, because the popup
    // renders only while visible: a change that arrives while it is hidden (after ESC, a click outside, or CloseAsync)
    // must still force the render that applies it, otherwise Open could never reopen it.
    private bool _openChanged;

    private IJSObjectReference? module;
    private bool _disposed;
    private Task<IJSObjectReference>? _import;   // shared by callers that overlap while it is in flight
    private ElementReference _dialogRef;

    // This 3 counters are replacing a single flag because the OnClose event comes asynchronously from the UI ...
    private int _currentShowId;
    private int _currentCloseId;
    private int _currentOnCloseId;

    /// <summary>Whether the popup is shown: true from <see cref="ShowAsync()"/> until it is closed, by code, Esc or a click outside, or disposed.</summary>
    public bool IsVisible { get => !_disposed && _currentShowId != _currentCloseId; }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (Open != _lastOpen)
            _openChanged = true;
    }

    /// <inheritdoc/>
    protected override bool ShouldRender() => IsVisible || _openChanged;

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await EnsureModuleIsLoaded(() => ValueTask.FromResult(true));
        }
        // Must do it here because I need _dialogRef which is valorized only after rendering
        if (_openChanged)
        {
            _openChanged = false;
            _lastOpen = Open;
            if (Open)
                await ShowAsync();
            else
                await CloseAsync();   // a parent driving the popup declaratively can close it too
        }
    }

    private async ValueTask<T> EnsureModuleIsLoaded<T>(Func<ValueTask<T>> func)
    {
        try
        {
            if (module == null)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _import ??= JS.InvokeAsync<IJSObjectReference>("import", "./_content/Glazor/Misc/Popup.razor.js").AsTask();
                IJSObjectReference imported = await _import;
                if (_disposed)
                {
                    // Disposed while the import was in flight: DisposeAsync found nothing, so the first caller back releases it
                    if (_import != null)
                    {
                        _import = null;
                        await imported.DisposeAsync();
                    }
                    return default!;    // go on and ignore it
                }
                module = imported;
            }
            return await func();
        }
        catch (Exception e) when (e is not JSDisconnectedException)   // the circuit is gone: a disconnect is no error to log
        {
            _import = null;   // a failed import is retried by the next call
            Logger.LogError(e, "Unexpected");
            throw;
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _disposed = true;
        _dialogRef = default;
        if (module != null)
        {
            var _module = module;
            module = null;
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
            catch (TaskCanceledException)
            {
            }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Shows the popup where its style places it, closing any other popup open on the page.</summary>
    /// <returns>False when it was not shown: not rendered yet, disposed, or already open.</returns>
    public async Task<bool> ShowAsync()
    {
        if (string.IsNullOrEmpty(_dialogRef.Id))
            return false;
        return await ShowImplAsync();
    }

    /// <summary>
    /// Shows the popup with its top left corner at <paramref name="x"/>, <paramref name="y"/>, in CSS pixels from the viewport's
    /// top left, for a popup whose <c>Style</c> has <c>position: fixed</c>; where it would cross the right or bottom edge it
    /// opens to the left of, or above, the point instead. An anchored popup sets its anchor's area aside here, and the next
    /// <see cref="ShowAsync()"/> places it by its <c>Style</c> again.
    /// </summary>
    /// <returns>False when it was not shown: not rendered yet, disposed, or already open.</returns>
    public async Task<bool> ShowAsync(int x, int y )
    {
        if (string.IsNullOrEmpty(_dialogRef.Id))
            return false;
        return await ShowImplAsync(x, y);
    }

    private async Task<bool> ShowImplAsync(params object?[] args)
    {
        if (_disposed || IsVisible)
            return false;
        Interlocked.Increment(ref _currentShowId);   // before the render: the content is keyed on it, so it starts afresh
        try
        {
            return await EnsureModuleIsLoaded(async () =>
            {
                StateHasChanged();
                // The script places the popup by the size of what the browser has. Inside an event handler or a render the
                // content is drawn only once this method yields: yield first, or the call to the script overtakes it
                await Task.Yield();
                await module.RequireNotNull().InvokeVoidAsync("show", [_dialogRef, .. args]);
                return true;
            });
        }
        catch
        {
            _currentCloseId = _currentShowId;
            _currentOnCloseId = _currentShowId;
            throw;
        }
    }

    /// <summary>Closes the popup, which then raises <c>OnClose</c>.</summary>
    /// <returns>False when it was not open, or is disposed.</returns>
    public async Task<bool> CloseAsync()
    {
        // an empty _dialogRef.Id is not checked here: that check caused a regression
        if (_disposed || !IsVisible)
            return false;
        _currentCloseId = _currentShowId;
        return await EnsureModuleIsLoaded(async () =>
        {
            await module.RequireNotNull().InvokeVoidAsync("close", _dialogRef);
            return true;
        });
    }

    private async Task OnCloseAsync()
    {
        if (_currentOnCloseId == _currentCloseId && _currentCloseId != _currentShowId)
        {
            // No CloseAsync() first
            // Required before setting IsVisible to false, otherwise, for example the LayoutDefDlg does not work as wanted after pressing the ESC key ...
            StateHasChanged();
            _currentCloseId = _currentShowId;
        }
        _currentOnCloseId = _currentCloseId;
        await OnClose.InvokeAsync();
    }

    /// <summary>
    /// Renders again; it can be called from any thread, as it goes through <c>InvokeAsync</c>.
    /// </summary>
    public async Task RepaintAsync()
    {
        await InvokeAsync(StateHasChanged);
    }
}
