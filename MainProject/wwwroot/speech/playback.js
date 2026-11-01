// Owns one cached recording and one player. Inference reuses the existing ONNX runtime.
export function create() {
    let worker, pending, player, url, cachedText, finish;
    function stop() {
        if (pending) {
            pending.reject(new Error('Speech generation canceled'));
            pending = undefined; worker?.terminate(); worker = undefined;
        }
        if (player) { player.pause(); player.currentTime = 0; }
        if (finish) { const done = finish; finish = undefined; done(false); }
    }
    function discard() {
        if (player) { player.src = ''; player.load(); player = undefined; }
        if (url) URL.revokeObjectURL(url);
        url = undefined; cachedText = undefined;
    }
    function wav(samples) {
        const data = new ArrayBuffer(44 + samples.length * 2), view = new DataView(data);
        const label = (at, text) => { for (let i = 0; i < text.length; i++) view.setUint8(at + i, text.charCodeAt(i)); };
        label(0, 'RIFF'); view.setUint32(4, data.byteLength - 8, true); label(8, 'WAVE'); label(12, 'fmt ');
        view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
        view.setUint32(24, 24000, true); view.setUint32(28, 48000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
        label(36, 'data'); view.setUint32(40, samples.length * 2, true);
        for (let i = 0; i < samples.length; i++) view.setInt16(44 + i * 2, Math.round(Math.max(-1, Math.min(1, samples[i])) * 32767), true);
        return new Blob([data], { type: 'audio/wav' });
    }
    return {
        has: text => text === cachedText && !!url,
        stop,
        async prepare(text, sentences) {
            if (text === cachedText && url) return;
            stop();
            if (!worker) {
                const spawned = new Worker(new URL('worker.js', import.meta.url));
                worker = spawned;
                spawned.onmessage = ({ data }) => {
                    if (worker !== spawned || !pending) return;
                    const request = pending; pending = undefined;
                    if (data.error) request.reject(new Error(`${data.error.message}\n${data.error.stack || ''}`));
                    else request.resolve(data.audio);
                };
                spawned.onerror = error => {
                    if (worker !== spawned) return;
                    const request = pending; pending = undefined;
                    worker?.terminate(); worker = undefined;
                    request?.reject(new Error(error.message || 'Speech worker failed'));
                };
            }
            const samples = await new Promise((resolve, reject) => { pending = { resolve, reject }; worker.postMessage({ sentences }); });
            discard();
            url = URL.createObjectURL(wav(samples)); cachedText = text;
            player = new Audio(url); player.preservesPitch = true;
        },
        play(rate) {
            if (!player) throw new Error('Speech is not prepared');
            player.currentTime = 0; player.playbackRate = rate;
            return new Promise((resolve, reject) => {
                finish = resolve;
                player.onended = () => { finish = undefined; resolve(true); };
                player.onerror = () => { finish = undefined; reject(new Error(player.error?.message || 'Speech playback failed')); };
                player.play().catch(error => { finish = undefined; reject(error); });
            });
        },
        reset() { stop(); worker?.terminate(); worker = undefined; },
        dispose() { stop(); discard(); worker?.terminate(); worker = undefined; }
    };
}
