// Drives TouchScroller.razor.js against a fake element: a two-finger swipe of ten steps, each one row high, with the
// re-registration every render makes arriving after each scroll step. A slow swipe of ten quarter-row steps too. Prints the scroll calls as JSON.
// Usage: node TouchScrollerGesture.mjs <path to TouchScroller.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerScrollerCallback } = await import(pathToFileURL(process.argv[2]).href);

function El() {
    const listeners = {};
    this.offsetHeight = 400;   // ten rows of 40 px for a page size of 10
    this.passive = {};
    this.addEventListener = (type, fn, options) => { (listeners[type] ||= []).push(fn); this.passive[type] = options?.passive === true; };
    this.removeEventListener = (type, fn) => { listeners[type] = (listeners[type] || []).filter(f => f !== fn); };
    this.dispatch = (type, y, fingers = 2) => {
        const evt = { touches: y === undefined ? [] : Array.from({ length: fingers }, () => ({ clientX: 0, clientY: y })), cancelable: true, prevented: false,
            preventDefault() { this.prevented = true; } };
        for (const fn of [...(listeners[type] || [])]) fn(evt);
        return evt.prevented;
    };
}

async function swipe(enabledAfterFirstStep, stepPx = 40) {
    const el = new El();
    const scrolls = [];
    const dotNet = { invokeMethodAsync: async (method, dx, dy) => { scrolls.push(dy); } };
    registerScrollerCallback(el, dotNet, "ProcessScrollBy", 10, 2, true);
    el.dispatch("touchstart", 500);
    const prevented = [];
    for (let step = 1; step <= 10; step++) {
        prevented.push(el.dispatch("touchmove", 500 - step * stepPx));
        await new Promise(r => setTimeout(r, 1));
        registerScrollerCallback(el, dotNet, "ProcessScrollBy", 10, 2, enabledAfterFirstStep);   // the render after the step
    }
    el.dispatch("touchend");
    return { scrolls, prevented };
}

// Registered for two fingers, then re-registered for one, as a render after a change of Fingers does: a one-finger swipe scrolls
function fingersChanged() {
    const el = new El();
    const scrolls = [];
    const dotNet = { invokeMethodAsync: async (method, dx, dy) => { scrolls.push(dy); } };
    registerScrollerCallback(el, dotNet, "ProcessScrollBy", 10, 2, true);
    registerScrollerCallback(el, dotNet, "ProcessScrollBy", 10, 1, true);
    el.dispatch("touchstart", 500, 1);
    for (let step = 1; step <= 10; step++)
        el.dispatch("touchmove", 500 - step * 40, 1);
    el.dispatch("touchend");
    return { scrolls };
}

// The start of a gesture is left to the browser, so a tap still clicks; the first move is taken
function tap(fingers) {
    const el = new El();
    registerScrollerCallback(el, { invokeMethodAsync: async () => { } }, "ProcessScrollBy", 10, fingers, true);
    const startPrevented = el.dispatch("touchstart", 500, fingers);
    const movePrevented = el.dispatch("touchmove", 460, fingers);
    el.dispatch("touchend");
    return { startPrevented, movePrevented, startPassive: el.passive.touchstart, movePassive: el.passive.touchmove };
}

// Registered disabled, as every grid is by default, then enabled by a later render: no listener that can cancel a move until then
function enabledLater() {
    const el = new El();
    const scrolls = [];
    const dotNet = { invokeMethodAsync: async (method, dx, dy) => { scrolls.push(dy); } };
    registerScrollerCallback(el, dotNet, "ProcessScrollBy", 10, 2, false);
    const disabledListens = "touchmove" in el.passive || "touchend" in el.passive;
    registerScrollerCallback(el, dotNet, "ProcessScrollBy", 10, 2, true);
    el.dispatch("touchstart", 500);
    const movePrevented = el.dispatch("touchmove", 460);
    el.dispatch("touchend");
    return { disabledListens, movePassive: el.passive.touchmove, movePrevented, scrolls };
}

// a slow drag: 10 px per move, a quarter of a row
console.log(JSON.stringify({ enabled: await swipe(true), disabledMidGesture: await swipe(false), slow: await swipe(true, 10), fingersChanged: fingersChanged(),
    tapOne: tap(1), tapTwo: tap(2), enabledLater: enabledLater() }));
