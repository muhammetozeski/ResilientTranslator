// Original Misaki/PeterReid fallback decoding, using the published ONNX graph.
class MisakiNeural {
    async load(root) {
        this.config = await speechJson(new URL('misaki/config.json', root));
        this.session = await speechSession(new URL('misaki/fallback.onnx', root));
        this.graphemes = Object.fromEntries(Array.from(this.config.grapheme_chars, (c, i) => [c, i]));
        this.phones = Array.from(this.config.phoneme_chars);
    }
    async word(text) {
        const chars = Array.from(text);
        if (chars.length > 62) {
            const result = [];
            for (let i = 0; i < chars.length; i += 62) result.push(await this.word(chars.slice(i, i + 62).join('')));
            return result.join('');
        }
        const input = speechTensor([1, ...chars.map(c => this.graphemes[c] ?? 3), 2]);
        const decoded = [1];
        try {
            for (let step = 0; step < 63; step++) {
                const decoder = speechTensor(decoded);
                let result;
                try {
                    result = await this.session.run({ input_ids: input, decoder_input_ids: decoder });
                    const logits = result.logits;
                    const size = logits.dims.at(-1), start = logits.data.length - size;
                    let best = 0;
                    for (let i = 1; i < size; i++) if (logits.data[start + i] > logits.data[start + best]) best = i;
                    if (best === 2) return decoded.filter(i => i > 3).map(i => this.phones[i] ?? '').join('');
                    decoded.push(best);
                } finally { decoder.dispose(); if (result) for (const value of Object.values(result)) value.dispose(); }
            }
            throw Error('Misaki neural pronunciation exceeded its output budget');
        } finally { input.dispose(); }
    }
}
