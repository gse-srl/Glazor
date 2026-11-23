using Glazor.Misc;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace Glazor.Grds.Filters;

/// <summary>
/// Represents a date range filter definition that allows specifying lower and upper bounds, as well as the option to include null values.
/// A <see cref="DateTimeOffset"/> bound is typed as a local date and time and takes the server's offset, not the browser's.
/// </summary>
/// <typeparam name="VT">The ones supported by <c>&lt;InputDate&gt;</c>, see: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.forms.inputdate-1?view=aspnetcore-10.0</typeparam>
public class DateDef<VT> : ARangeDef<DateDef<VT>, VT?> where VT : struct, IComparable<VT>
{
    /// <summary>Whether a null value passes the filter; true by default.</summary>
    public bool IncludeNull { get; set; } = true;

    /// <summary>The lower bound, included; null for none.</summary>
    public VT? From { get; set; }
    /// <summary>The upper bound, included with the whole minute or day the editor picks it to; null for none.</summary>
    public VT? To { get; set; }

    static DateDef()
    {
        // https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.forms.inputdate-1?view=aspnetcore-10.0
        if (!typeof(DateTime).IsAssignableFrom(typeof(VT))
            && !typeof(DateTimeOffset).IsAssignableFrom(typeof(VT))
            && !typeof(DateOnly).IsAssignableFrom(typeof(VT))
            && !typeof(TimeOnly).IsAssignableFrom(typeof(VT))
            )
        {
            throw new ArgumentException($"Not a supported type: {typeof(VT)}");
        }
    }

    /// <summary>The input the editor shows, by default from the type; a subclass over <see cref="DateTime"/> holding dates only returns <see cref="InputDateType.Date"/>, and overrides <see cref="Clone"/>.</summary>
    protected internal virtual InputDateType GetInputDateType()
    {
        if (typeof(DateOnly).IsAssignableFrom(typeof(VT)))
            return InputDateType.Date;
        if (typeof(TimeOnly).IsAssignableFrom(typeof(VT)))
            return InputDateType.Time;
        return InputDateType.DateTimeLocal;
    }

    /// <inheritdoc/>
    public override DateDef<VT> Clone()
    {
        return new DateDef<VT>
        {
            IncludeNull = this.IncludeNull,
            From = this.From,
            To = this.To
        };
    }

    /// <inheritdoc/>
    public override bool MustIncludeNull() => IncludeNull;

    /// <inheritdoc/>
    public override VT? GetFrom() => From;

    /// <inheritdoc/>
    public override VT? GetTo() => To;

    /// <summary>
    /// A <see cref="To"/> on a whole minute, or on a whole day with a date input, covers that minute or day, as the editor picks
    /// no finer: the range ends before the next one. A <see cref="To"/> set in code finer than that ends the range where it is.
    /// </summary>
    protected override (VT? End, bool Excluded) GetEnd()
    {
        if (To is not VT to)
            return (To, false);
        TimeSpan unit = GetInputDateType() switch
        {
            InputDateType.DateTimeLocal or InputDateType.Time => TimeSpan.FromMinutes(1),
            InputDateType.Date => TimeSpan.FromDays(1),
            _ => TimeSpan.Zero,
        };
        if (unit == TimeSpan.Zero)
            return (To, false);
        switch (to)
        {
            // The last minute or day there is has no next one: nothing lies past it
            case DateTime d when d.TimeOfDay.Ticks % unit.Ticks == 0:
                return DateTime.MaxValue - d >= unit ? ((VT)(object)(d + unit), true) : (null, false);
            case DateTimeOffset d when d.TimeOfDay.Ticks % unit.Ticks == 0:
                if (DateTimeOffset.MaxValue - d < unit)
                    return (null, false);
                // In UTC when a positive offset puts the next one's clock time past year 9999, the instant being in range
                return ((VT)(object)(DateTime.MaxValue - d.DateTime >= unit ? d + unit : d.ToUniversalTime() + unit), true);
            case TimeOnly t when unit == TimeSpan.FromMinutes(1) && t.Ticks % unit.Ticks == 0:
                // The last minute of the day has no next one: it ends at the day's last tick, included
                return t == new TimeOnly(23, 59) ? ((VT)(object)TimeOnly.MaxValue, false) : ((VT)(object)t.Add(unit), true);
            default:
                return (To, false);
        }
    }

    private static readonly FilterDefRendererType<DateDefRenderer<VT>> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor

    /// <inheritdoc/>
    public override DateDef<VT> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        IncludeNull = ser.IncludeNull;
        From = ser.From;
        To = ser.To;
        return this;
    }
}
