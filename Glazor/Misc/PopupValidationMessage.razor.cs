using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace Glazor.Misc;

/// <summary>
/// A field's validation message behind an error icon: a click on the icon, or with <see cref="ShowWhenMouseOver"/> the mouse over
/// it, shows the message in a popup.
/// </summary>
public partial class PopupValidationMessage<TValue> : ComponentBase, IDisposable
{
    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;
    private IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    [Inject] ILogger<PopupValidationMessage<TValue>> Logger { get; init; } = null!;

    private EditContext? _previousEditContext;
    private Expression<Func<TValue>>? _previousFieldAccessor;
    private readonly EventHandler<ValidationStateChangedEventArgs>? _validationStateChangedHandler;
    private FieldIdentifier _fieldIdentifier;

    /// <summary>
    /// Attributes passed on to the <see cref="ValidationMessage{TValue}"/> in the popup, which applies them to each message's
    /// <c>div</c>.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [CascadingParameter] EditContext CurrentEditContext { get; set; } = default!;

    /// <summary>
    /// Specifies the field for which validation messages should be displayed.
    /// </summary>
    [Parameter] public Expression<Func<TValue>>? For { get; set; }

    /// <summary>Classes added to the icon's own.</summary>
    [Parameter] public string? Class { get; set; }
    /// <summary>Styles added to the icon's own.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Also opens the message when the mouse moves over the icon; it stays open until Esc, a click outside or another popup.</summary>
    [Parameter] public bool ShowWhenMouseOver { get; set; }

    private readonly string _anchor = "--glazor-" + Guid.NewGuid().ToString("N");   // known on the first render, as an element reference is not
    private Popup? _popupRef = null;

    /// <summary>
    /// Constructs an instance of <see cref="PopupValidationMessage{TValue}"/>.
    /// </summary>
    public PopupValidationMessage()
    {
        _validationStateChangedHandler = (sender, eventArgs) =>
        {
            StateHasChanged();
        };
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (CurrentEditContext == null)
        {
            throw new InvalidOperationException($"{GetType()} requires a cascading parameter " +
                $"of type {nameof(EditContext)}. For example, you can use {GetType()} inside " +
                $"an {nameof(EditForm)}.");
        }

        if (For == null) // Not possible except if you manually specify T
        {
            throw new InvalidOperationException($"{GetType()} requires a value for the " +
                $"{nameof(For)} parameter.");
        }
        else if (For != _previousFieldAccessor)
        {
            _fieldIdentifier = FieldIdentifier.Create(For);
            _previousFieldAccessor = For;
        }

        if (CurrentEditContext != _previousEditContext)
        {
            DetachValidationStateChangedListener();
            CurrentEditContext.OnValidationStateChanged += _validationStateChangedHandler;
            _previousEditContext = CurrentEditContext;
        }
    }

    /// <summary>Whether the edit context holds no validation message for the field <see cref="For"/> names.</summary>
    public bool IsValid() => CurrentEditContext.IsValid(_fieldIdentifier);

    /// <summary>
    /// Called to dispose this instance.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> if called within <see cref="IDisposable.Dispose"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }

    void IDisposable.Dispose()
    {
        DetachValidationStateChangedListener();
        Dispose(disposing: true);
    }

    private void DetachValidationStateChangedListener()
    {
        _previousEditContext?.OnValidationStateChanged -= _validationStateChangedHandler;
    }

    /// <summary>Shows the popup with the validation messages.</summary>
    protected virtual async Task ShowPopupAsync()
    {
        await _popupRef.RequireNotNull().ShowAsync();
    }

    /// <summary>Shows the popup with the validation messages, or closes it when it is shown.</summary>
    protected virtual async Task TogglePopupAsync()
    {
        var popup = _popupRef.RequireNotNull();
        if (popup.IsVisible)
            await popup.CloseAsync();
        else
            await popup.ShowAsync();
    }
}
