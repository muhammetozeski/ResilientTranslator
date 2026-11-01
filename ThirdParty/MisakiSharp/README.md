# English-only MisakiSharp

Source: https://github.com/Lyrcaxis/MisakiSharp, commit beb91a9f06ebe5e25e595b5a3c990ee616ad75e5. Only the four English C# files and American lexicon/tokenizer/tagger resources are included. Apache-2.0 and upstream NOTICE are retained. The upstream default eSpeak-replay constructor is replaced with a required injected fallback; pronunciation algorithms are unchanged. The application supplies Misaki's original neural fallback through its existing ONNX Web runtime. No Python, native eSpeak or unrelated language resources are distributed.
