namespace Glazor.Grds.Filters;

/// <summary>
/// The untyped side of <see cref="IWithInjectedParams{SELF}"/>, which is the one to implement.
/// </summary>
public interface IWithInjectedParams
{
    internal AFilterDef? InjectParamsInto(AFilterDef toBeInjected);
}

/// <summary>
/// Defines a contract for injecting parameters into deserialized filter definitions of a specific type.
/// </summary>
/// <typeparam name="SELF">The type of filter definition that parameters will be injected into.</typeparam>
public interface IWithInjectedParams<SELF> : IWithInjectedParams where SELF : AFilterDef
{
    /// <summary>
    /// Called on the column's own definition with one a memento restored, which has the user's choices but not what the page
    /// supplied: returns a definition with both. <see cref="ADataGrid{TGridItem}.RestoreMemento"/> applies what it returns.
    /// </summary>
    public SELF InjectParamsInto(SELF toBeInjected);

    AFilterDef? IWithInjectedParams.InjectParamsInto(AFilterDef toBeInjected)
    {
        return toBeInjected is SELF self ? InjectParamsInto(self) : null;
    }
}
