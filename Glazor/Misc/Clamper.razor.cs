using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Glazor.Misc;

/// <summary>
/// Shows the content optionally showing ellipses if it does not fit, moving over the ellipsis shows a popup with the
/// whole content.
/// </summary>
public partial class Clamper : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; init; } = null!;
    [Inject] ILogger<Clamper> Logger { get; init; } = null!;

    /// <summary>The content to clamp.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Required. The lines shown before the ellipsis; 0 or less shows the content unclamped.</summary>
    [Parameter, EditorRequired]
    public int LinesToClamp { get; set; }

    /// <summary>False keeps the content clamped but turns off the popup that shows it whole on hover and click.</summary>
    [Parameter]
    public bool Enabled { get; set; } = true;

    private IJSObjectReference? module;
    private bool _importStarted;
    private bool _disposed;
    // https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-dotnet-from-javascript?view=aspnetcore-10.0
    private DotNetObjectReference<Clamper>? dotNetObjRef;

    private bool showPopup = false;

    private bool _overEllipsis = false;

    private ElementReference _divRef;

    private ElementReference _lastObservedRef;
    private bool _lastEnabled;

    private Popup? _popupRef;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        dotNetObjRef = DotNetObjectReference.Create(this);
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (LinesToClamp > 0)
        {
            if (!_importStarted)   // not only on the first render: LinesToClamp can start at 0
            {
                _importStarted = true;
                // https://learn.microsoft.com/en-us/aspnet/core/blazor/components/class-libraries?view=aspnetcore-10.0&tabs=visual-studio
                IJSObjectReference imported = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Glazor/Misc/Clamper.razor.js");
                if (_disposed)
                {
                    await imported.DisposeAsync();   // disposed while the import was in flight
                    return;
                }
                module = imported;
            }
            // It's not enough on firstRender and sometimes firstRender and !firstRender are executed "together"
            if (module != null && !string.IsNullOrEmpty(_divRef.Id) && 
                (_divRef.Id != _lastObservedRef.Id || Enabled != _lastEnabled))
            {
                _lastObservedRef = _divRef;
                _lastEnabled = Enabled;
                try
                {
                    await module.RequireNotNull().InvokeVoidAsync("registerObserverCallback", _divRef, dotNetObjRef.RequireNotNull(),
                            nameof(ProcessOver), nameof(ProcessClick), Enabled);
                }
                catch (ObjectDisposedException e)
                {
                    if (Logger.IsEnabled(LogLevel.Trace))
                        Logger.LogTrace("As far as I know this can be safely ignored: {msg}", e.Message);
                }
            }
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
        _divRef = default;
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
        dotNetObjRef?.Dispose();
    }

    // Mouse over the "ellipsis"
    /// <summary>Called by the JS side when the mouse enters or leaves the ellipsis.</summary>
    [JSInvokable, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]   // for the JS side, not for hosts
    public async Task ProcessOver(bool over, double width, double height)
    {
        if (over == _overEllipsis)
            return;
        _overEllipsis = over;
        if (over && (_popupRef?.IsVisible == true) != over)
        {
            showPopup = over;
            StateHasChanged();
            if (_popupRef != null)
            {
                // Since there is the Open attribute on the Popup element this should be superflous but I've to call it even in
                // presence of StatusChanged()...
                await _popupRef.ShowAsync();
            }
        }
    }

    // Click on the "ellipsis"
    /// <summary>Called by the JS side on a click on the ellipsis: shows the popup, or closes it.</summary>
    [JSInvokable, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]   // for the JS side, not for hosts
    public async Task ProcessClick(double width, double height)
    {
        showPopup = !(_popupRef?.IsVisible == true);
        // Close before the render: hiding removes the Popup from the tree, which disposes it, and a disposed popup does nothing
        if (!showPopup && _popupRef != null)
            await _popupRef.CloseAsync();
        StateHasChanged();
        if (showPopup && _popupRef != null)
        {
            // Since there is the Open attribute on the Popup element this should be superflous but I've to call it even in
            // presence of StatusChanged()...
            await _popupRef.ShowAsync();
        }
    }
}
