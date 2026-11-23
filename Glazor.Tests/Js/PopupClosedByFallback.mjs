// Drives Popup.razor.js as a browser without closedby on <dialog> (Safari) runs it: the popup closes itself as its closedby
// asks, "any" on Esc and on a press outside, "closerequest" on Esc only, "none" never, and leaves no listener once closed.
// Usage: node PopupClosedByFallback.mjs <path to Popup.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";

globalThis.HTMLDialogElement = class { };   // no closedBy: the module falls back
globalThis.window = { innerWidth: 800, innerHeight: 600 };
const listeners = [];
globalThis.document = {
    addEventListener(type, fn) { listeners.push({ type, fn }); },
    removeEventListener(type, fn) {
        const i = listeners.findIndex(l => l.type === type && l.fn === fn);
        if (i >= 0)
            listeners.splice(i, 1);
    },
};
const { show } = await import(pathToFileURL(process.argv[2]).href);

const dispatch = (type, e) => { for (const l of listeners.filter(l => l.type === type)) l.fn(e); };
const inside = {}, outside = {};
const popup = by => {
    const onClose = [];
    return {
        open: false, offsetWidth: 10, offsetHeight: 10, style: {},
        show() { this.open = true; },
        close() {
            if (!this.open)
                return;
            this.open = false;
            for (const handler of onClose.splice(0))
                handler();
        },
        getAttribute() { return by; },
        contains(target) { return target === inside; },
        addEventListener(type, fn) { if (type === "close") onClose.push(fn); },
    };
};
const tryIt = by => {
    const p = popup(by);
    show(p);
    dispatch("keydown", { key: "Escape" });
    const escape = !p.open;
    p.close();
    show(p);
    dispatch("pointerdown", { target: outside });
    dispatch("pointerup", { target: outside });
    const pressOutside = !p.open;
    p.close();
    return { escape, pressOutside };
};

console.log(JSON.stringify({ any: tryIt("any"), closerequest: tryIt("closerequest"), none: tryIt("none"), listenersLeft: listeners.length }));
