// Presentation-only behavior; these pages never call the translation engine.
const preview = document.getElementById('preview');
if (preview) {
    const frame = document.getElementById('preview-window');
    const dimensions = document.getElementById('dimensions');
    document.querySelectorAll('[data-design]').forEach(link => {
        link.addEventListener('click', () => {
            document.querySelectorAll('[data-design]').forEach(item => item.classList.toggle('selected', item === link));
        });
    });
    document.querySelectorAll('[data-size]').forEach(button => {
        button.addEventListener('click', () => {
            const [width, height] = button.dataset.size.split(',').map(Number);
            frame.style.width = `${width}px`;
            frame.style.height = `${height}px`;
        });
    });
    new ResizeObserver(() => {
        dimensions.textContent = `${Math.round(frame.clientWidth)} × ${Math.round(frame.clientHeight)}`;
    }).observe(frame);
} else {
    document.querySelector('[data-action="languages"]').addEventListener('click', () => {
        const source = document.getElementById('source');
        const target = document.getElementById('target');
        [source.value, target.value] = [target.value, source.value];
    });
    document.querySelector('[data-action="texts"]').addEventListener('click', () => {
        const input = document.getElementById('input');
        const output = document.getElementById('output');
        [input.value, output.value] = [output.value, input.value];
    });
}

// Animate only overflowing status text; recalculate after resizing.
const status = document.querySelector('.toolbar-status');
if (status) {
    const track = document.createElement('span');
    track.className = 'status-track';
    while (status.firstChild) track.append(status.firstChild);
    status.append(track);
    let motion;
    const update = () => {
        motion?.cancel();
        const distance = track.scrollWidth - status.clientWidth;
        if (distance <= 0 || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        const travel = Math.max(2000, distance / 28 * 1000);
        const duration = travel * 2 + 2400;
        const pause = 1200 / duration;
        motion = track.animate([
            { transform: 'translateX(0)', offset: 0 },
            { transform: 'translateX(0)', offset: pause },
            { transform: `translateX(-${distance}px)`, offset: .5 },
            { transform: `translateX(-${distance}px)`, offset: .5 + pause },
            { transform: 'translateX(0)', offset: 1 }
        ], { duration, iterations: Infinity, easing: 'linear' });
        if (status.matches(':hover')) motion.pause();
    };
    new ResizeObserver(update).observe(status);
    status.addEventListener('mouseenter', () => motion?.pause());
    status.addEventListener('mouseleave', () => motion?.play());
    update();
}