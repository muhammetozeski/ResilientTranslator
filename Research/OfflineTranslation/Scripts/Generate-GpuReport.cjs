const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const destination = path.join(root, 'Results', 'Gpu');
const configurations = [
  ['Toz-CSharp', 'Töz · 8 bit'],
  ['Toz-Float32-CSharp', 'Töz · 32 bit'],
  ['Bergamot-Float32-CSharp', 'Bergamot · 32 bit']
];
const median = values => { const sorted = [...values].sort((a, b) => a - b); return (sorted[4] + sorted[5]) / 2; };
const models = configurations.map(([key, label]) => {
  const read = backend => JSON.parse(fs.readFileSync(path.join(destination, 'Measurements', `${key}-${backend}-Warm.json`), 'utf8'));
  const cpu = read('Cpu'), gpu = read('Gpu');
  return { key, label, cpu, gpu, cpuMedianMs: median(cpu.Rows.map(r => r.ElapsedMs)), gpuMedianMs: median(gpu.Rows.map(r => r.ElapsedMs)), equalOutputs: cpu.Rows.filter((r, i) => r.Output === gpu.Rows[i].Output).length };
});
const summary = {
  date: '2026-10-30', processor: 'Intel Core i5-8300H', graphics: 'NVIDIA GeForce GTX 1050 4 GB',
  driver: '582.66', adapterIndex: 1, implementation: 'C# .NET 10, ONNX Runtime 1.31.1, DirectML',
  protocol: 'The same ten sentences, beam=4, CPU first then GPU. One full first pass followed by the timed second pass. Profiling disabled during timing. CPU uses four threads.',
  models: models.map(({key,label,cpuMedianMs,gpuMedianMs,equalOutputs})=>({key,label,cpuMedianMs,gpuMedianMs,equalOutputs})),
  bergamot: {
    origin: 'Original downloaded Mozilla intgemm model files, both directions; weights dequantized in C# and encoder, SSRU decoder and calibrated activation ranges exported to ONNX.',
    tokenization: 'C# token IDs matched Google SentencePiece for all ten inputs.',
    parity: 'The converted path is experimental. It does not reproduce the original Bergamot runtime in all cases: tr05 lost the negation on both CPU and GPU.',
    sources: ['https://raw.githubusercontent.com/browsermt/marian-dev/master/src/common/binary.cpp', 'https://raw.githubusercontent.com/browsermt/marian-dev/master/src/tensors/cpu/integer_common.h', 'https://raw.githubusercontent.com/browsermt/marian-dev/master/src/models/transformer.h', 'https://raw.githubusercontent.com/browsermt/marian-dev/master/src/rnn/cells.h']
  },
  toz: { q8: 'Original downloaded ONNX files.', float32: 'The same quantized weights dequantized to float32, with dynamic integer MatMul replaced by float MatMul. Activation quantization is removed in this comparison variant.' },
  gpuEvidence: 'Saved ONNX Runtime profiles record DmlExecutionProvider on GTX 1050. The Bergamot probe has only DML node events. The Toz probe also has CPU helper nodes; matrix multiplications are on DML.'
};
fs.writeFileSync(path.join(destination, 'summary.json'), JSON.stringify(summary, null, 2));
const data = JSON.stringify(models).replace(/</g, '\\u003c');
const html = `<!doctype html><html lang="tr"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>C# işlemci / ekran kartı karşılaştırması</title>
<style>:root{color-scheme:dark;font:16px/1.5 system-ui;background:#14181c;color:#e5edf3}body{max-width:1250px;margin:auto;padding:20px}h1{font-size:23px}h2{font-size:17px;margin:0;color:#8ad8af}p{color:#b1c0cb}table{width:100%;border-collapse:collapse;margin:18px 0}td,th{text-align:left;padding:8px;border-bottom:1px solid #3b4955}select,button{font:inherit;padding:7px;background:#25313b;color:inherit;border:1px solid #53636f;border-radius:6px}nav{display:flex;gap:9px;flex-wrap:wrap}#case{flex:1}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:12px}section{padding:15px;border:1px solid #465661;background:#1d252d;border-radius:8px;margin:14px 0}.text{white-space:pre-wrap;overflow-wrap:anywhere;margin-top:10px}.time{float:right;color:#adbdc8}</style>
<h1>C# ile aynı cümleler: işlemci ve GTX 1050</h1><p>Isınma sonrası ikinci geçiş · 10 cümle · 4 çeviri adayı · sürelerde model yükleme ve profil kaydı yok</p>
<table><thead><tr><th>Model</th><th>İşlemci ortanca</th><th>Ekran kartı ortanca</th><th>Aynı çıktı</th></tr></thead><tbody id="summary"></tbody></table>
<nav><select id="model" aria-label="Model"></select><button id="previous">Önceki</button><select id="case" aria-label="Cümle"></select><button id="next">Sonraki</button></nav>
<section><h2>Girdi</h2><div class="text" id="input"></div></section><div class="grid"><section><h2>İşlemci <span id="cpuTime" class="time"></span></h2><div class="text" id="cpu"></div></section><section><h2>GTX 1050 <span id="gpuTime" class="time"></span></h2><div class="text" id="gpu"></div></section></div>
<p>Bergamot'un dönüştürülen sürümünde “Yeni bir istek geldiğinde, önceki çevirinin sonucunu kullanma.” cümlesinin olumsuzluğu her iki aygıtta da kayboldu. Bu deneysel aktarımın çıktıları asıl motorla bütünüyle eşleşmiyor.</p>
<script>const models=${data};const model=document.getElementById('model'),sample=document.getElementById('case');
function option(value,text){const o=document.createElement('option');o.value=value;o.textContent=text;return o}
for(let i=0;i<models.length;i++){model.append(option(i,models[i].label));const tr=document.createElement('tr');for(const text of [models[i].label,models[i].cpuMedianMs.toFixed(1)+' ms',models[i].gpuMedianMs.toFixed(1)+' ms',models[i].equalOutputs+'/10']){const td=document.createElement('td');td.textContent=text;tr.append(td)}document.getElementById('summary').append(tr)}
models[0].cpu.Rows.forEach((row,i)=>sample.append(option(i,row.Id+' · '+row.Source+' → '+row.Target)));
function render(){const data=models[+model.value],index=+sample.value,cpu=data.cpu.Rows[index],gpu=data.gpu.Rows[index];document.getElementById('input').textContent=cpu.Text;document.getElementById('cpu').textContent=cpu.Output;document.getElementById('gpu').textContent=gpu.Output;document.getElementById('cpuTime').textContent=cpu.ElapsedMs.toFixed(1)+' ms';document.getElementById('gpuTime').textContent=gpu.ElapsedMs.toFixed(1)+' ms';document.getElementById('previous').disabled=index===0;document.getElementById('next').disabled=index===9}
model.onchange=render;sample.onchange=render;document.getElementById('previous').onclick=()=>{sample.selectedIndex--;render()};document.getElementById('next').onclick=()=>{sample.selectedIndex++;render()};render();</script></html>`;
fs.writeFileSync(path.join(destination, 'Comparison.html'), html);
console.log(JSON.stringify(summary.models));
