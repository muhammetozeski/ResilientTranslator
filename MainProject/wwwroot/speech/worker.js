// Reuses the already shipped ONNX Web runtime; no second runtime is distributed.
let session, vocabulary;
self.onmessage = async ({ data }) => {
    try {
        if (!session) {
            importScripts('../offline/vendor/toz/ort.wasm.min.js');
            ort.env.wasm.wasmPaths = new URL('../offline/vendor/toz/', self.location).href;
            ort.env.wasm.numThreads = 1;
            ort.env.wasm.proxy = false;
            const [model, words] = await Promise.all([
                fetch(new URL('paradee_int8.onnx', self.location)).then(r => { if (!r.ok) throw Error('Speech model unavailable'); return r.arrayBuffer(); }),
                fetch(new URL('vocabulary.json', self.location)).then(r => { if (!r.ok) throw Error('Speech vocabulary unavailable'); return r.json(); })
            ]);
            vocabulary = words;
            session = await ort.InferenceSession.create(new Uint8Array(model), { executionProviders: ['wasm'] });
        }
        const chunks = [];
        let length = 0;
        for (const sentence of data.sentences) {
            let remaining = sentence.trim();
            while (remaining) {
                let cut = remaining.length <= 510 ? remaining.length : remaining.lastIndexOf(' ', 510);
                if (cut <= 0) cut = Math.min(510, remaining.length);
                const part = remaining.slice(0, cut);
                remaining = remaining.slice(cut).trimStart();
                const ids = [0, ...Array.from(part).filter(c => vocabulary[c] !== undefined).map(c => vocabulary[c]), 0];
                if (ids.length <= 2) continue;
                const result = await session.run({
                    input_ids: new ort.Tensor('int64', BigInt64Array.from(ids, BigInt), [1, ids.length]),
                    speed: new ort.Tensor('float32', Float32Array.of(1), [1]),
                    voice: new ort.Tensor('int64', BigInt64Array.of(0n), [1])
                });
                const chunk = Float32Array.from(result.waveform.data);
                if (!chunk.length || !chunk.every(Number.isFinite)) throw Error('Invalid generated speech');
                chunks.push(chunk); length += chunk.length;
                for (const value of Object.values(result)) value.dispose();
            }
        }
        if (!length) throw Error('No speech generated');
        const audio = new Float32Array(length);
        let offset = 0;
        for (const chunk of chunks) { audio.set(chunk, offset); offset += chunk.length; }
        self.postMessage({ audio }, [audio.buffer]);
    } catch (error) { self.postMessage({ error: { message: error.message, stack: error.stack } }); }
};
