using System.Globalization;
using System.Resources;

namespace Glazor.Misc;

/// <summary>
/// The texts the components show: button titles, dialog labels, menu entries. The built-in one reads them from the
/// library's resources for <see cref="CultureInfo.CurrentUICulture"/>, English by default and Italian; a host that wants
/// other wording or another language registers its own implementation as a service, and the components take it over
/// the built-in one. The keys are the <see cref="TextKeys"/> constants.
/// </summary>
public interface IGlazorTexts
{
    /// <summary>The text for <paramref name="key"/>, one of the <see cref="TextKeys"/> constants, in the current UI culture.</summary>
    string Get(string key);
}

/// <summary>
/// The built-in <see cref="IGlazorTexts"/>, over <c>Resources/Texts.resx</c> and its satellite assemblies.
/// </summary>
public sealed class GlazorTexts : IGlazorTexts
{
    /// <summary>The one instance, which the components use when the host registers no <see cref="IGlazorTexts"/>.</summary>
    public static readonly GlazorTexts Default = new();

    private static readonly ResourceManager Resources = new("Glazor.Resources.Texts", typeof(GlazorTexts).Assembly);

    private GlazorTexts()
    {
    }

    /// <summary>The text for <paramref name="key"/> in the current UI culture, or the key itself when there is none.</summary>
    public string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>The host's registered <see cref="IGlazorTexts"/>, or <see cref="Default"/> when it registered none.</summary>
    public static IGlazorTexts Resolve(IServiceProvider? services) =>
        services?.GetService(typeof(IGlazorTexts)) as IGlazorTexts ?? Default;
}

