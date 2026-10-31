---
language:
- en
- tr
tags:
- translation
license: cc-by-4.0
datasets:
- quickmt/quickmt-train.tr-en
model-index:
- name: quickmt-tr-en
  results:
  - task:
      name: Translation tur-eng
      type: translation
      args: tur-eng
    dataset:
      name: flores101-devtest
      type: flores_101
      args: tur_Latn eng_Latn devtest
    metrics:
    - name: BLEU
      type: bleu
      value: 39.5
    - name: CHRF
      type: chrf
      value: 65.03
    - name: COMET
      type: comet
      value: 89.0
---

<a href="https://huggingface.co/spaces/quickmt/quickmt-gui"><img src="https://huggingface.co/datasets/huggingface/badges/resolve/main/open-in-hf-spaces-lg-dark.svg" alt="Open in Spaces"></a>

# `quickmt-tr-en` Neural Machine Translation Model 

`quickmt-tr-en` is a reasonably fast and reasonably accurate neural machine translation model for translation from `tr` into `en`.


## Model Information

* Trained using [`eole`](https://github.com/eole-nlp/eole)
* 195M parameter transformer 'big' with 8 encoder layers and 2 decoder layers
* 50k joint Sentencepiece vocabulary
* Exported for fast inference to [CTranslate2](https://github.com/OpenNMT/CTranslate2) format
* Training data: https://huggingface.co/datasets/quickmt/quickmt-train.tr-en/tree/main

See the `eole` model configuration in this repository for further details and the `eole-model` for the raw `eole` (pytorch) model.

## Usage with `quickmt`

You must install the Nvidia cuda toolkit first, if you want to do GPU inference.

Next, install the `quickmt` [python library](github.com/quickmt/quickmt). 

```bash
git clone https://github.com/quickmt/quickmt.git
pip install ./quickmt/
```

Finally, use the model in python:

```python
from quickmt import Translator
from huggingface_hub import snapshot_download

# Download Model (if not downloaded already) and return path to local model
# Device is either 'auto', 'cpu' or 'cuda'
t = Translator(
    snapshot_download("quickmt/quickmt-tr-en", ignore_patterns="eole-model/*"),
    device="cpu"
)

# Translate - set beam size to 1 for faster speed (but lower quality)
sample_text = "Halifax, Nova Scotia'daki Dalhousie Üniversitesi'nde tıp profesörü ve Kanada Diyabet Derneği'nin klinik ve bilim alanı başkanı olan Dr. Ehud Ur, çalışmanın henüz başlangıç aşamasında olduğu konusunda uyarıda bulundu."

t(sample_text, beam_size=5)
```

> 'Dr. Ehud Ur, a professor of medicine at Dalhousie University in Halifax, Nova Scotia, and head of clinical and science at the Canadian Diabetes Association, warned that the study is still in its infancy.'

```python
# Get alternative translations by sampling
# You can pass any cTranslate2 `translate_batch` arguments
t([sample_text], sampling_temperature=1.2, beam_size=1, sampling_topk=50, sampling_topp=0.9)
```

> 'Dr. Ehud Ur, a professor of medicine at Dalhousie University in Halifax, Nova Scotia, and chief clinical and science officer of the Canadian Diabetes Society, warned that the study is still at its preliminary stage.'

The model is in `ctranslate2` format, and the tokenizers are `sentencepiece`, so you can use `ctranslate2` directly instead of through `quickmt`. It is also possible  to get this model to work with e.g. [LibreTranslate](https://libretranslate.com/) which also uses `ctranslate2` and `sentencepiece`.

## Metrics

`bleu` and `chrf2` are calculated with [sacrebleu](https://github.com/mjpost/sacrebleu) on the [Flores200 `devtest` test set](https://huggingface.co/datasets/facebook/flores) ("tur_Latn"->"eng_Latn"). `comet22` with the [`comet`](https://github.com/Unbabel/COMET) library and the [default model](https://huggingface.co/Unbabel/wmt22-comet-da). "Time (s)" is the time in seconds to translate the flores-devtest dataset (1012 sentences) on an RTX 4070s GPU with batch size 32 (faster speed is possible using a larger batch size).


|                                  |   bleu |   chrf2 |   comet22 |   Time (s) |
|:---------------------------------|-------:|--------:|----------:|-----------:|
| quickmt/quickmt-tr-en            |  39.5  |   65.03 |     89    |       1.28 |
| Helsinki-NLP/opus-mt-tr-en       |  32.19 |   59.43 |     87.03 |       3.5  |
| facebook/nllb-200-distilled-600M |  37.15 |   62.76 |     88.28 |      20.91 |
| facebook/nllb-200-distilled-1.3B |  39.33 |   64.88 |     89.27 |      36.35 |
| facebook/m2m100_418M             |  28.53 |   57.08 |     84.59 |      17.34 |
| facebook/m2m100_1.2B             |  33.1  |   60.38 |     86.82 |      34.1  |
