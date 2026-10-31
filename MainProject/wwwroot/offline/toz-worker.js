// Inference follows the published Töz encoder/decoder and beam-search flow.
// The ONNX and SentencePiece runtimes are supplied libraries, not converted models.
let encoder, decoder, processor, beamSize = 4;
const tensor = (values, dimensions) => new ort.Tensor('int64', BigInt64Array.from(values, BigInt), dimensions);

async function initialize(configuration) {
    importScripts('./vendor/toz/ort.wasm.min.js');
    ort.env.wasm.wasmPaths = new URL('vendor/toz/', configuration.root).href;
    const canShareMemory = typeof SharedArrayBuffer !== 'undefined' && self.crossOriginIsolated;
    ort.env.wasm.numThreads = canShareMemory ? configuration.threads : 1;
    if (!canShareMemory && configuration.threads > 1)
        self.postMessage({ log: { level: 'information', message: 'WebView shared memory unavailable; ONNX uses one thread per worker' } });
    ort.env.wasm.proxy = false;
    const modelRoot = new URL('models/toz/', configuration.root);
    const [encoderBytes, decoderBytes, vocabulary] = await Promise.all(['encoder_q8.onnx', 'decoder_q8.onnx', 'tokenizer.model'].map(async name => {
        const response = await fetch(new URL(name, modelRoot));
        if (!response.ok) throw new Error(`Töz model unavailable: ${name}; status=${response.status}`);
        return new Uint8Array(await response.arrayBuffer());
    }));
    // sentencepiece-js supplies CommonJS exports and a synchronous model-reader hook.
    // Provide that hook from already fetched bytes; the supplied tokenizer itself is unchanged.
    self.exports = {};
    self.require = name => {
        if (name === 'fs') return { readFileSync: () => vocabulary };
        if (name === 'url') return { URL };
        throw new Error(`Unsupported tokenizer browser dependency: ${name}`);
    };
    self.__filename = new URL('vendor/toz/sentencepiece.js', configuration.root).pathname;
    importScripts('./vendor/toz/sentencepiece.js');
    processor = new self.exports.SentencePieceProcessor();
    await processor.load('tokenizer.model');
    encoder = await ort.InferenceSession.create(encoderBytes, { executionProviders: ['wasm'] });
    decoder = await ort.InferenceSession.create(decoderBytes, { executionProviders: ['wasm'] });
    beamSize = configuration.beam;
}

async function sentence(text, target) {
    const ids = processor.encodeIds(text);
    if (!ids.length) return '';
    if (ids.length > 254) throw new Error('Töz input exceeds the tokenizer budget');
    const source = [target === 'en' ? 4 : 5, ...ids, 3];
    const cross = await encoder.run({ src: tensor(source, [1, source.length]) });
    let pastK = new ort.Tensor('float32', new Float32Array(0), [6, 1, 8, 0, 64]);
    let pastV = new ort.Tensor('float32', new Float32Array(0), [6, 1, 8, 0, 64]);
    let beams = [{ tokens: [], score: 0, last: 2, parent: 0 }];
    const finished = [];
    const maxTokens = Math.min(2 * ids.length + 10, 254);
    for (let position = 0; position < maxTokens; position++) {
        const result = await decoder.run({
            token: tensor(beams.map(beam => beam.last), [beams.length, 1]),
            pos: tensor([position], [1]), past_k: pastK, past_v: pastV,
            beam_idx: tensor(beams.map(beam => beam.parent), [beams.length]),
            cross_k: cross.cross_k, cross_v: cross.cross_v
        });
        const logits = result.logits.data;
        const top = [], candidateCount = beamSize * 2;
        for (let b = 0; b < beams.length; b++) {
            const base = b * 32000;
            let maximum = -Infinity;
            for (let token = 0; token < 32000; token++) maximum = Math.max(maximum, logits[base + token]);
            let sum = 0;
            for (let token = 0; token < 32000; token++) sum += Math.exp(logits[base + token] - maximum);
            const logSum = maximum + Math.log(sum);
            for (let token = 1; token < 32000; token++) {
                const score = beams[b].score + logits[base + token] - logSum;
                if (top.length === candidateCount && score <= top[candidateCount - 1].score) continue;
                let index = top.findIndex(candidate => score > candidate.score);
                if (index === -1) index = top.length;
                top.splice(index, 0, { b, token, score });
                if (top.length > candidateCount) top.pop();
            }
        }
        const next = [];
        for (const candidate of top) {
            const old = beams[candidate.b];
            if (candidate.token === 3) finished.push({ score: candidate.score / (old.tokens.length + 1), tokens: old.tokens });
            else if (next.length < beamSize) next.push({ tokens: [...old.tokens, candidate.token], last: candidate.token, score: candidate.score, parent: candidate.b });
        }
        if (!next.length || finished.length >= beamSize) break;
        pastK = result.new_k;
        pastV = result.new_v;
        beams = next;
    }
    if (!finished.length) throw new Error('Töz did not complete its translation within the output budget');
    finished.sort((a, b) => b.score - a.score);
    return processor.decodeIds(finished[0].tokens);
}

function splitToBudget(text) {
    if (processor.encodeIds(text).length <= 254) return [text];
    const chunks = [];
    let current = '';
    for (const word of text.split(/(?<=\s)/u)) {
        if (processor.encodeIds(current + word).length <= 254) { current += word; continue; }
        if (current.trim()) chunks.push(current);
        current = '';
        if (processor.encodeIds(word).length <= 254) { current = word; continue; }
        for (const { segment } of new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(word)) {
            if (processor.encodeIds(current + segment).length > 254) {
                if (!current) throw new Error('A text element exceeds the Töz tokenizer budget');
                chunks.push(current); current = '';
            }
            current += segment;
        }
    }
    if (current.trim()) chunks.push(current);
    return chunks;
}

async function translate(text, target) {
    if (!encoder || !decoder || !processor) throw new Error('Töz worker has not initialized');
    const paragraphs = [];
    for (const paragraph of text.split('\n')) {
        const translated = [];
        const sentences = paragraph.trim().split(/(?<=[.!?…])\s+(?=["'“‘(\[]*[A-ZÇĞİÖŞÜ0-9])/u);
        for (const part of sentences) for (const chunk of splitToBudget(part)) translated.push(await sentence(chunk, target));
        paragraphs.push(translated.join(' '));
    }
    return paragraphs.join('\n');
}

self.onmessage = async ({ data: { id, name, args } }) => {
    try {
        let result;
        if (name === 'initialize') result = await initialize(...args);
        else if (name === 'translate') result = await translate(...args);
        else throw new Error(`Unknown Töz worker operation: ${name}`);
        self.postMessage({ id, result });
    } catch (error) {
        self.postMessage({ id, error: { name: error.name, message: error.message, stack: error.stack } });
    }
};
