const tickers = new WeakMap();

/** Refreshes a two-copy marquee; no elements are allocated during animation. */
export function update(element, speed) {
    let state = tickers.get(element);
    if (!state) {
        const viewport = element.querySelector('.status-viewport');
        const track = element.querySelector('.status-track');
        state = { viewport, track, speed, motion: null };
        state.refresh = () => {
            state.motion?.cancel();
            const [original, copy] = track.children;
            const spacing = parseFloat(getComputedStyle(original).paddingRight);
            const width = original.getBoundingClientRect().width;
            const overflow = width - spacing > viewport.clientWidth;
            copy.hidden = !overflow;
            if (!overflow || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
            state.motion = track.animate([
                { transform: 'translateX(0)' },
                { transform: `translateX(-${width}px)` }
            ], { duration: width / state.speed * 1000, iterations: Infinity, easing: 'linear' });
            if (element.matches(':hover')) state.motion.pause();
        };
        state.pause = () => state.motion?.pause();
        state.resume = () => state.motion?.play();
        state.observer = new ResizeObserver(state.refresh);
        state.observer.observe(viewport);
        element.addEventListener('mouseenter', state.pause);
        element.addEventListener('mouseleave', state.resume);
        tickers.set(element, state);
    }
    state.speed = speed;
    state.refresh();
}

/** Removes all observers and animation ownership for a disposed component. */
export function dispose(element) {
    const state = tickers.get(element);
    if (!state) return;
    state.motion?.cancel();
    state.observer.disconnect();
    element.removeEventListener('mouseenter', state.pause);
    element.removeEventListener('mouseleave', state.resume);
    tickers.delete(element);
}
