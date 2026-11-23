using Glazor.Misc;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds.Filters;

/// <summary>
/// The dialog that edits a column's filter, with the definition's own editor inside: the grids open it from a header, and a
/// host through the grid's <c>ShowFilterDialogAsync</c>.
/// </summary>
public partial class FilterDefDlg
{
    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;
    private IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    /// <summary>Raised when the dialog is confirmed, with the column's name and the definition, or null when it was cleared.</summary>
    [Parameter]
    public EventCallback<SubmitEvent> OnFormSubmit { get; set; }

    private DynamicComponent dynamicComponent = null!;
    private ElementReference okButton;

    private string? columnName;
    private AFilterDef? filterDef;
    private string? title;
    private readonly string _titleId = "t" + Guid.NewGuid().ToString("N");   // aria-labelledby, unique per dialog on the page

    private DialogBox jsDialog = null!;

    private Action<SubmitEvent>? _onFormSubmit;
    private bool _nullableValue = true;
    private CancellationTokenSource _closing = new();

    /// <inheritdoc/>
    protected override bool ShouldRender() => jsDialog != null && jsDialog.IsVisible;

    /// <summary>Opens the dialog on <paramref name="filterDef"/>; a column whose value cannot be null gets no "Include null" box.</summary>
    public async Task ShowModalAsync(string columnName, AFilterDef filterDef, string? title,
        Action<SubmitEvent>? onFormSubmit, bool nullableValue = true)
    {
        ArgumentNullException.ThrowIfNull(filterDef);
        this.columnName = columnName;
        this.filterDef = filterDef;
        this.title = title;
        _onFormSubmit = onFormSubmit;
        _nullableValue = nullableValue;
        await jsDialog.ShowModalAsync();
        StateHasChanged();   // the dialog's own attributes (aria-labelledby) follow the title, not only its content
        var renderer = (AFilterDefRenderer)dynamicComponent.Instance.RequireNotNull();
        renderer.Validate();
        await renderer.ShownAsyncImpl();
    }

    /// <summary>Closes the dialog without applying the filter.</summary>
    public async Task CloseAsync()
    {
        await jsDialog.CloseAsync();
        columnName = null;
        filterDef = null;
        title = null;
        _onFormSubmit = null;
        _closing.Cancel();   // stops the editor's load
        _closing = new();
    }

    /// <summary>Applies the definition being edited and closes the dialog, as its OK button does; nothing happens while the editor's <c>Validate</c> refuses it.</summary>
    public async Task SubmitAsync()
    {
        await okButton.FocusAsync();  // So we move data into the model
        var renderer = (AFilterDefRenderer)dynamicComponent.Instance.RequireNotNull();
        if (!renderer.Validate())  
            return;
        SubmitEvent submitted = new(columnName.RequireNotNull(), filterDef.RequireNotNull());
        await OnFormSubmit.InvokeAsync(submitted);
        var efs = _onFormSubmit;
        await CloseAsync();
        efs?.Invoke(submitted);
    }

    /// <summary>Removes the column's filter and closes the dialog, as its Clear button does.</summary>
    public async Task ClearAsync()
    {
        SubmitEvent submitted = new(columnName.RequireNotNull(), null);
        await OnFormSubmit.InvokeAsync(submitted);
        var efs = _onFormSubmit;
        await CloseAsync();
        efs?.Invoke(submitted);
    }

    /// <summary>The column filtered and its definition, null when the filter was cleared.</summary>
    /// <param name="ColumnName">The column filtered.</param>
    /// <param name="FilterDef">Its definition; null when the filter was cleared.</param>
    public record SubmitEvent(string ColumnName, AFilterDef? FilterDef);
}
