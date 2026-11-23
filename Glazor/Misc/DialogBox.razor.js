// A browser without closedby on <dialog> (Safari): the dialog does itself what its closedby asks
const closedByMissing = !('closedBy' in HTMLDialogElement.prototype);

export function show(dialog) {
    dialog.show();
    if (closedByMissing)
        followClosedBy(dialog, false);
}

export function showModal(dialog) {
    dialog.showModal();
    if (closedByMissing)
        followClosedBy(dialog, true);
}

export function close(dialog) {
    dialog.close?.();
}

// A modal dialog closes on Esc by itself, a non-modal one does not: "none" keeps the first open, "any" and "closerequest"
// close the second on Esc, and "any" closes either on a press outside it
function followClosedBy(dialog, modal) {
    if (dialog.$glazor$closedBy)
        return;   // shown again while open
    const by = dialog.getAttribute('closedby');
    const outside = e => {
        const r = dialog.getBoundingClientRect();   // a press on a modal's backdrop targets the dialog itself
        return !dialog.contains(e.target) || e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
    };
    const listeners = [];
    const listen = (target, type, handler) => {
        target.addEventListener(type, handler, true);
        listeners.push([target, type, handler]);
    };
    if (modal && by === 'none')
        listen(dialog, 'cancel', e => e.preventDefault());
    if (!modal && (by === 'any' || by === 'closerequest'))
        listen(document, 'keydown', e => { if (e.key === 'Escape') dialog.close(); });
    if (by === 'any') {   // closed on the pointerup of a press begun outside, as the native one is
        let pressedOutside = false;
        listen(document, 'pointerdown', e => { pressedOutside = outside(e); });
        listen(document, 'pointerup', e => { if (pressedOutside && outside(e)) dialog.close(); pressedOutside = false; });
    }
    if (listeners.length === 0)
        return;
    dialog.$glazor$closedBy = true;
    dialog.addEventListener('close', () => {
        for (const [target, type, handler] of listeners)
            target.removeEventListener(type, handler, true);
        dialog.$glazor$closedBy = false;
    }, { once: true });
}
