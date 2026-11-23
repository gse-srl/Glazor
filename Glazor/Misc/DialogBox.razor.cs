using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Glazor.Misc;

/// <summary>
/// A thin wrapper over the HTML <c>&lt;dialog&gt;</c> element: <see cref="ShowAsync"/> or <see cref="ShowModalAsync"/> opens it,
/// <see cref="CloseAsync"/> or the user, as <c>ClosedBy</c> allows, closes it, and <see cref="OnClose"/> reports every close.
/// See docs/Technical_Documentation.md.
/// </summary>
public partial class DialogBox : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; init; } = null!;
    [Inject] ILogger<DialogBox> Logger { get; init; } = null!;

    /// <summary>Required. The dialog's content.</summary>
    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; } = null!;

    /// <summary>Classes added to the <c>dialog</c> element's own.</summary>
    [Parameter]
    public string Class { get; set; } = "";

    /// <summary>Styles added to the <c>dialog</c> element's own.</summary>
    [Parameter]
    public string Style { get; set; } = "";

    /// <summary>How the dialog may be closed, the <c>closedby</c> attribute: by default <see cref="ClosedBy.CloseRequest"/>, Esc or <see cref="CloseAsync"/>.</summary>
    [Parameter]
    public ClosedBy ClosedBy { get; set; } = ClosedBy.CloseRequest;
    
    /// <summary>Raised however the dialog closed, Esc included.</summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    /// <summary>
    /// Any other attribute goes on the <c>&lt;dialog&gt;</c>: <c>aria-labelledby</c>, <c>aria-describedby</c>, <c>data-*</c>.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private IJSObjectReference? module;
    private bool _disposed;
    private ElementReference _dialogRef;
    // The import in flight, shared by the callers that overlap: unlike the documented pattern, which imports from
    // OnAfterRenderAsync only, this dialog also imports on demand, so that the spinner can show during a fetch that runs inside
    // the first render, before any OnAfterRenderAsync. That first import is a module fetch, which the batch's acknowledgement
    // can overtake, and then OnAfterRenderAsync would import a second time and leak the first reference.
    private Task<IJSObjectReference>? _import;

    // This 3 counters are replacing a single flag because the OnClose event comes asynchronously from the UI ...
    private int _currentShowId;
    private int _currentCloseId;
    private int _currentOnCloseId;

    /// <summary>Whether the dialog is shown: true from <see cref="ShowAsync"/> or <see cref="ShowModalAsync"/> until it is closed, or disposed.</summary>
    public bool IsVisible { get => !_disposed && _currentShowId != _currentCloseId; }

    /// <inheritdoc/>
    protected override bool ShouldRender() => IsVisible;

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await EnsureModuleIsLoaded(() => ValueTask.FromResult(true));
        }
    }

    private async ValueTask<T> EnsureModuleIsLoaded<T>(Func<ValueTask<T>> func)
    {
        try
        {
            if (module == null)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _import ??= JS.InvokeAsync<IJSObjectReference>("import", "./_content/Glazor/Misc/DialogBox.razor.js").AsTask();
                IJSObjectReference imported = await _import;
                if (_disposed)
                {
                    // Disposed while the import was in flight: DisposeAsync found nothing, so the first caller back releases it
                    if (_import != null)
                    {
                        _import = null;
                        await imported.DisposeAsync();
                    }
                    return default!;
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

    /// <summary>Shows the dialog without blocking the page behind it.</summary>
    /// <returns>False when it was not shown: disposed, or already open.</returns>
    public async Task<bool> ShowAsync() => await ShowImplAsync("show", true);

    /// <summary>Shows the dialog modally, over a backdrop that blocks the page behind it.</summary>
    /// <returns>False when it was not shown: disposed, or already open.</returns>
    public async Task<bool> ShowModalAsync() => await ShowImplAsync("showModal", true);

    // The spinner opens from inside a render that then blocks the dispatcher: a yield would show it only once that ends
    internal Task<bool> ShowModalWithoutYieldAsync() => ShowImplAsync("showModal", false);

    private async Task<bool> ShowImplAsync(string jsFunction, bool yieldFirst)
    {
        if (_disposed || IsVisible)
            return false;
        Interlocked.Increment(ref _currentShowId);   // before the render: the content is keyed on it, so it starts afresh
        try
        {
            return await EnsureModuleIsLoaded(async () =>
            {
                StateHasChanged();
                // Inside an event handler the new content is drawn only once this method yields, and the browser focuses
                // what the dialog holds when it opens: yield first, or the focus goes with the content the render replaces
                if (yieldFirst)
                    await Task.Yield();
                await module.RequireNotNull().InvokeVoidAsync(jsFunction, _dialogRef);
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

    /// <summary>Closes the dialog, which then raises <c>OnClose</c>.</summary>
    /// <returns>False when it was not open, or is disposed.</returns>
    public async Task<bool> CloseAsync()
    {
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

/// <summary>How a dialog or a popup may be closed: the <c>closedby</c> attribute of the HTML <c>dialog</c> element.</summary>
/// <remarks>https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dialog#closerequest</remarks>
public enum ClosedBy
{
    /// <summary>
    /// ESC, explicit close() or click outside
    /// </summary>
    Any,
    /// <summary>
    /// ESC or explicit close()
    /// </summary>
    CloseRequest,
    /// <summary>
    /// Explicit close() only
    /// </summary>
    None,
}
