namespace Glazor.Grds.Filters;

/// <summary>The editor of a <see cref="DistinctDef{SELF, VT}"/>, whose rows wrap the column's values.</summary>
public class DistinctDefRenderer<SELF, VT> : ADynaSetDefRenderer<SELF, VT, DistinctRowValue<VT>, VT> where SELF : DistinctDef<SELF, VT>
{
}
