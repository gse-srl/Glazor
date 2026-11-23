
export function registerObserverCallback(observed, dotNetObjRef, overMethodName, clickMethodName, enabled) {
    var observer = new Observer(dotNetObjRef, overMethodName, clickMethodName, enabled);
    var gObserver = observed.$glazor$clamperObserver;
    if (!gObserver) {
        gObserver = new ClamperObserver(observed, observer);
        observed.$glazor$clamperObserver = gObserver;
    }
    else {
        gObserver.reset(observer);
    }
}

function Observer(dotNetObjRef, overMethodName, clickMethodName, enabled) {
    var self = this;
    self.over = null;
    this.mouseOver = function (over, width, height) {
        processMouseOver(over, width, height);
    };
    this.mouseClick = function (width, height) {
        processMouseClick(width, height);
    };
    this.isEnabled = function () {
        return enabled;
    };
    async function processMouseOver(over, width, height) {
        if (enabled) {
            if (self.over !== over) {
                self.over = over;   // before the call: the moves made while it is in flight do not send it again
                await dotNetObjRef.invokeMethodAsync(overMethodName, over, width, height);
            }
        }
    }
    async function processMouseClick(width, height) {
        if (enabled) {
            await dotNetObjRef.invokeMethodAsync(clickMethodName, width, height);
        }
    }
};

/* 
 * interface Observer {
 *   void mouseOver(bool over, int width, int height); 
 *   void mouseClick(int width, int height); 
 *   boolean isEnabled();     
 * }
 */
function ClamperObserver(observed, s) {
    var self = this;

    // A single line (.ellipsis, nowrap) overflows horizontally; a line-clamped box wraps and overflows vertically.
    function isClamped() {
        return observed.offsetWidth < observed.scrollWidth || observed.clientHeight < observed.scrollHeight;
    }

    self.reset = function (observer) {
        self._observed = observed;
        self._src = observer;

        observed.onmousemove = function (e) {
            // check if the ellipsis are visible; a disabled clamper opens no popup, so it marks no hot zone
            if (observer.isEnabled() && isClamped()) {
                var cr = observed.getBoundingClientRect();
                if (e.clientX >= (cr.x + cr.width - 20)) {  // 20 is for the ellipsis
                    observed.classList.add("over-ellipsis");
                    observer.mouseOver(true, cr.width, cr.height);
                }
                else {
                    observed.classList.remove("over-ellipsis");
                    observer.mouseOver(false, cr.width, cr.height);
                }
            }
        };
        observed.onmouseout = function () {
            // check if the ellipsis are visible
            if (isClamped()) {
                observed.classList.remove("over-ellipsis");
                var cr = observed.getBoundingClientRect();
                observer.mouseOver(false, cr.width, cr.height);
            }
        };
        observed.onclick = function (e) {
            // check if the ellipsis are visible
            if (isClamped()) {
                var cr = observed.getBoundingClientRect();
                if (e.clientX >= (cr.x + cr.width - 20)) {
                    observer.mouseClick(cr.width, cr.height);
                }
            }
        };
    };

    self.reset(s);
};
