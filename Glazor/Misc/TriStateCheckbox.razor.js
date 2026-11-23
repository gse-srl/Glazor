export function setState(el, indeterminate, checked) {
    // Both are DOM properties. "checked" is set here too because after an indeterminate -> unchecked toggle the
    // rendered attribute does not change, so Blazor emits no DOM edit and the box would stay ticked.
    el.indeterminate = indeterminate;
    el.checked = checked;
}
