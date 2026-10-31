---
language:
- tr
license: apache-2.0
library_name: transformers
pipeline_tag: text-generation
base_model: ORG/Toz-1.5-Base
tags:
- turkish
- türkçe
- chat
- qwen3
- turkish-nlp
- instruction
- SLM
- tr
---

# Töz-1.5

To run in your browser: toz-chat.onrender.com/sohbet.html

Töz-1.5 is a **604M-parameter Turkish chat model** trained from scratch by Töz AI. It was pre-trained on 15 billion Turkish tokens and then fine-tuned for chat on a new SFT dataset built from the ground up.

- **Developer:** Töz AI (Ahmet Emin Arnak)
- **Language:** Turkish
- **License:** Apache-2.0

![Benchmark results](assets/benchmarks.png)

## Model details

| | |
|---|---|
| Parameters | 604M |
| Architecture | Qwen3-style decoder (`Qwen3ForCausalLM`) |
| Hidden size / layers | 1152 / 32 |
| Attention | GQA: 9 query heads, 3 KV heads, head dim 128, QK-norm |
| FFN | SwiGLU, 4096 |
| Normalization | RMSNorm |
| Positional encoding | RoPE (θ = 10,000) |
| Vocabulary | 32k (tokenizer trained for Turkish) |
| Context length | 1024 tokens |
| Embeddings | Tied input/output, no biases |
| Pre-training | 15B tokens, JAX, TPU v5e-8 |

## Usage

```python
from transformers import AutoTokenizer, AutoModelForCausalLM
import torch

model_id = "TozAI/Toz-1.5"
tok = AutoTokenizer.from_pretrained(model_id)
model = AutoModelForCausalLM.from_pretrained(model_id, dtype=torch.float16)

messages = [{"role": "user", "content": "Fotosentez nedir?"}]
inputs = tok.apply_chat_template(
    messages, add_generation_prompt=True, return_tensors="pt", return_dict=True
)
out = model.generate(**inputs, max_new_tokens=256)
print(tok.decode(out[0][inputs["input_ids"].shape[1]:], skip_special_tokens=True))
```

Chat template:

```
Kullanıcı: {message}
Töz: {response}<|endoftext|>
```

## Tool use: calculator

Töz-1.5 does not compute arithmetic itself. It writes the expression inside `<hesap>…</hesap>` ("hesap" = calculation), an external calculator evaluates it, and the result is inserted inside `<sonuc>…</sonuc>` ("sonuç" = result). The model then continues and writes the final answer:

```
Kullanıcı: 25*4-10 kaç eder peki?
Töz: Hesaplayalım: <hesap>25*4-10</hesap>
<sonuc>90</sonuc>
 Sonuç: 90.
```

How it works:

1. Generate until the model writes `</hesap>`, then stop.
2. Evaluate the expression between `<hesap>` and `</hesap>`.
3. Append `\n<sonuc>{result}</sonuc>\n` to the text and continue generating.

If you skip the tool loop, the model may fill in `<sonuc>` on its own, and that value is not reliable, especially for large numbers. Always run the calculator.

```python
import ast, operator
from fractions import Fraction
import torch
from transformers import AutoTokenizer, AutoModelForCausalLM

model_id = "TozAI/Toz-1.5"
tok = AutoTokenizer.from_pretrained(model_id)
model = AutoModelForCausalLM.from_pretrained(model_id, dtype=torch.float16)  # use torch.float32 on CPU

OPS = {ast.Add: operator.add, ast.Sub: operator.sub, ast.Mult: operator.mul,
       ast.Div: operator.truediv, ast.Mod: operator.mod, ast.Pow: operator.pow,
       ast.USub: operator.neg, ast.UAdd: operator.pos}

def calculate(expr: str) -> str:
    """Safe calculator: numbers, + - * / % ** and parentheses only. No eval()."""
    expr = expr.replace("×", "*").replace("÷", "/").replace("^", "**")
    def ev(n):
        if isinstance(n, ast.Expression):
            return ev(n.body)
        if isinstance(n, ast.Constant) and isinstance(n.value, (int, float)):
            return Fraction(str(n.value))
        if isinstance(n, ast.BinOp) and type(n.op) in OPS:
            a, b = ev(n.left), ev(n.right)
            if isinstance(n.op, ast.Pow):
                if b.denominator != 1 or abs(b) > 1000:
                    raise ValueError("unsupported exponent")
                b = int(b)
            return OPS[type(n.op)](a, b)
        if isinstance(n, ast.UnaryOp) and type(n.op) in OPS:
            return OPS[type(n.op)](ev(n.operand))
        raise ValueError("unsupported expression")
    r = ev(ast.parse(expr, mode="eval"))
    return str(r.numerator) if r.denominator == 1 else f"{float(r):.10g}"

def chat(question: str, max_new_tokens: int = 256, max_calls: int = 5) -> str:
    text = tok.apply_chat_template([{"role": "user", "content": question}],
                                   add_generation_prompt=True, tokenize=False)
    start = len(text)
    for _ in range(max_calls + 1):
        ids = tok(text, return_tensors="pt", add_special_tokens=False)
        out = model.generate(**ids, max_new_tokens=max_new_tokens, do_sample=False,
                             stop_strings=["</hesap>"], tokenizer=tok)
        new = tok.decode(out[0][ids["input_ids"].shape[1]:], skip_special_tokens=True)
        text += new
        if not new.rstrip().endswith("</hesap>"):
            break
        expr = text.rsplit("<hesap>", 1)[1].rsplit("</hesap>", 1)[0]
        try:
            result = calculate(expr)
        except Exception:
            result = "hata"  # "error"
        text += f"\n<sonuc>{result}</sonuc>\n"
    return text[start:].strip()

print(chat("12*7 kaç eder?"))
# Hesaplayalım: <hesap>12*7</hesap>
# <sonuc>84</sonuc>
#  Sonuç: 84.
```

