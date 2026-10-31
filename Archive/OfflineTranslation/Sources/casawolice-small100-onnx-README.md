---
license: mit
library_name: transformers.js
pipeline_tag: translation
base_model: alirezamsh/small100
tags:
  - translation
  - multilingual
  - onnx
  - int8
  - small100
  - m2m100
language:
  - multilingual
  - af
  - am
  - ar
  - ast
  - az
  - ba
  - be
  - bg
  - bn
  - br
  - bs
  - ca
  - ceb
  - cs
  - cy
  - da
  - de
  - el
  - en
  - es
  - et
  - fa
  - ff
  - fi
  - fr
  - fy
  - ga
  - gd
  - gl
  - gu
  - ha
  - he
  - hi
  - hr
  - ht
  - hu
  - hy
  - id
  - ig
  - ilo
  - is
  - it
  - ja
  - jv
  - ka
  - kk
  - km
  - kn
  - ko
  - lb
  - lg
  - ln
  - lo
  - lt
  - lv
  - mg
  - mk
  - ml
  - mn
  - mr
  - ms
  - my
  - ne
  - nl
  - no
  - ns
  - oc
  - or
  - pa
  - pl
  - ps
  - pt
  - ro
  - ru
  - sd
  - si
  - sk
  - sl
  - so
  - sq
  - sr
  - ss
  - su
  - sv
  - sw
  - ta
  - th
  - tl
  - tn
  - tr
  - uk
  - ur
  - uz
  - vi
  - wo
  - xh
  - yi
  - yo
  - zh
  - zu
---

# SMaLL-100 · ONNX (int8)

