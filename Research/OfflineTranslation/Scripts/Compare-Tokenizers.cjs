const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const { SentencePieceProcessor } = require(path.join(root, 'Runtimes/Onnx/node_modules/sentencepiece-js'));
async function main() {
  const data = JSON.parse(fs.readFileSync(path.join(root, 'Results/Gpu/Validation/bergamot-tokenizer-csharp.json'), 'utf8'));
  const processors = new Map();
  const comparisons = [];
  for (const item of data) {
    if (!processors.has(item.Pair)) {
      const processor = new SentencePieceProcessor();
      const folder = path.join(root, 'Models/Mozilla', item.Pair);
      await processor.load(path.join(folder, fs.readdirSync(folder).find(name => name.startsWith('vocab.'))));
      processors.set(item.Pair, processor);
    }
    const reference = processors.get(item.Pair).encodeIds(item.Text);
    comparisons.push({ id: item.Id, equal: JSON.stringify(reference) === JSON.stringify(item.Ids), reference, csharp: item.Ids });
  }
  fs.writeFileSync(path.join(root, 'Results/Gpu/Validation/bergamot-tokenizer-comparison.json'), JSON.stringify(comparisons, null, 2));
  console.log(JSON.stringify(comparisons.map(({id, equal}) => ({id, equal}))));
  if (comparisons.some(item => !item.equal)) process.exitCode = 1;
}
main().catch(error => { console.error(error); process.exitCode = 1; });
