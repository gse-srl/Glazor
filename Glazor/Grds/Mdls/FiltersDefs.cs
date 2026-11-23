using Glazor.Grds.Filters;
using System.Collections.ObjectModel;

namespace Glazor.Grds.Mdls;

internal class FiltersDefs
{
    private readonly OrderedDictionary<string, AFilterDef> _defs;
    private readonly ReadOnlyDictionary<string, AFilterDef> _roDefs;

    public FiltersDefs()
    {
        _defs = new();
        _roDefs = new(_defs);
    }

    public int Count { get => _defs.Count; }

    public IReadOnlyDictionary<string, AFilterDef> Definitions { get => _roDefs; }

    public bool IsFiltered()
    {
        return _defs.Count > 0 && _defs.Values.FirstOrDefault(e => !e.IsVoid()) != null;
    }

    internal void Sync()
    {
        var toRemove = _defs.Where(d => !d.Value.IsStillApplicable()).Select(d => d.Key).ToList();
        foreach (var k in toRemove)
        {
            _defs.Remove(k);
        }
    }

    public bool Remove(string colName) => _defs.Remove(colName);

    public void Clear() => _defs.Clear();

    public void Add(string colName, AFilterDef def) => _defs.Add(colName, def);
}
