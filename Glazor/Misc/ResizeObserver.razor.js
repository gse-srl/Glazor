
export function registerResizeObserverCallback(observed, dotNetObjRef, methodName, enabled) {
    if (!observed)   // removed by the render that passed it: the next render passes its replacement
        return;
    var observer = new Observer(dotNetObjRef, methodName, enabled);
    var gresizeObserver = observed.$glazor$resizeObserver;
    if (!gresizeObserver) {
        gresizeObserver = new GResizeObserver(observed, observer);
        observed.$glazor$resizeObserver = gresizeObserver;
    }
    else {
        gresizeObserver.reset(observer);
    }
}

export function cleanup(observed) {
    if (!observed)   // a reference to an element removed from the DOM arrives as null
        return;
    var gresizeObserver = observed.$glazor$resizeObserver;
    observed.$glazor$resizeObserver = null;
    if (gresizeObserver) {
        gresizeObserver.disconnect();
    }
}

function Observer(dotNetObjRef, methodName, enabled) {
    this.resized = function (width, height) {
        processResize(width, height);
    };
    this.isEnabled = function () {
        return enabled;
    };
    async function processResize(width, height) {
        if (enabled) {
            await dotNetObjRef.invokeMethodAsync(methodName, width, height);
        }
    }
};

/* 
 * interface Observer {
 *   void resized(int width, int height); 
 *   boolean isEnabled();     
 * }
 */
function GResizeObserver(observed, s) {
    var self = this;
    // https://developer.mozilla.org/en-US/docs/Web/API/ResizeObserver
    var resObserver = new ResizeObserver((entries) => {
        if (self._disabled)
            return;
        for (const entry of entries) {
            if (!entry.target.isConnected)   // removed by a render: its 0x0 is no resize, and cleanup gets it as null
                continue;
            if (entry.target.$glazor$resizeObserver !== self)
                throw new Error("Unexpected");
            const cr = entry.contentRect;
            if (self._oldWidth == cr.width && self._oldHeight == cr.height)
                continue;
            self._oldWidth = cr.width;
            self._oldHeight = cr.height;
            entry.target.$glazor$resizeObserver._src.resized(cr.width, cr.height);
        }
    });
    self._src = s;
    self._oldWidth = null;
    self._oldHeight = null;

    self.reset = function (observer) {
        //Arguments that were passed
        self._observed = observed;
        self._src = observer;
        //Height and position properties
        self._disabled = !self._src.isEnabled();

        resObserver.unobserve(observed);
        resObserver.observe(observed);
    };

    self.disconnect = function (observer) {
        self._observed = null;
        self._src = null;
        resObserver.disconnect();
    }

    self.reset(s);
};
