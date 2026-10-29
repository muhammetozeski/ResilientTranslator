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
