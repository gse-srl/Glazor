using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Glazor.Misc;

/// <summary>
/// A checkbox bound to a <c>bool?</c>, with null shown as the indeterminate state.
/// </summary>
public partial class TriStateCheckbox : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; init; } = null!;
    [Inject] ILogger<TriStateCheckbox> Logger { get; init; } = null!;

    /// <summary>True, false or null for indeterminate; supports <c>@bind-Value</c>.</summary>
    [Parameter]
    public bool? Value { get; set; }

    /// <summary>
    /// Used for data binding with @bind-Value. Raised when the user toggles the checkbox; a programmatic write to
    /// <see cref="Value"/> does not raise it, like Blazor's own input components.
    /// </summary>
    [Parameter]
    public EventCallback<bool?> ValueChanged { get; set; }

    /// <summary>The <c>input</c>'s <c>class</c>.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>The <c>input</c>'s <c>title</c>.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>Refuses changes; <see cref="ValueChanged"/> is not raised while set.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Default logic: Unchecked -> Checked -> Indeterminate -> Unchecked
    /// </summary>
    [Parameter]
    public Func<bool?, bool?> ToggleFn { get; set; } = value => value switch
    {
        false => true,
        true => null,
        _ => false
    };

    /// <summary>
    /// Any other attribute goes on the <c>input</c>: <c>id</c>, <c>name</c>, <c>aria-label</c>, <c>data-*</c>; the attributes the
    /// checkbox sets itself win.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private IJSObjectReference? module;
    private ElementReference _checkboxRef;

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && module == null)
        {
            // https://learn.microsoft.com/en-us/aspnet/core/blazor/components/class-libraries?view=aspnetcore-10.0&tabs=visual-studio
            IJSObjectReference imported = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Glazor/Misc/TriStateCheckbox.razor.js");
            if (_disposed)
            {
                await imported.DisposeAsync();   // disposed while the import was in flight
                return;
            }
            module = imported;
        }
        // "indeterminate" exists only as a DOM property, and "checked" must follow Value even when the rendered attribute did
        // not change (indeterminate -> unchecked leaves it true in the DOM); every render, so programmatic changes show too
        if (module != null && !string.IsNullOrEmpty(_checkboxRef.Id))
        {
            await module.RequireNotNull().InvokeVoidAsync("setState", _checkboxRef, Value == null, Value == true);
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
                await module.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error while disposing TriStateCheckbox");
            }
            module = null;
        }
        _checkboxRef = default;
        GC.SuppressFinalize(this);
    }

    private async Task ToggleState()
    {
        if (Disabled)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
                Logger.LogDebug("Skipping events of a disabled tristate-checkbox");
            return;
        }
        bool? value = ToggleFn.Invoke(Value);
        if (Value == value)
            return;
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }
}
