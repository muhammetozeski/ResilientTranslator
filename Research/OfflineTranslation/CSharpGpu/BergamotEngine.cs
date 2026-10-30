using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

internal sealed class BergamotEngine : ITranslationEngine
{
    private readonly Dictionary<string, Pair> pairs = [];
    private readonly string root, name;
    private readonly bool gpu, strict, profiling;
    private readonly int adapter;

    public BergamotEngine(string root, bool gpu, bool strict, int adapter, string name, bool profiling)
    {
        this.root = root; this.gpu = gpu; this.strict = strict; this.adapter = adapter; this.name = name; this.profiling = profiling;
    }

    public string Translate(string text, string target)
    {
        string key = target == "en" ? "tr-en" : "en-tr";
        if (!pairs.TryGetValue(key, out var pair))
        {
            pair = new Pair(root, key, gpu, strict, adapter, name, profiling);
            pairs.Add(key, pair);
        }
        return string.Join('\n', text.Split('\n').Select(paragraph => string.Join(' ',
            Regex.Split(paragraph.Trim(), "(?<=[.!?…])\\s+(?=[\"'“‘(\\[]*[A-ZÇĞİÖŞÜ0-9])").Select(pair.Translate))));
    }

    public void SaveProfiles() { foreach (var pair in pairs.Values) pair.SaveProfiles(); }
    public void Dispose() { foreach (var pair in pairs.Values) pair.Dispose(); }

    private sealed class Pair : IDisposable
    {
        private readonly InferenceSession encoder, decoder;
        private readonly SentencePieceTokenizer tokenizer;
        private readonly bool profiling;
        private bool saved;

        public Pair(string root, string pair, bool gpu, bool strict, int adapter, string name, bool profiling)
        {
            this.profiling = profiling;
            string source = Path.Combine(root, "Models", "Mozilla", pair);
            tokenizer = BergamotVocabulary.Load(Directory.GetFiles(source, "vocab.*.spm").Single());
            string model = Path.Combine(root, "Models", "Mozilla", "Onnx", pair);
            using var options = new SessionOptions
            {
                IntraOpNumThreads = 4, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, EnableMemoryPattern = !gpu,
                ProfileOutputPathPrefix = Path.Combine(root, "Results", "Gpu", "Profiles", name + "-" + pair), EnableProfiling = profiling
            };
            if (gpu)
            {
                options.AppendExecutionProvider_DML(adapter);
                if (strict) options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
            }
            Program.Log($"Loading {name} {pair}");
            encoder = new InferenceSession(Path.Combine(model, "encoder.onnx"), options);
            options.EnableProfiling = false;
            options.ProfileOutputPathPrefix = Path.Combine(root, "Results", "Gpu", "Profiles", name + "-" + pair + "-decoder");
            options.EnableProfiling = profiling;
            decoder = new InferenceSession(Path.Combine(model, "decoder.onnx"), options);
            Program.Log($"Loaded {name} {pair}");
        }

        public string Translate(string text)
        {
            int[] ids = tokenizer.EncodeToIds(text).Take(254).ToArray();
            if (ids.Length == 0) return "";
            long[] source = [.. ids.Select(x => (long)x), 0];
            using var cross = encoder.Run([
                LongInput("src", source, [1, source.Length]),
                NamedOnnxValue.CreateFromTensor("positions", Positions(source.Length, 1, 0, source: true))]);
            var keys = cross.First(x => x.Name == "cross_k").AsTensor<float>();
            var values = cross.First(x => x.Name == "cross_v").AsTensor<float>();
            Tensor<float> past = new DenseTensor<float>([2, 1, 1, 256]);
            var beams = new List<Beam> { new([], 0, 0, 0) };
            var finished = new List<(double Score, int[] Tokens)>();
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? retained = null;
            try
            {
                for (int position = 0; position < Math.Min(ids.Length * 2 + 10, 254); position++)
                {
                    var result = decoder.Run([
                        LongInput("token", beams.Select(b => (long)b.Last).ToArray(), [beams.Count, 1]),
                        NamedOnnxValue.CreateFromTensor("positions", Positions(1, beams.Count, position, source: false)),
                        NamedOnnxValue.CreateFromTensor("embedding_mask", new DenseTensor<float>(new[] { position == 0 ? 0f : 1f }, new[] { 1 })),
                        NamedOnnxValue.CreateFromTensor("past", past),
                        LongInput("beam_idx", beams.Select(b => (long)b.Parent).ToArray(), [beams.Count]),
                        NamedOnnxValue.CreateFromTensor("cross_k", keys),
                        NamedOnnxValue.CreateFromTensor("cross_v", values)]);
                    retained?.Dispose();
                    retained = result;
                    float[] logits = result.First(v => v.Name == "logits").AsTensor<float>().ToArray();
                    var top = new List<Candidate>(9);
                    for (int b = 0; b < beams.Count; b++)
                    {
                        int offset = b * 32000;
                        float maximum = float.NegativeInfinity;
                        for (int token = 0; token < 32000; token++) maximum = Math.Max(maximum, logits[offset + token]);
                        double sum = 0;
                        for (int token = 0; token < 32000; token++) sum += Math.Exp(logits[offset + token] - maximum);
                        double logSum = maximum + Math.Log(sum);
                        for (int token = 0; token < 32000; token++)
                        {
                            double score = beams[b].Score + logits[offset + token] - logSum;
                            if (top.Count == 8 && score <= top[7].Score) continue;
                            int index = top.FindIndex(c => score > c.Score);
                            if (index < 0) index = top.Count;
                            top.Insert(index, new(b, token, score));
                            if (top.Count > 8) top.RemoveAt(8);
                        }
                    }
                    var next = new List<Beam>(4);
                    foreach (var candidate in top)
                    {
                        var old = beams[candidate.Beam];
                        if (candidate.Token == 0) finished.Add((candidate.Score / (old.Tokens.Length + 1), old.Tokens));
                        else if (next.Count < 4) next.Add(new([.. old.Tokens, candidate.Token], candidate.Score, candidate.Token, candidate.Beam));
                    }
                    if (next.Count == 0 || finished.Count >= 4) break;
                    past = result.First(v => v.Name == "new_past").AsTensor<float>();
                    beams = next;
                }
                if (finished.Count == 0) finished.Add((0, beams[0].Tokens));
                return tokenizer.Decode(finished.MaxBy(x => x.Score).Tokens);
            }
            finally { retained?.Dispose(); }
        }

        private static DenseTensor<float> Positions(int steps, int batch, int start, bool source)
        {
            var tensor = new DenseTensor<float>([batch, steps, 256]);
            for (int b = 0; b < batch; b++)
                for (int step = 0; step < steps; step++)
                    for (int i = 0; i < 128; i++)
                    {
                        float angle = (start + step) * MathF.Pow(1e-4f, i / 127f);
                        tensor[b, step, i] = MathF.Sin(angle);
                        tensor[b, step, i + 128] = MathF.Cos(angle);
                    }
            return tensor;
        }

        private static NamedOnnxValue LongInput(string name, long[] data, int[] dimensions)
            => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(data, dimensions));
        public void SaveProfiles()
        {
            if (!profiling || saved) return;
            saved = true;
            Program.Log("Encoder profile: " + encoder.EndProfiling());
            Program.Log("Decoder profile: " + decoder.EndProfiling());
        }
        public void Dispose() { try { SaveProfiles(); } finally { encoder.Dispose(); decoder.Dispose(); } }
        private sealed record Beam(int[] Tokens, double Score, int Last, int Parent);
        private sealed record Candidate(int Beam, int Token, double Score);
    }
}
