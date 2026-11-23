using Glazor.Misc;

namespace Glazor.Demo;

/// <summary>
/// What the layout hands to the pages: the one spinner the grids share, set once the layout has rendered, and the dialog
/// the layout holds for errors and questions.
/// </summary>
public sealed class GuiHelper(ILogger logger, Func<string, string, Task> openErrorDialog,
    Func<string, string, Func<Task>, Task> openConfirmDialog)
{
    public Spinner? Spinner { get; set; }

    /// <summary>Logs <paramref name="ex"/> under a new error id, and shows the id with <paramref name="msg"/> in the error dialog.</summary>
    public async Task Error(Exception ex, string msg = "Please try again, or ask for help.")
    {
        Guid errorId = Guid.CreateVersion7();
        logger.LogError(ex, "Error {ErrorId}", errorId);
        await openErrorDialog("Error", $"Error {errorId}. {msg}");
    }

    /// <summary>Asks <paramref name="question"/> in the dialog, and runs <paramref name="onConfirm"/> on OK once it has closed; Cancel or Esc runs nothing.</summary>
    public Task OpenConfirm(string title, string question, Func<Task> onConfirm) => openConfirmDialog(title, question, onConfirm);
}
