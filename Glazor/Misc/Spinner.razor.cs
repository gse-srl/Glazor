using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Glazor.Misc;

/// <summary>
/// The modal wheel that covers a long operation; the grids show it during a fetch, through <see cref="ISpinner"/>. The
/// application allocates one in its main layout and cascades it to the pages. See the Spinner section of
/// docs/Technical_Documentation.md.
/// </summary>
public partial class Spinner : ComponentBase, ISpinner
{
    [Inject] ILogger<Spinner> Logger { get; init; } = null!;

    // Assigned by the @ref capture on the first render, so null while a model fetches before the spinner has rendered.
    private DialogBox? jsDialog;

    /// <summary>Shows the spinner modally.</summary>
    /// <returns>False when it was not shown: not rendered yet, disposed, or already open.</returns>
    public async Task<bool> ShowModalAsync()
    {
        return jsDialog != null && await jsDialog.ShowModalWithoutYieldAsync();
    }

    /// <summary>Closes the spinner.</summary>
    /// <returns>False when it was not open, not rendered yet, or disposed.</returns>
    public async Task<bool> CloseAsync()
    {
        return jsDialog != null && await jsDialog.CloseAsync();
    }

    /// <inheritdoc/>
    public bool Show()
    {
        if (jsDialog == null || jsDialog.IsVisible)
            return false;
        _ = base.InvokeAsync(() => Observe(jsDialog.ShowModalWithoutYieldAsync(), "shown"));
        return true;
    }

    /// <inheritdoc/>
    public void Hide()
    {
        if (jsDialog == null)
            return;
        _ = base.InvokeAsync(() => Observe(jsDialog.CloseAsync(), "hidden"));
    }

    /// <summary>Show and Hide are fire-and-forget, so a failure is logged here instead of becoming an unobserved exception.</summary>
    private async Task Observe(Task<bool> task, string what)
    {
        try
        {
            await task;
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException)
        {
            // The circuit is gone; nothing to show or hide.
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "The spinner could not be {what}", what);
        }
    }
}
