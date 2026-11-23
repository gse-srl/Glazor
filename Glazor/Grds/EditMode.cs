namespace Glazor.Grds;

/// <summary>How a <see cref="Grigliaria{TGridItem}"/> over an <see cref="Mdls.IEditableGridModel{TGridItem}"/> edits rows in place.</summary>
public enum EditMode
{
    /// <summary>The grid edits nothing; a page edits the rows its own way, in a form beside the grid say.</summary>
    Disabled = 0,

    /// <summary>F2 or a double-click edits the current row and Ctrl+Ins inserts one; Enter posts and Esc cancels.</summary>
    Manual,

    /// <summary>As <see cref="Manual"/>, and a click on a row edits it, posting first the row being edited.</summary>
    OnClick,

    /// <summary>
    /// As <see cref="OnClick"/>, and the row reached with ↑, ↓, PageUp or PageDown is edited too; while a row is edited those keys
    /// post it and move, so the editors do not get them.
    /// </summary>
    OnMove,
}
