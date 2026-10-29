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

// Two fixed text copies loop without adding nodes during animation.
const status = document.querySelector('.toolbar-status');
if (status) {
    const light = status.querySelector('i');
    const message = status.textContent.trim();
    const viewport = document.createElement('span');
    viewport.className = 'status-viewport';
    const track = document.createElement('span');
    track.className = 'status-track';
    const original = document.createElement('span');
    original.textContent = message;
    const copy = original.cloneNode(true);
    copy.setAttribute('aria-hidden', 'true');
    track.append(original, copy);
    viewport.append(track);
    status.replaceChildren(light, viewport);
    let motion;
    let speed = 70;
    const update = () => {
        motion?.cancel();
        const overflowing = original.scrollWidth > viewport.clientWidth;
        copy.hidden = !overflowing;
        if (!overflowing || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        const distance = original.getBoundingClientRect().width;
        motion = track.animate([
            { transform: 'translateX(0)' },
            { transform: `translateX(-${distance}px)` }
        ], { duration: distance / speed * 1000, iterations: Infinity, easing: 'linear' });
        if (status.matches(':hover')) motion.pause();
    };
    const controls = document.createElement('label');
    controls.className = 'speed-control';
    controls.innerHTML = 'Hız <input type="range" min="5" max="150" value="70" aria-label="Kayan yazı hızı"><output>70 px/sn</output>';
    document.body.append(controls);
    const slider = controls.querySelector('input');
    slider.addEventListener('input', () => {
        speed = Number(slider.value);
        controls.querySelector('output').textContent = `${speed} px/sn`;
        update();
    });
    new ResizeObserver(update).observe(viewport);
    status.addEventListener('mouseenter', () => motion?.pause());
    status.addEventListener('mouseleave', () => motion?.play());
    update();
}