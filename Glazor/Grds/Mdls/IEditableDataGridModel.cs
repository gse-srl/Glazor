namespace Glazor.Grds.Mdls;

/// <summary>A model that filters and sorts and edits its rows too, as <see cref="EditableDataGridModel{TGridItem}"/> does.</summary>
public interface IEditableDataGridModel<TGridItem> : IEditableGridModel<TGridItem>, IDataGridModel<TGridItem>
{
}
