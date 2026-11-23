namespace Glazor.Grds.Decs;

/// <summary>What a header template gets: the column.</summary>
/// <param name="Column">The column whose header is drawn.</param>
public readonly record struct HDecoratorContext<TGridItem>(IColumn<TGridItem> Column);
