namespace Glazor.Grds.Mdls;

/// <summary>A model over an external store, such as a database, whose rows can be read again, all of them or one.</summary>
public interface IReloadableGridModel<TGridItem> : IGridModel<TGridItem>
{
    /// <summary>
    /// Forces the model to reload data from the external storage at next sync.<br/>
    /// </summary>
    /// <param name="restoreCurrentRow">If true the model shall try to restore the old current row.</param>
    public void ForceReload(bool restoreCurrentRow);
  
    /// <summary>
    /// Reloads the row from the external storage.
    /// </summary>
    /// <param name="rowIndex">The row to read again.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>true if the row stays in the model; false if it leaves it: deleted or, depending on the model, no longer
    /// admitted by its filters.</returns>
    public ValueTask<bool> ReloadItemAsync(int rowIndex, CancellationToken ct = default);
}
