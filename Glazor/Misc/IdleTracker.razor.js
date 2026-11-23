export function startTracker(idleSpanRef, dotNetObjRef, timeOutMS) {
    var currentTracker = idleSpanRef.$glazor$idleTracker;
    if (!currentTracker) {
        currentTracker = new IdleTracker(dotNetObjRef, timeOutMS);
        idleSpanRef.$glazor$idleTracker = currentTracker;
    }
    else {
        currentTracker.reset(dotNetObjRef, timeOutMS);
    }
}

export function cleanup(idleSpanRef) {
    var currentTracker = idleSpanRef.$glazor$idleTracker;
    if (!currentTracker)
        return;
    delete idleSpanRef.$glazor$idleTracker;
    currentTracker.cleanup();
}

// Listen to major user interaction events. "wheel" is how a grid is paged without moving the mouse.
const activityEvents = ['mousemove', 'mousedown', 'keydown', 'click', 'scroll', 'wheel', 'touchstart'];
// Capture phase: the grid's key and mouse handlers stop propagation of the events they handle, and an element's
// scroll event does not bubble at all, so a bubble-phase listener on window would miss a user working in a grid.
const listenerOptions = { capture: true, passive: true };

function IdleTracker(dotNetObjRef, timeOutMS) {
    var self = this;
    var timer = null;
    var idle = false;

    activityEvents.forEach(evt => {
        window.addEventListener(evt, onActivity, listenerOptions);
    });
    // Set initial timer
    resetIdleTimer();

    function onActivity() {
        if (idle) {
            idle = false;
            if (dotNetObjRef) {
                dotNetObjRef.invokeMethodAsync('UserIsActive');
            }
        }
        resetIdleTimer();
    }

    function resetIdleTimer() {
        clearTimeout(timer);
        // Alert C# when the user has finally gone idle after a period of activity.
        timer = setTimeout(() => {
            idle = true;
            if (dotNetObjRef) {
                dotNetObjRef.invokeMethodAsync('UserIsIdle');
            }
        }, timeOutMS);
    }

    self.reset = function(_dotNetObjRef, _timeOutMS) {
        dotNetObjRef = _dotNetObjRef;
        timeOutMS = _timeOutMS;
        resetIdleTimer();
    }

    self.cleanup = function () {
        clearTimeout(timer);
        activityEvents.forEach(evt => {
            window.removeEventListener(evt, onActivity, listenerOptions);   // must match the capture flag of the add
        });
    };
}