**[English](#english) | [中文](#中文)**

ONNX export of [**SMaLL-100**](https://huggingface.co/alirezamsh/small100) — a
shallow, distilled multilingual machine-translation model (distilled from
M2M-100), covering **100 languages with direct any-to-any translation** (no
English pivot). This repo packages an **int8-quantized, ~609 MB** deployment set
for on-device / offline use. There is no official SMaLL-100 ONNX on the Hub; this
fills that gap.

## Platforms · 跨平台

One model + one `tokenizer.json` + one `lang_tokens.json`, five runtimes. All follow
the same recipe in **[USAGE.md](USAGE.md)**; runnable/reference code in **[examples/](examples/)**.

| Platform | Runtime + tokenizer | Example |
|---|---|---|
| Python | optimum / onnxruntime + `tokenizers` | [examples/python](examples/python/translate.py) ✅ |
| transformers.js | `@huggingface/transformers` v3 | [examples/transformers-js](examples/transformers-js/translate.mjs) ✅ |
| Flutter | `onnxruntime` (Dart) + bundled pure-Dart BPE tokenizer | [examples/flutter](examples/flutter/small100_translator.dart) ✅ |
| Android | onnxruntime-android + DJL `tokenizers` | [examples/android](examples/android/Small100Translator.kt) 📝 |
| iOS | onnxruntime-swift + swift-transformers | [examples/ios](examples/ios/Small100Translator.swift) 📝 |

✅ = runnable and verified (Flutter: the actual translator class from a shipped
Android/iOS S2S translator app, real devices). 📝 = reference implementation,
algorithm complete but not wired to a build.

`tokenizer.json` is a validated HuggingFace fast tokenizer (encoding matches the
official SMaLL-100 tokenizer exactly); `lang_tokens.json` maps 100 language codes →
token ids. The target-language token is prepended to the **source** (not
`forced_bos_token_id`). 语言 token 加在源句前，各平台一致。

---

## English

### Why SMaLL-100

- **Direct** translation between any of 100 languages (no pivot → single hop).
- **Shallow decoder (3 layers)** → fast: ~4× faster than M2M-100 at similar
  quality; on a laptop CPU greedy decoding is ~150–250 ms/sentence.
- Small enough for mobile: **609 MB int8** vs ~850 MB for NLLB-200-distilled-600M.

### Files — what actually needs what

Only **4 files** are universal; everything else exists for one specific loading
path. If you're integrating on Android/iOS/Flutter, that's all you need —
ignore the rest.

| File | Needed by |
|---|---|
| `onnx/encoder_model.onnx` (274 MB, int8) | 🌐 **every platform** — core weights |
| `onnx/decoder_model_merged.onnx` (307 MB, int8, KV cache) | 🌐 **every platform** — core weights |
| `tokenizer.json` | 🌐 **every platform** — validated HF fast tokenizer |
| `lang_tokens.json` | 🌐 **every platform** — `{lang_to_id, eos, pad, unk, decoder_start}` |
| `config.json`, `generation_config.json` | 🐍 Python `optimum.ORTModelForSeq2SeqLM.from_pretrained` · 🟨 transformers.js `AutoModelForSeq2SeqLM.from_pretrained` |
| `tokenizer_config.json`, `special_tokens_map.json`, `added_tokens.json` | 🐍 Python · 🟨 transformers.js — both via `AutoTokenizer.from_pretrained`'s directory convention |
| `vocab.json`, `sentencepiece.bpe.model`, `tokenization_small100.py` | 🐍 **Python only** — the upstream *slow* `SMALL100Tokenizer` class (`scripts/translate_demo.py`); not used by the fast-tokenizer (`tokenizer.json`) path at all |
| `USAGE.md` | 🌐 docs — platform-agnostic algorithm |
| `examples/` | python · transformers-js · flutter · android · ios |
| `scripts/export.py` | 🐍 dev tooling — reproduce the export + int8 quantization |
| `scripts/translate_demo.py` | 🐍 Python — end-to-end demo (upstream tokenizer path) |

The two `.onnx` files are **int8-quantized** but keep the standard optimum names
(`encoder_model.onnx` / `decoder_model_merged.onnx`) so `from_pretrained` loads
them without extra arguments; this repo ships the quantized weights only.

Model constants: `d_model=1024`, `encoder_layers=12`, `decoder_layers=3`,
`heads=16` (head_dim=64), `vocab=128112`, `decoder_start=eos=2`, `pad=1`, `unk=3`.
The decoder ONNX I/O is the standard optimum merged-decoder signature
(`input_ids`, `encoder_hidden_states`, `encoder_attention_mask`,
`past_key_values.{i}.{decoder,encoder}.{key,value}`, `use_cache_branch` → `logits`,
`present.{i}...`).

### Usage (optimum / onnxruntime, Python)

```bash
pip install optimum[onnxruntime] transformers sentencepiece
python scripts/translate_demo.py
```

```python
from optimum.onnxruntime import ORTModelForSeq2SeqLM
from tokenization_small100 import SMALL100Tokenizer

tok = SMALL100Tokenizer.from_pretrained(".")
model = ORTModelForSeq2SeqLM.from_pretrained(
    ".", subfolder="onnx", use_merged=True, use_io_binding=False)

tok.tgt_lang = "en"                       # target token is prepended to the SOURCE
enc = tok("你好，请问最近的地铁站怎么走？", return_tensors="pt")
out = model.generate(**enc, num_beams=1, max_length=128)
print(tok.batch_decode(out, skip_special_tokens=True)[0])
# -> "Hello, what is the nearest metro station?"
```

**Note (M2M vs SMaLL-100):** SMaLL-100 does **not** use `forced_bos_token_id`.
The target language is selected by setting `tok.tgt_lang`, which prepends the
language token (e.g. `en`→128022, `zh`→128102, `ja`→128046, `ko`→128052) to the
**source** input. Decode is greedy from `decoder_start_token_id=2`.

### Supported languages (100)

Any-to-any: pass any of these ISO-639 codes as the target. Full code→token-id map
in [`lang_tokens.json`](lang_tokens.json).

<details><summary><b>100 languages · click to expand</b></summary>

| | | | |
|---|---|---|---|
| `af` Afrikaans | `am` Amharic | `ar` Arabic | `ast` Asturian |
| `az` Azerbaijani | `ba` Bashkir | `be` Belarusian | `bg` Bulgarian |
| `bn` Bengali | `br` Breton | `bs` Bosnian | `ca` Catalan |
| `ceb` Cebuano | `cs` Czech | `cy` Welsh | `da` Danish |
| `de` German | `el` Greek | `en` English | `es` Spanish |
| `et` Estonian | `fa` Persian | `ff` Fula | `fi` Finnish |
| `fr` French | `fy` Western Frisian | `ga` Irish | `gd` Scottish Gaelic |
| `gl` Galician | `gu` Gujarati | `ha` Hausa | `he` Hebrew |
| `hi` Hindi | `hr` Croatian | `ht` Haitian Creole | `hu` Hungarian |
| `hy` Armenian | `id` Indonesian | `ig` Igbo | `ilo` Iloko |
| `is` Icelandic | `it` Italian | `ja` Japanese | `jv` Javanese |
| `ka` Georgian | `kk` Kazakh | `km` Central Khmer | `kn` Kannada |
| `ko` Korean | `lb` Luxembourgish | `lg` Ganda | `ln` Lingala |
| `lo` Lao | `lt` Lithuanian | `lv` Latvian | `mg` Malagasy |
| `mk` Macedonian | `ml` Malayalam | `mn` Mongolian | `mr` Marathi |
| `ms` Malay | `my` Burmese | `ne` Nepali | `nl` Dutch |
| `no` Norwegian | `ns` Northern Sotho | `oc` Occitan | `or` Oriya |
| `pa` Panjabi | `pl` Polish | `ps` Pashto | `pt` Portuguese |
| `ro` Romanian | `ru` Russian | `sd` Sindhi | `si` Sinhala |
| `sk` Slovak | `sl` Slovenian | `so` Somali | `sq` Albanian |
| `sr` Serbian | `ss` Swati | `su` Sundanese | `sv` Swedish |
| `sw` Swahili | `ta` Tamil | `th` Thai | `tl` Tagalog |
| `tn` Tswana | `tr` Turkish | `uk` Ukrainian | `ur` Urdu |
| `uz` Uzbek | `vi` Vietnamese | `wo` Wolof | `xh` Xhosa |
| `yi` Yiddish | `yo` Yoruba | `zh` Chinese | `zu` Zulu |

</details>

### Benchmarks (int8, laptop CPU arm64, greedy)

| direction | latency |
|---|---|
| zh→en | ~160 ms |
| en→zh | ~130 ms |
| ja→zh | ~170 ms |
| ko→zh | ~140 ms |

Quality is decent for a 330M distilled model; high-resource pairs are good,
some place-name / register slips on harder pairs.

### How it was quantized (the merged-decoder trick)

The merged decoder wraps its cached / non-cached paths in an ONNX `If` node.
`onnxruntime.quantization.quantize_dynamic` skips subgraphs by default, leaving
the decoder unquantized (~1.26 GB). The fix:

```python
quantize_dynamic(src, dst, weight_type=QuantType.QInt8,
                 extra_options={"EnableSubgraph": True})
```

This quantizes the MatMuls inside the `If` branches → merged decoder ~322 MB,
keeping the KV cache (so generation stays fast). See `scripts/export.py`.

### Tokenizer

`tokenizer.json` is a validated HuggingFace **fast** tokenizer whose encoding
matches the official SMaLL-100 tokenizer **exactly** (verified across zh/en/ja/ko).
It loads in `tokenizers` (Rust), transformers.js, DJL (Android) and
swift-transformers (iOS) — one tokenizer for every platform. It was rebuilt from
`Xenova/m2m100_418M` (same 128112 vocab) by dropping 1053 merges that referenced
out-of-vocab pieces (which made newer `tokenizers` reject the file), and by
setting the post-processor to append `</s>` only. The language token is **not**
baked in — prepend `lang_tokens.json[tgt]` in app code (see USAGE.md).
`sentencepiece.bpe.model` + `tokenization_small100.py` remain for the upstream
Python path.

### License

SMaLL-100 is released under the **MIT license** (see the upstream model card).
This repo redistributes the ONNX-converted weights under the same terms.

Upstream: SMaLL-100 (Mohammadshahi et al., EMNLP 2022),
[paper](https://arxiv.org/abs/2210.11621) ·
[model](https://huggingface.co/alirezamsh/small100).

---

## 中文

[**SMaLL-100**](https://huggingface.co/alirezamsh/small100) 的 ONNX 导出版——一个
浅层、蒸馏的多语言机器翻译模型（从 M2M-100 蒸馏而来），覆盖 **100 种语言、任意语向
直译**（无需经英语中转）。本仓库打包了 **int8 量化、约 609 MB** 的部署集，供端上 /
离线使用。目前 Hub 上没有官方的 SMaLL-100 ONNX，本仓库填补这一空缺。

### 为什么选 SMaLL-100

- **任意 100 种语言之间直译**（无中转 → 单跳）。
- **浅层解码器（3 层）** → 快：质量相近下比 M2M-100 快约 4×；笔记本 CPU 上贪心解码
  约 150–250 ms/句。
- 体积适合移动端：**int8 609 MB**，相比之下 NLLB-200-distilled-600M 约 850 MB。

### 文件说明——各文件到底谁在用

真正**跨平台通用的只有 4 个文件**；其余的都只服务于某一条特定的加载路径。如果你在
接入 Android/iOS/Flutter，只需要这 4 个，其余可以完全忽略。

| 文件 | 谁在用 |
|---|---|
| `onnx/encoder_model.onnx`（274 MB, int8） | 🌐 **所有平台** —— 核心权重 |
| `onnx/decoder_model_merged.onnx`（307 MB, int8，带 KV 缓存） | 🌐 **所有平台** —— 核心权重 |
| `tokenizer.json` | 🌐 **所有平台** —— 经验证的 HF fast 分词器 |
| `lang_tokens.json` | 🌐 **所有平台** —— `{lang_to_id, eos, pad, unk, decoder_start}` |
| `config.json`、`generation_config.json` | 🐍 Python `optimum.ORTModelForSeq2SeqLM.from_pretrained` · 🟨 transformers.js `AutoModelForSeq2SeqLM.from_pretrained` |
| `tokenizer_config.json`、`special_tokens_map.json`、`added_tokens.json` | 🐍 Python · 🟨 transformers.js —— 都是通过 `AutoTokenizer.from_pretrained` 的目录约定读取 |
| `vocab.json`、`sentencepiece.bpe.model`、`tokenization_small100.py` | 🐍 **仅 Python** —— 上游"慢速" `SMALL100Tokenizer` 类专用（`scripts/translate_demo.py`）；fast 分词器（`tokenizer.json`）路径完全用不到 |
| `USAGE.md` | 🌐 文档 —— 与平台无关的算法说明 |
| `examples/` | python · transformers-js · flutter · android · ios |
| `scripts/export.py` | 🐍 开发工具 —— 复现导出 + int8 量化 |
| `scripts/translate_demo.py` | 🐍 Python —— 端到端示例（上游分词器路径） |

两个 `.onnx` 文件是 **int8 量化**的，但保留 optimum 标准命名
（`encoder_model.onnx` / `decoder_model_merged.onnx`），以便 `from_pretrained`
无需额外参数即可加载；本仓库只提供量化后的权重。

模型常量：`d_model=1024`、`encoder_layers=12`、`decoder_layers=3`、`heads=16`
（head_dim=64）、`vocab=128112`、`decoder_start=eos=2`、`pad=1`、`unk=3`。decoder
的 ONNX 输入输出是 optimum 标准合并解码器签名（`input_ids`、
`encoder_hidden_states`、`encoder_attention_mask`、
`past_key_values.{i}.{decoder,encoder}.{key,value}`、`use_cache_branch` →
`logits`、`present.{i}...`）。

### 用法（optimum / onnxruntime，Python）

```bash
pip install optimum[onnxruntime] transformers sentencepiece
python scripts/translate_demo.py
```

```python
from optimum.onnxruntime import ORTModelForSeq2SeqLM
from tokenization_small100 import SMALL100Tokenizer

tok = SMALL100Tokenizer.from_pretrained(".")
model = ORTModelForSeq2SeqLM.from_pretrained(
    ".", subfolder="onnx", use_merged=True, use_io_binding=False)

tok.tgt_lang = "en"                       # 目标语言 token 会被加到「源句」前
enc = tok("你好，请问最近的地铁站怎么走？", return_tensors="pt")
out = model.generate(**enc, num_beams=1, max_length=128)
print(tok.batch_decode(out, skip_special_tokens=True)[0])
# -> "Hello, what is the nearest metro station?"
```

**注意（M2M 与 SMaLL-100 的区别）**：SMaLL-100 **不使用** `forced_bos_token_id`。
目标语言通过设置 `tok.tgt_lang` 选择，它会把语言 token（如 `en`→128022、
`zh`→128102、`ja`→128046、`ko`→128052）加到**源句**前面。解码从
`decoder_start_token_id=2` 开始贪心生成。

### 支持的语言（100 种）

任意语向互译：把下列任一 ISO-639 code 作为目标语言传入。完整的 code→token id 映射见
[`lang_tokens.json`](lang_tokens.json)。

<details><summary><b>100 种语言 · 点击展开</b></summary>

| | | | |
|---|---|---|---|
| `af` 南非荷兰语 | `am` 阿姆哈拉语 | `ar` 阿拉伯语 | `ast` 阿斯图里亚斯语 |
| `az` 阿塞拜疆语 | `ba` 巴什基尔语 | `be` 白俄罗斯语 | `bg` 保加利亚语 |
| `bn` 孟加拉语 | `br` 布列塔尼语 | `bs` 波斯尼亚语 | `ca` 加泰罗尼亚语 |
| `ceb` 宿务语 | `cs` 捷克语 | `cy` 威尔士语 | `da` 丹麦语 |
| `de` 德语 | `el` 希腊语 | `en` 英语 | `es` 西班牙语 |
| `et` 爱沙尼亚语 | `fa` 波斯语 | `ff` 富拉语 | `fi` 芬兰语 |
| `fr` 法语 | `fy` 西弗里斯语 | `ga` 爱尔兰语 | `gd` 苏格兰盖尔语 |
| `gl` 加利西亚语 | `gu` 古吉拉特语 | `ha` 豪萨语 | `he` 希伯来语 |
| `hi` 印地语 | `hr` 克罗地亚语 | `ht` 海地克里奥尔语 | `hu` 匈牙利语 |
| `hy` 亚美尼亚语 | `id` 印度尼西亚语 | `ig` 伊博语 | `ilo` 伊洛卡诺语 |
| `is` 冰岛语 | `it` 意大利语 | `ja` 日语 | `jv` 爪哇语 |
| `ka` 格鲁吉亚语 | `kk` 哈萨克语 | `km` 高棉语 | `kn` 卡纳达语 |
| `ko` 韩语 | `lb` 卢森堡语 | `lg` 卢干达语 | `ln` 林加拉语 |
| `lo` 老挝语 | `lt` 立陶宛语 | `lv` 拉脱维亚语 | `mg` 马尔加什语 |
| `mk` 马其顿语 | `ml` 马拉雅拉姆语 | `mn` 蒙古语 | `mr` 马拉地语 |
| `ms` 马来语 | `my` 缅甸语 | `ne` 尼泊尔语 | `nl` 荷兰语 |
| `no` 挪威语 | `ns` 北索托语 | `oc` 奥克语 | `or` 奥里亚语 |
| `pa` 旁遮普语 | `pl` 波兰语 | `ps` 普什图语 | `pt` 葡萄牙语 |
| `ro` 罗马尼亚语 | `ru` 俄语 | `sd` 信德语 | `si` 僧伽罗语 |
| `sk` 斯洛伐克语 | `sl` 斯洛文尼亚语 | `so` 索马里语 | `sq` 阿尔巴尼亚语 |
| `sr` 塞尔维亚语 | `ss` 斯瓦蒂语 | `su` 巽他语 | `sv` 瑞典语 |
| `sw` 斯瓦希里语 | `ta` 泰米尔语 | `th` 泰语 | `tl` 他加禄语 |
| `tn` 茨瓦纳语 | `tr` 土耳其语 | `uk` 乌克兰语 | `ur` 乌尔都语 |
| `uz` 乌兹别克语 | `vi` 越南语 | `wo` 沃洛夫语 | `xh` 科萨语 |
| `yi` 意第绪语 | `yo` 约鲁巴语 | `zh` 中文 | `zu` 祖鲁语 |

</details>

### 基准（int8，笔记本 CPU arm64，贪心解码）

| 语向 | 延迟 |
|---|---|
| 中→英 | ~160 ms |
| 英→中 | ~130 ms |
| 日→中 | ~170 ms |
| 韩→中 | ~140 ms |

作为 330M 蒸馏模型质量尚可；高资源语对不错，较难的语对偶有地名 / 语气偏差。

### 量化方法（合并 decoder 的关键技巧）

合并 decoder 把「带缓存 / 不带缓存」两条路径包在一个 ONNX `If` 节点里。
`onnxruntime.quantization.quantize_dynamic` 默认不进子图，会导致 decoder 未被量化
（约 1.26 GB）。解决办法：

```python
quantize_dynamic(src, dst, weight_type=QuantType.QInt8,
                 extra_options={"EnableSubgraph": True})
```

这会量化 `If` 分支内部的 MatMul → 合并 decoder 约 322 MB，且保留 KV 缓存（生成依旧
很快）。见 `scripts/export.py`。

### 分词器

`tokenizer.json` 是一份经过验证的 HuggingFace **fast** 分词器，编码结果与官方
SMaLL-100 分词器**完全一致**（中英日韩均已核对）。它能在 `tokenizers`(Rust)、
transformers.js、DJL(Android)、swift-transformers(iOS) 中加载——**一份分词器通吃四端**。
它基于 `Xenova/m2m100_418M`（同 128112 词表）重建：剔除了 1053 个引用越界 piece 的
merge（正是它们导致新版 `tokenizers` 拒绝加载），并把 post-processor 改为只追加
`</s>`。语言 token **不**写死在分词器里——由应用层前缀 `lang_tokens.json[tgt]`
（见 USAGE.md）。`sentencepiece.bpe.model` + `tokenization_small100.py` 保留作为
上游 Python 路径。

### 许可协议

SMaLL-100 以 **MIT 协议**发布（见上游模型卡）。本仓库以相同条款再分发 ONNX 转换后的
权重。

上游：SMaLL-100（Mohammadshahi 等，EMNLP 2022），
[论文](https://arxiv.org/abs/2210.11621) ·
[模型](https://huggingface.co/alirezamsh/small100)。
