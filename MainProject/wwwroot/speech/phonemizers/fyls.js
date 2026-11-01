// Port of the published FYLs-G2P normalization, tag-distance and ONNX inference flow.
const fylsTags = ['<PAD>', 'VERB', 'VBP', 'VBN', 'VBD', 'ADJ', 'ADV', 'DT', 'NOUN', 'DEFAULT', 'NONE'];
const fylsPunctuation = ';:,.!?—…"()“”';
function fylsTokens(text) {
    const clean = text.toLowerCase().replace(/[^a-z0-9;:,.!?—…"()“”\s]/g, '');
    return clean.match(/[a-z0-9]+(?:[;:,.!?—…"()“”][a-z0-9]+)+|[a-z0-9]+|[;:,.!?—…"()“”]/g) ?? [];
}
function fylsNumber(text) {
    const ones = ['', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen'];
    const tens = ['', '', 'twenty', 'thirty', 'forty', 'fifty', 'sixty', 'seventy', 'eighty', 'ninety'];
    const scales = ['', 'thousand', 'million', 'billion', 'trillion'];
    const digits = value => Array.from(value, c => c === '0' ? 'zero' : ones[Number(c)]).join(' ');
    const [whole, decimal] = text.split('.');
    let n = BigInt(whole || '0'), groups = [], scale = 0;
    if (n >= 1000000000000000n) groups = [digits(whole)];
    else while (n > 0n) {
        const value = Number(n % 1000n);
        if (value) {
            const words = [], hundreds = Math.floor(value / 100), remainder = value % 100;
            if (hundreds) { words.push(ones[hundreds], 'hundred'); if (remainder) words.push('and'); }
            if (remainder < 20) { if (remainder) words.push(ones[remainder]); }
            else { words.push(tens[Math.floor(remainder / 10)]); if (remainder % 10) words.push(ones[remainder % 10]); }
            if (scales[scale]) words.push(scales[scale]);
            if (scale === 0 && value < 100 && n >= 1000n) words.unshift('and');
            groups.unshift(words.join(' '));
        }
        n /= 1000n; scale++;
    }
    const result = groups.length ? groups.join(' ') : 'zero';
    return decimal ? `${result} point ${digits(decimal)}` : result;
}
class FylsPronunciation {
    async load(root) {
        const files = ['us_gold_m.json', 'word2idx.json', 'char2idx.json', 'idx2tag.json', 'idx2ipa.json'];
        [this.lexicon, this.words, this.chars, this.tags, this.ipa] = await Promise.all(files.map(name => speechJson(new URL(`fyls/${name}`, root))));
        this.tagger = await speechSession(new URL('fyls/etagger.onnx', root));
        this.fallback = await speechSession(new URL('fyls/etoddler.onnx', root));
        const n = fylsTags.length;
        this.distances = Array.from({ length: n }, (_, i) => Array.from({ length: n }, (_, j) => i === j ? 0 : Infinity));
        const similarities = [['NOUN','VERB',.25],['NOUN','ADJ',.75],['NOUN','DEFAULT',.35],['VERB','VBN',1],['VERB','VBP',1],['VERB','VBD',1],['VERB','ADJ',.25],['VERB','ADV',.5],['VERB','DEFAULT',.35],['VBN','VBP',.75],['VBN','VBD',1.25],['VBN','ADJ',.75],['VBN','DEFAULT',.35],['VBP','VBD',.75],['VBP','DEFAULT',.35],['VBD','DEFAULT',.35],['ADJ','ADV',.5],['ADJ','DT',.75],['ADJ','DEFAULT',.35],['ADV','DT',.25],['ADV','DEFAULT',.35],['DEFAULT','DT',.35],['DEFAULT','NONE',.5]];
        for (const [a,b,similarity] of similarities) {
            const i = fylsTags.indexOf(a), j = fylsTags.indexOf(b);
            this.distances[i][j] = this.distances[j][i] = 1 / similarity;
        }
        for (let k=0;k<n;k++) for(let i=0;i<n;i++) for(let j=0;j<n;j++) this.distances[i][j]=Math.min(this.distances[i][j],this.distances[i][k]+this.distances[k][j]);
    }
    choose(predicted, entries) {
        if (Object.hasOwn(entries, predicted)) return predicted;
        const from = fylsTags.indexOf(predicted.toUpperCase());
        let best = Object.keys(entries)[0], distance = Infinity;
        for (const tag of Object.keys(entries)) {
            const to = fylsTags.indexOf(tag.toUpperCase());
            if (from >= 0 && to >= 0 && this.distances[from][to] < distance) { distance = this.distances[from][to]; best = tag; }
        }
        return best;
    }
    async unknown(word) {
        const chars = Array.from(word);
        if(chars.length>118) {
            const parts=[];
            for(let i=0;i<chars.length;i+=118) parts.push(await this.unknown(chars.slice(i,i+118).join('')));
            return parts.join('');
        }
        const values=[this.chars['<SOS>'],...chars.map(c=>this.chars[c]??this.chars['-']),this.chars['<EOS>']];
        while(values.length<750)values.push(0);
        const input=speechTensor(values);let result;
        try {
            result=await this.fallback.run({[this.fallback.inputNames[0]]:input});
            const tokens=result[this.fallback.outputNames[0]].data;
            let output='';
            for(const value of tokens){const id=Number(value);if(id===2)break;if(id>2)output+=this.ipa[id]??'';}
            if(!output)throw Error(`FYLs returned no pronunciation for ${word}`);
            return output;
        } finally { input.dispose();if(result)for(const value of Object.values(result))value.dispose(); }
    }
    async sentence(text) {
        const words=fylsTokens(text).flatMap(word=>/^\d+(?:\.\d+)?$/.test(word)?fylsTokens(fylsNumber(word)): [word]);
        const parts=[];
        for(let base=0;base<words.length;base+=750) {
            const batch=words.slice(base,base+750),values=batch.map(word=>this.words[word]??1);
            while(values.length<750)values.push(0);
            const input=speechTensor(values);let result;
            try {
                result=await this.tagger.run({[this.tagger.inputNames[0]]:input});
                const tensor=result[this.tagger.outputNames[0]],classes=tensor.dims.at(-1),logits=tensor.data;
                for(let i=0;i<batch.length;i++) {
                    const word=batch[i],entry=this.lexicon[word];
                    if(typeof entry==='string'){parts.push(entry);continue;}
                    if(entry){let tag=0;for(let j=1;j<classes;j++)if(logits[i*classes+j]>logits[i*classes+tag])tag=j;parts.push(entry[this.choose(this.tags[tag],entry)]);}
                    else parts.push(fylsPunctuation.includes(word)?word:await this.unknown(word));
                }
            } finally {input.dispose();if(result)for(const value of Object.values(result))value.dispose();}
        }
        return parts.join(' ');
    }
}
