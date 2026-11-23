
var other = null;

// A browser without closedby on <dialog> (Safari): the popup closes itself as its closedby asks
const closedByMissing = !('closedBy' in HTMLDialogElement.prototype);

export function show(popup, x, y) {
    closeOthers(popup);
    popup.show?.(); // before setting position, so that offsetWidth/offsetHeight are correct
    const by = closedByMissing ? popup.getAttribute('closedby') : null;
    if (by === 'any' || by === 'closerequest')
        closeOnPressOutsideAndEscape(popup, by === 'any');
    if (x !== undefined && y !== undefined) {
        const width = popup.offsetWidth;
        const height = popup.offsetHeight;
        const windowWidth = window.innerWidth;
        const windowHeight = window.innerHeight;
        if (x + width > windowWidth) {
            x = x - width;
        }
        if (y + height > windowHeight) {
            y = y - height;
        }
        // the Style's own, for a show without a point; an anchor's area would take left and top as offsets within it
        popup.$glazor$styleCorner ??= { left: popup.style.left, top: popup.style.top, positionArea: popup.style.positionArea };
        popup.style.positionArea = 'none';
        popup.style.left = `${Math.max(0, x)}px`;
        popup.style.top = `${Math.max(0, y)}px`;
    }
    else if (popup.$glazor$styleCorner) {
        // shown at a point before: the Style places it again, its anchor with it
        popup.style.left = popup.$glazor$styleCorner.left;
        popup.style.top = popup.$glazor$styleCorner.top;
        popup.style.positionArea = popup.$glazor$styleCorner.positionArea;
        delete popup.$glazor$styleCorner;
    }
}

export function close(popup) {
    popup.close?.();
    if (other === popup)
        other = null;   // a dialog taken out of the page is not kept by the module
}

function closeOthers(popup) {
    if (other != null && other != popup) {
        close(other);
    }
    other = popup;
}

// Esc closes it, and with outsideToo ("any", not "closerequest") a press outside it
function closeOnPressOutsideAndEscape(popup, outsideToo) {
    if (popup.$glazor$lightDismiss)
        return;   // shown again while open
    // closed on the pointerup of a press begun outside, as the native one is: the click that follows reaches the host first
    let pressedOutside = false;
    const onDown = e => { pressedOutside = !popup.contains(e.target); };
    const onUp = e => { if (pressedOutside && !popup.contains(e.target)) popup.close(); pressedOutside = false; };
    const onKey = e => { if (e.key === 'Escape') popup.close(); };
    if (outsideToo) {
        document.addEventListener('pointerdown', onDown, true);
        document.addEventListener('pointerup', onUp, true);
    }
    document.addEventListener('keydown', onKey, true);
    popup.$glazor$lightDismiss = true;
    popup.addEventListener('close', () => {
        document.removeEventListener('pointerdown', onDown, true);
        document.removeEventListener('pointerup', onUp, true);
        document.removeEventListener('keydown', onKey, true);
        popup.$glazor$lightDismiss = false;
    }, { once: true });
}
