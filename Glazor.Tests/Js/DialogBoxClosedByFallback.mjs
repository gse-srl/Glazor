// Drives DialogBox.razor.js as a browser without closedby on <dialog> (Safari) runs it. A non-modal dialog closes on Esc for
// "any" and "closerequest", and on a press outside for "any". A modal one keeps Esc for "none" and closes on a press on its
// backdrop for "any". Once closed it leaves no listener, and shown again while open it adds none.
// Usage: node DialogBoxClosedByFallback.mjs <path to DialogBox.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";

globalThis.HTMLDialogElement = class { };   // no closedBy: the module falls back

// A listener is removed only with the capture flag it was added with, as in the DOM
const capture = options => typeof options === "boolean" ? options : !!options?.capture;
const target = () => {
    const listeners = [];
    return {
        listeners,
        addEventListener(type, fn, options) { listeners.push({ type, fn, capture: capture(options), once: !!options?.once }); },
        removeEventListener(type, fn, options) {
            const i = listeners.findIndex(l => l.type === type && l.fn === fn && l.capture === capture(options));
            if (i >= 0)
                listeners.splice(i, 1);
        },
        dispatch(type, e) {
            for (const l of listeners.filter(l => l.type === type)) {
                if (l.once)
                    listeners.splice(listeners.indexOf(l), 1);
                l.fn(e);
            }
        },
    };
};
globalThis.document = target();
const { show, showModal } = await import(pathToFileURL(process.argv[2]).href);

const inside = {}, outside = {};
const rect = { left: 100, right: 200, top: 100, bottom: 200 };
const dialog = by => {
    const d = Object.assign(target(), {
        open: false,
        show() { this.open = true; },
        showModal() { this.open = true; },
        close() {
            if (!this.open)
                return;
            this.open = false;
            this.dispatch("close", {});
        },
        getAttribute(name) { return name === "closedby" ? by : null; },
        contains(t) { return t === inside || t === d; },
        getBoundingClientRect() { return rect; },
    });
    return d;
};
const press = (t, x, y) => {
    const e = { target: t, clientX: x, clientY: y };
    document.dispatch("pointerdown", e);
    document.dispatch("pointerup", e);
};
// A modal dialog's Esc: the browser fires cancel, and closes it unless a listener prevents that
const escapeModal = d => {
    const e = { defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    d.dispatch("cancel", e);
    if (!e.defaultPrevented)
        d.close();
};
const left = d => document.listeners.length + d.listeners.length;

const nonModal = by => {
    const d = dialog(by);
    show(d);
    show(d);   // shown again while open
    const listeners = document.listeners.length;
    document.dispatch("keydown", { key: "Escape" });
    const escape = !d.open;
    d.close();
    show(d);
    press(inside, 150, 150);
    const pressInside = !d.open;
    press(outside, 10, 10);
    const pressOutside = !d.open;
    d.close();
    return { escape, pressInside, pressOutside, listeners, left: left(d) };
};

const modal = by => {
    const d = dialog(by);
    showModal(d);
    escapeModal(d);
    const escape = !d.open;
    d.close();
    showModal(d);
    press(d, 150, 150);   // on the dialog itself
    const pressInside = !d.open;
    press(d, 10, 10);   // on its backdrop, which targets the dialog
    const pressBackdrop = !d.open;
    d.close();
    return { escape, pressInside, pressBackdrop, left: left(d) };
};

console.log(JSON.stringify({
    nonModal: { any: nonModal("any"), closerequest: nonModal("closerequest"), none: nonModal("none") },
    modal: { any: modal("any"), closerequest: modal("closerequest"), none: modal("none") },
}));
