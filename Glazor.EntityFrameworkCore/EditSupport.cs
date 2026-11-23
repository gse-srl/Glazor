using Glazor.Grds.Mdls;
using Microsoft.EntityFrameworkCore;

namespace Glazor.EntityFrameworkCore;

internal class EditSupport<TDbContext, TGridItem> : EditSupport<TGridItem> where TDbContext : DbContext where TGridItem : class
{
    internal EditSupport(EditableEFDataGridModel<TDbContext, TGridItem> model, Func<TGridItem, CancellationToken, ValueTask> insertPostedFn)
        // While the key is unchanged the public overload reloads the row, and an override of it runs
        : base(async (idx, key, ct) => await (key == null || Equals(key, model.GetItemKeyFn().Invoke(model.GetItem(idx)))
                ? model.ReloadItemAsync(idx, ct) : model.ReloadItemAsync(idx, key, ct)),
            insertPostedFn,
            model)
    {
    }
}
