using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Glazor.Misc;

/// <summary>
/// Renders its content and raises <c>OnUserIdle</c> after <see cref="TimeoutMilliseconds"/> without mouse, keyboard, scroll or
/// touch activity on the window, then <c>OnUserActive</c> on the next one.
/// </summary>
public partial class IdleTracker : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; init; } = null!;
    [Inject] ILogger<IdleTracker> Logger { get; init; } = null!;

    private IJSObjectReference? module;

    private DotNetObjectReference<IdleTracker>? dotNetObjRef;

    private ElementReference idleSpanRef;

    /// <summary>Whether the user is idle now: true from <c>OnUserIdle</c> until the next activity.</summary>
    public bool IsIdle { get; private set; } = false;

    /// <summary>The inactivity, in milliseconds, after which the user is idle: 60,000 by default; it must be positive.</summary>
    [Parameter]
    public int TimeoutMilliseconds { get; set; } = 60000; // Default: 1 minute

    /// <summary>Raised once the user has been inactive for <see cref="TimeoutMilliseconds"/>, once per idle period.</summary>
    [Parameter]
    public EventCallback OnUserIdle { get; set; }

    /// <summary>The content, given the tracker to read <see cref="IsIdle"/>.</summary>
    [Parameter] 
    public RenderFragment<IdleTracker>? ChildContent { get; set; }

    /// <summary>Raised on the first mouse, keyboard, scroll or touch activity after the user went idle.</summary>
    [Parameter]
    public EventCallback OnUserActive { get; set; }

    private ElementReference _lastObservedRef;
    private int _lastTimeoutMilliseconds;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        dotNetObjRef = DotNetObjectReference.Create(this);
    }

    // A timeout of zero would report the user idle at once, from the first render
    /// <inheritdoc/>
    protected override void OnParametersSet() => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TimeoutMilliseconds);

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && module == null)
        {
            // https://learn.microsoft.com/en-us/aspnet/core/blazor/components/class-libraries?view=aspnetcore-10.0&tabs=visual-studio
            IJSObjectReference imported = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Glazor/Misc/IdleTracker.razor.js");
            if (_disposed)
            {
                await imported.DisposeAsync();   // disposed while the import was in flight
                return;
            }
            module = imported;
        }
        // Re-registers when the element or the timeout changes; the JS side resets an existing tracker in place.
        if (module != null && !string.IsNullOrEmpty(idleSpanRef.Id) &&
            (idleSpanRef.Id != _lastObservedRef.Id || TimeoutMilliseconds != _lastTimeoutMilliseconds))
        {
            _lastObservedRef = idleSpanRef;
            _lastTimeoutMilliseconds = TimeoutMilliseconds;
            try
            {
                await module.RequireNotNull().InvokeVoidAsync("startTracker", idleSpanRef, dotNetObjRef.RequireNotNull(), TimeoutMilliseconds);
            }
            catch (JSException e)
            {
                Logger.LogError(e, $"IdleTracker not started");
            }
            catch (ObjectDisposedException e)
            {
                if (Logger.IsEnabled(LogLevel.Trace))
                    Logger.LogTrace("As far as I know this can be safely ignored: {msg}", e.Message);
            }
        }
    }

    private bool _disposed;

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _disposed = true;
        if (module != null)
        {
            try
            {
                try
                {
                    await module.InvokeVoidAsync("cleanup", idleSpanRef);
                }
                finally
                {
                    await module.DisposeAsync();
                }
            }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error while disposing IdleTracker");
            }
            module = null;
        }
        dotNetObjRef?.Dispose();
        idleSpanRef = default;
        GC.SuppressFinalize(this);
    }

    /// <summary>Called by the JS side when the user goes idle.</summary>
    [JSInvokable, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]   // for the JS side, not for hosts
    public async Task UserIsIdle()
    {
        if (!IsIdle)
        {
            IsIdle = true;
            try
            {
                await OnUserIdle.InvokeAsync();
                StateHasChanged();
            }
            catch (Exception ex) when (!_disposed)
            {
                await DispatchExceptionAsync(ex);
            }
        }
    }

    // Called from JS on the first activity event after the user had gone idle, so OnUserIdle can
    // fire again on the next idle period rather than only once per component lifetime.
    /// <summary>Called by the JS side on the first activity after the user went idle.</summary>
    [JSInvokable, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]   // for the JS side, not for hosts
    public async Task UserIsActive()
    {
        if (IsIdle)
        {
            IsIdle = false;
            try
            {
                await OnUserActive.InvokeAsync();
                StateHasChanged();
            }
            catch (Exception ex) when (!_disposed)
            {
                await DispatchExceptionAsync(ex);
            }
        }
    }
}