/// <summary>The keys of the texts in <c>Resources/Texts.resx</c>, one per string the components show.</summary>
public static class TextKeys
{
    /// <summary>English: "Layout".</summary>
    public const string Layout = "Layout";
    /// <summary>English: "Column left".</summary>
    public const string ColumnLeft = "ColumnLeft";
    /// <summary>English: "Column right".</summary>
    public const string ColumnRight = "ColumnRight";
    /// <summary>English: "Shift left".</summary>
    public const string ShiftLeft = "ShiftLeft";
    /// <summary>English: "Shift right".</summary>
    public const string ShiftRight = "ShiftRight";
    /// <summary>English: "Search on current sorted column".</summary>
    public const string SearchOnSortedColumn = "SearchOnSortedColumn";
    /// <summary>English: "First row".</summary>
    public const string FirstRow = "FirstRow";
    /// <summary>English: "Previous page".</summary>
    public const string PreviousPage = "PreviousPage";
    /// <summary>English: "Previous row".</summary>
    public const string PreviousRow = "PreviousRow";
    /// <summary>English: "Next row".</summary>
    public const string NextRow = "NextRow";
    /// <summary>English: "Next page".</summary>
    public const string NextPage = "NextPage";
    /// <summary>English: "Last row".</summary>
    public const string LastRow = "LastRow";
    /// <summary>English: "Sort ascending".</summary>
    public const string SortAscending = "SortAscending";
    /// <summary>English: "Sort descending".</summary>
    public const string SortDescending = "SortDescending";
    /// <summary>English: "No sort".</summary>
    public const string NoSort = "NoSort";
    /// <summary>English: "Filter".</summary>
    public const string Filter = "Filter";
    /// <summary>English: "Filter active".</summary>
    public const string FilterActive = "FilterActive";
    /// <summary>English: "Filter...".</summary>
    public const string FilterMenu = "FilterMenu";
    /// <summary>English: "Clear filter".</summary>
    public const string ClearFilter = "ClearFilter";
    /// <summary>English: "Hidden columns".</summary>
    public const string HiddenColumns = "HiddenColumns";
    /// <summary>English: "Visible columns".</summary>
    public const string VisibleColumns = "VisibleColumns";
    /// <summary>English: "Show all columns".</summary>
    public const string ShowAllColumns = "ShowAllColumns";
    /// <summary>English: "Show selected columns".</summary>
    public const string ShowSelectedColumns = "ShowSelectedColumns";
    /// <summary>English: "Hide selected columns".</summary>
    public const string HideSelectedColumns = "HideSelectedColumns";
    /// <summary>English: "Hide all columns".</summary>
    public const string HideAllColumns = "HideAllColumns";
    /// <summary>English: "Cancel".</summary>
    public const string Cancel = "Cancel";
    /// <summary>English: "Confirm".</summary>
    public const string Post = "Post";
    /// <summary>English: "Edit".</summary>
    public const string EditRow = "EditRow";
    /// <summary>English: "Insert".</summary>
    public const string InsertRow = "InsertRow";
    /// <summary>English: "Remove".</summary>
    public const string RemoveRow = "RemoveRow";
    /// <summary>English: "OK".</summary>
    public const string OK = "OK";
    /// <summary>English: "Clear".</summary>
    public const string Clear = "Clear";
    /// <summary>English: "Include null".</summary>
    public const string IncludeNull = "IncludeNull";
    /// <summary>English: "Include null/empty".</summary>
    public const string IncludeNullOrEmpty = "IncludeNullOrEmpty";
    /// <summary>English: "Contains".</summary>
    public const string Contains = "Contains";
    /// <summary>English: "Value".</summary>
    public const string Value = "Value";
    /// <summary>English: "From".</summary>
    public const string From = "From";
    /// <summary>English: "To".</summary>
    public const string To = "To";
    /// <summary>English: "All".</summary>
    public const string All = "All";
    /// <summary>English: "True".</summary>
    public const string True = "True";
    /// <summary>English: "False".</summary>
    public const string False = "False";
    /// <summary>English: "Select all".</summary>
    public const string SelectAll = "SelectAll";
    /// <summary>English: "Select row".</summary>
    public const string SelectRow = "SelectRow";
    /// <summary>English: "Move row up".</summary>
    public const string MoveRowUp = "MoveRowUp";
    /// <summary>English: "Move row down".</summary>
    public const string MoveRowDown = "MoveRowDown";
    /// <summary>English: "Show the validation error".</summary>
    public const string ShowValidationError = "ShowValidationError";
    /// <summary>English: "Loading".</summary>
    public const string Loading = "Loading";
    /// <summary>English: "Grid layout".</summary>
    public const string GridLayout = "GridLayout";
    /// <summary>English: "Grid layout - Filtered data".</summary>
    public const string GridLayoutFiltered = "GridLayoutFiltered";
    /// <summary>English: "...empty...".</summary>
    public const string Empty = "Empty";
    /// <summary>English: "Invalid range".</summary>
    public const string InvalidRange = "InvalidRange";
    /// <summary>English: "Expand".</summary>
    public const string Expand = "Expand";
    /// <summary>English: "Collapse".</summary>
    public const string Collapse = "Collapse";
    /// <summary>English: "Zoom".</summary>
    public const string Zoom = "Zoom";
    /// <summary>English: "Zoom in".</summary>
    public const string ZoomIn = "ZoomIn";
    /// <summary>English: "Zoom out".</summary>
    public const string ZoomOut = "ZoomOut";
    /// <summary>English: "Zoom out all the way".</summary>
    public const string ZoomOutAll = "ZoomOutAll";
    /// <summary>English: "Editing".</summary>
    public const string Editing = "Editing";
    /// <summary>English: "Inserting".</summary>
    public const string Inserting = "Inserting";
    /// <summary>English: "TOTAL".</summary>
    public const string Total = "Total";
    /// <summary>English: "tot:".</summary>
    public const string SubTotal = "SubTotal";
    /// <summary>English: "Label".</summary>
    public const string Label = "Label";
    /// <summary>English: "Pref. size".</summary>
    public const string PreferredSize = "PreferredSize";
    /// <summary>English: "Show the row with all its columns".</summary>
    public const string ShowRow = "ShowRow";
    /// <summary>English: "Reload the data".</summary>
    public const string Reload = "Reload";
    /// <summary>English: "Row".</summary>
    public const string Row = "Row";
    /// <summary>English: "Column".</summary>
    public const string Column = "Column";
    /// <summary>English: "Close".</summary>
    public const string Close = "Close";
}
