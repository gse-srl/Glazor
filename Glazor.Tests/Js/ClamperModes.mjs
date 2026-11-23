// Drives Clamper.razor.js against a fake DOM in its two modes: a single line clamped by nowrap, which overflows horizontally,
// and a two-line box clamped by -webkit-line-clamp, which wraps and overflows vertically. Prints the callbacks as JSON.
// Usage: node ClamperModes.mjs <path to Clamper.razor.js copied as .mjs>
import { pathToFileURL } from "node:url";
const { registerObserverCallback } = await import(pathToFileURL(process.argv[2]).href);

function El(metrics) {
    Object.assign(this, metrics);
    const classes = new Set();
    this.classList = { add: c => classes.add(c), remove: c => classes.delete(c), contains: c => classes.has(c) };
    this.getBoundingClientRect = () => ({ x: 0, y: 0, width: this.offsetWidth, height: this.clientHeight });
}

function probe(el) {
    const calls = [];
    registerObserverCallback(el, { invokeMethodAsync: async (method, ...args) => { calls.push(method + (method == "ProcessOver" ? ":" + args[0] : "")); } },
        "ProcessOver", "ProcessClick", true);
    return async (type, x, y) => {
        calls.length = 0;
        el["on" + type]({ clientX: x, clientY: y });
        await new Promise(r => setTimeout(r, 10));
        return { calls: [...calls], highlighted: el.classList.contains("over-ellipsis") };
    };
}

const singleLine = probe(new El({ offsetWidth: 100, scrollWidth: 300, clientHeight: 20, scrollHeight: 20 }));
const twoLines = probe(new El({ offsetWidth: 100, scrollWidth: 100, clientHeight: 40, scrollHeight: 120 }));
const notClamped = probe(new El({ offsetWidth: 100, scrollWidth: 100, clientHeight: 40, scrollHeight: 40 }));
console.log(JSON.stringify({
    singleLine: { overEllipsis: await singleLine("mousemove", 90, 10), click: await singleLine("click", 90, 10), elsewhere: await singleLine("mousemove", 10, 10) },
    twoLines: { overEllipsis: await twoLines("mousemove", 90, 35), click: await twoLines("click", 90, 35), elsewhere: await twoLines("mousemove", 10, 10) },
    notClamped: { move: await notClamped("mousemove", 90, 35), click: await notClamped("click", 90, 35) },
}));
