using Glazor.Grds.Filters;

namespace Glazor.Grds.Mdls;

internal class InMemoryFilter<TGridItem>
{

    private readonly IDataGridModel<TGridItem> _gridModel;
    private readonly int _size;
    private readonly int[] _propsIdx;
    private readonly AFilterDef[] _fdefs;

    public InMemoryFilter(IDataGridModel<TGridItem> gridModel, bool check4PushDown, IReadOnlyDictionary<string, AFilterDef> filterDefs)
    {
        _gridModel = gridModel;
        AGridModel<TGridItem>? amodel = gridModel as AGridModel<TGridItem>;
        int[] propsIdx = new int[filterDefs.Count];
        AFilterDef[] fdefs = new AFilterDef[filterDefs.Count];
        int size;
        {
            int i = -1;
            foreach (var entry in filterDefs)
            {
                if (entry.Value.IsVoid())
                    continue;
                int idx = gridModel.GetColumnIndex(entry.Key);
                if (idx < 0)
                    throw new InvalidOperationException($"No index for column: {entry.Key}");
                if (check4PushDown && entry.Value.CanCreatePredicate() &&
                        amodel != null && amodel.GetColumnInfo(idx).QueryPushDownEnabled)
                    continue;
                i++;
                propsIdx[i] = idx;
                fdefs[i] = entry.Value;
            }
            size = i + 1;
        }
        _size = size;
        _propsIdx = propsIdx;
        _fdefs = fdefs;
    }

    public bool IsVoid()
    {
        return _size <= 0;
    }

    public bool Includes(TGridItem el)
    {
        bool included = true;
        for (int i = 0; i < _size && included; i++)
        {
            object? value = _gridModel.GetValue(el, _propsIdx[i]);
            included = _fdefs[i].Includes(value);
        }
        return included;
    }
}
