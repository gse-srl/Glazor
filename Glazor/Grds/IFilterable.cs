using Glazor.Grds.Filters;

namespace Glazor.Grds;

internal interface IFilterable<TGridItem> : ILayoutable<TGridItem>
{
    AFilterDef? GetFilter(string columnName);

    /// <summary>
    /// Open a dialog to ask the user a filter condition for the given column-name
    /// </summary>
    Task ShowFilterDialogAsync(string columnName, Action<FilterDefDlg.SubmitEvent>? onFormSubmit);
}
