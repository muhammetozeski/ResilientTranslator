// Browser-only geometry and element projection. Component interaction policies live in C#.
export function bind(element) {
    if (!element?.isConnected) throw new Error('The component element is not attached.');
    return {
        bounds() {
            const r = element.getBoundingClientRect();
            return { x:r.x, y:r.y, width:r.width, height:r.height, viewportWidth:innerWidth, viewportHeight:innerHeight };
        },
        position(x,y,mode) { element.style.position=mode; element.style.left=`${x}px`; element.style.top=`${y}px`; }
    };
}
export function observeViewport(callback) {
    let active=true, scheduled=false;
    const changed=()=>{
        if(!active||scheduled)return;
        scheduled=true;
        requestAnimationFrame(()=>{
            scheduled=false;
            if(active)callback.invokeMethodAsync('ViewportChanged').catch(error=>{if(active)console.error(error);});
        });
    };
    const events=['resize','scroll','blur'];
    for(const event of events)window.addEventListener(event,changed);
    return { dispose(){active=false;for(const event of events)window.removeEventListener(event,changed);} };
}