The calculator uses exact integer and fraction arithmetic, so large numbers are not rounded. It never calls `eval()`. Only numbers, the four operations, `%`, `**` and parentheses are accepted.

### Pre-training
Fineweb-2 + wiki filtered, trained 15B token

### Supervised fine-tuning (SFT)
The SFT data for Töz-1.5 was built from scratch, independently of Töz-1's, with a focus on higher quality.

Compared to Töz-1: better at math with tool use, more accurate answers, and fewer hallucinations.

No data under non-commercial (NC) licenses was used.

## Evaluation

The results below are for the **pre-trained model (Töz-1.5-Base)**. The SFT model was not separately evaluated on these benchmarks. All evaluations are 0-shot and use the same script. The other models were re-evaluated with this script.

| Benchmark | Metric | Chance | **Töz-1.5-Base** (604M) | ufakzeka-1-base (182M) | Töz-1-Base (195M) |
|---|---|---|---|---|---|
| HellaSwag-tr | acc_norm | 25% | **40.8%** | 36.8% | 35.0% |
| XCOPA-tr | acc_norm | 50% | **60.6%** | 59.8% | 55.8% |
| TurBLiMP | acc | 50% | **93.6%** | 90.4% | 91.8% |
| Wikitext-2 (English) | BPB ↓ | — | 1.377 | **1.362** | 1.520 |
| Turkish-MMLU | acc | 20% | chance level | chance level | chance level |

Notes:
- On TurBLiMP, Töz models were given a BOS token while the other HF models were not. This may give Töz a small advantage.
- Wikitext-2 is an English benchmark. The Töz models were trained mainly on Turkish and have no advantage there.
- Turkish-MMLU does not separate models at this scale (under 1B). All models score at chance level.

## Limitations

- This is a small model. It can make up facts (hallucinate). Verify its answers on anything important.
- Because the pre-training data is news-heavy, responses can sometimes drift into a news-article style.
- It was trained only for Turkish. It is weak in other languages and at code generation.
- It falls behind larger models on multi-step reasoning and questions that need broad world knowledge.

## Citation

```bibtex
@misc{toz15,
  title  = {Töz-1.5: A 604M-parameter Turkish chat model},
  author = {Arnak, Ahmet Emin},
  year   = {2026},
  url    = {https://huggingface.co/ORG/Toz-1.5}
}
```

## Türkçe özet

Töz-1.5, Töz AI'ın sıfırdan eğittiği 604M parametrelik Türkçe sohbet modelidir. Mimarisi Qwen3 tipindedir. 15 milyar Türkçe token ile ön eğitimden geçti, ardından baştan hazırlanmış yeni bir SFT verisiyle sohbet için ince ayar yapıldı. Yukarıdaki sonuçlar ön eğitimli modele aittir ve 0-shot ölçülmüştür. Töz-1.5-Base, HellaSwag-tr'de %40,8, XCOPA-tr'de %60,6, TurBLiMP'te %93,6 aldı ve üç testte de karşılaştırılan modellerin önünde. Küçük bir model olduğu için bilgi uydurabilir, önemli konularda cevaplarını doğrulayın. Lisansı Apache-2.0'dır.