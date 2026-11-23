// Drives Popup.razor.js through fake dialogs: showing a popup closes the one open before it, and a popup shown at a point
// that would cross the right or bottom edge of the viewport opens to the left of, or above, the point.
// Usage: node PopupShow.mjs <path to Popup.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";

globalThis.HTMLDialogElement = class { };
HTMLDialogElement.prototype.closedBy = "any";   // the browser does closedby itself
globalThis.window = { innerWidth: 800, innerHeight: 600 };
const { show } = await import(pathToFileURL(process.argv[2]).href);

const popup = (name, width = 100, height = 50) => ({
    name, open: false, closes: 0, offsetWidth: width, offsetHeight: height, style: { left: "", top: "", positionArea: "" },
    show() { this.open = true; },
    close() { this.open = false; this.closes++; },
    getAttribute() { return "any"; },
});

const first = popup("first"), second = popup("second");
show(first);
show(second);
show(second);   // shown again: the one open is itself
const placed = (p, x, y) => { show(p, x, y); return `${p.style.left},${p.style.top}`; };

// shown at a point twice, then without one: the corner and the anchor's area the Style gave it come back
const both = popup("both"), fixedByStyle = popup("fixedByStyle");
both.style.positionArea = "bottom span-right";
fixedByStyle.style.left = "5px";
const pointThenStyle = p => {
    show(p, 50, 40);
    const areaAtThePoint = p.style.positionArea;
    show(p, 60, 45);
    show(p);
    return `${p.style.left},${p.style.top},${areaAtThePoint}->${p.style.positionArea}`;
};

console.log(JSON.stringify({
    first: { open: first.open, closes: first.closes },
    second: { open: second.open, closes: second.closes },
    inside: placed(popup("inside"), 50, 40),
    rightEdge: placed(popup("rightEdge"), 750, 20),
    bottomEdge: placed(popup("bottomEdge"), 10, 580),
    tallerThanTheViewport: placed(popup("tall", 100, 700), 30, 590),
    anchoredAfterAPoint: pointThenStyle(both),
    styleCornerAfterAPoint: pointThenStyle(fixedByStyle),
    pointAgain: placed(both, 70, 50),
}));
