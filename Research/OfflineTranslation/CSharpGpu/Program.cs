using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Windows.Win32;
using Windows.Win32.Graphics.Dxgi;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Path.Combine(Root, "Results", "Gpu"));
        foreach (string group in new[] { "Measurements", "Profiles", "Diagnostics", "Validation" })
            Directory.CreateDirectory(Path.Combine(Root, "Results", "Gpu", group));
        try
        {
            if (args.Contains("--convert-bergamot"))
            {
                BergamotConverter.ConvertPair(Root, "tr-en");
                BergamotConverter.ConvertPair(Root, "en-tr");
                return 0;
            }
            if (args.Contains("--convert-toz"))
            {
                string folder = Path.Combine(Root, "Models", "Toz");
                OnnxFloatConverter.Convert(Path.Combine(folder, "encoder_q8.onnx"), Path.Combine(folder, "Float32", "encoder.onnx"));
                OnnxFloatConverter.Convert(Path.Combine(folder, "decoder_q8.onnx"), Path.Combine(folder, "Float32", "decoder.onnx"));
                return 0;
            }
            var adapters = FindAdapters();
            Log(JsonSerializer.Serialize(adapters, JsonOptions));
            File.WriteAllText(Path.Combine(Root, "Sources", "Gpu", "adapters.json"), JsonSerializer.Serialize(adapters, JsonOptions));
            if (args.Contains("--adapters")) return 0;
            bool gpu = args.Contains("--gpu");
            bool strict = args.Contains("--strict");
            bool warm = args.Contains("--warm");
            bool bergamot = args.Contains("--bergamot");
            bool tozFloat = args.Contains("--toz-float");
            var selected = adapters.First(a => a.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
            var cases = JsonSerializer.Deserialize<SentenceCase[]>(File.ReadAllText(Path.Combine(Root, "Corpus", "toz-bergamot-cases.json")), JsonOptions)!;
            if (args.Contains("--tokenizer-check"))
            {
                var data = cases.Select(item =>
                {
                    string pair = item.Source == "tr" ? "tr-en" : "en-tr";
                    var tokenizer = BergamotVocabulary.Load(Directory.GetFiles(Path.Combine(Root, "Models", "Mozilla", pair), "vocab.*.spm").Single());
                    return new { item.Id, item.Text, Pair = pair, Ids = tokenizer.EncodeToIds(item.Text) };
                });
                File.WriteAllText(Path.Combine(Root, "Results", "Gpu", "Validation", "bergamot-tokenizer-csharp.json"), JsonSerializer.Serialize(data, JsonOptions));
                return 0;
            }
            string name = gpu ? strict ? "Toz-CSharp-GpuStrict" : "Toz-CSharp-Gpu" : "Toz-CSharp-Cpu";
            if (warm) name += "-Warm";
            if (bergamot) name = name.Replace("Toz", "Bergamot-Float32");
            else if (tozFloat) name = name.Replace("Toz", "Toz-Float32");
            var rows = new List<object>();
            var firstPass = new List<object>();
            using ITranslationEngine engine = bergamot
                ? new BergamotEngine(Root, gpu, strict, selected.Index, name, args.Contains("--probe"))
                : new TozEngine(Root, gpu, strict, selected.Index, name, args.Contains("--probe"), tozFloat);
            if (args.Contains("--probe"))
            {
                Log(engine.Translate(cases[0].Text, cases[0].Target));
                engine.SaveProfiles();
                return 0;
            }
            if (warm)
            {
                foreach (var item in cases)
                {
                    var watch = Stopwatch.StartNew();
                    string output = engine.Translate(item.Text, item.Target);
                    watch.Stop();
                    firstPass.Add(new { item.Id, item.Text, Output = output, ElapsedMs = watch.Elapsed.TotalMilliseconds });
                    Log($"First pass {name} {item.Id}: {watch.Elapsed.TotalMilliseconds:F1} ms | {output}");
                }
            }
            foreach (var item in cases)
            {
                var watch = Stopwatch.StartNew();
                string output = engine.Translate(item.Text, item.Target);
                watch.Stop();
                rows.Add(new { item.Id, item.Source, item.Target, item.Text, Output = output, ElapsedMs = watch.Elapsed.TotalMilliseconds });
                var report = new { Model = bergamot ? "Bergamot original quantized weights dequantized to float32 ONNX" : tozFloat ? "Toz q8 weights dequantized to float32 MatMul" : "Toz-Cevir-1 q8", Backend = gpu ? "DirectML" : "CPU", Adapter = gpu ? selected.Name : null, Beam = 4, FirstPass = firstPass, Rows = rows };
                File.WriteAllText(Path.Combine(Root, "Results", "Gpu", "Measurements", name + ".json"), JsonSerializer.Serialize(report, JsonOptions));
                Log($"{name} {item.Id}: {watch.Elapsed.TotalMilliseconds:F1} ms | {output}");
            }
            engine.SaveProfiles();
            return 0;
        }
        catch (Exception error)
        {
            Log(error.ToString());
            File.WriteAllText(Path.Combine(Root, "Results", "Gpu", "Diagnostics", "error-" + (args.Contains("--gpu") ? "gpu" : "cpu") + ".txt"), error.ToString());
            return 1;
        }
    }

    private static unsafe List<AdapterInfo> FindAdapters()
    {
        Guid factoryId = typeof(IDXGIFactory1).GUID;
        PInvoke.CreateDXGIFactory1(&factoryId, out object raw).ThrowOnFailure();
        var factory = (IDXGIFactory1)raw;
        var adapters = new List<AdapterInfo>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var result = factory.EnumAdapters1(index, out IDXGIAdapter1 adapter);
                if (result == Windows.Win32.Foundation.HRESULT.DXGI_ERROR_NOT_FOUND) break;
                result.ThrowOnFailure();
                try
                {
                    adapter.GetDesc1(out var description).ThrowOnFailure();
                    adapters.Add(new((int)index, description.Description.ToString(), (ulong)description.DedicatedVideoMemory));
                }
                finally { Marshal.ReleaseComObject(adapter); }
            }
        }
        finally { Marshal.ReleaseComObject(factory); }
        return adapters;
    }

    internal static void Log(string text) => Console.WriteLine(text);
    private sealed record AdapterInfo(int Index, string Name, ulong DedicatedMemory);
    private sealed record SentenceCase(string Id, string Source, string Target, string Category, string Text);
}

internal interface ITranslationEngine : IDisposable
{
    string Translate(string text, string target);
    void SaveProfiles();
}

internal sealed class TozEngine : ITranslationEngine
{
    private readonly InferenceSession encoder;
    private readonly InferenceSession decoder;
    private readonly SentencePieceTokenizer tokenizer;
    private readonly string resultDirectory;
    private readonly string name;
    private readonly bool profiling;
    private bool profilesSaved;

    public TozEngine(string root, bool gpu, bool strict, int adapterIndex, string name, bool profiling, bool floatModel = false)
    {
        this.name = name;
        this.profiling = profiling;
        resultDirectory = Path.Combine(root, "Results", "Gpu", "Profiles");
        var folder = Path.Combine(root, "Models", "Toz");
        using var stream = File.OpenRead(Path.Combine(folder, "tokenizer.model"));
        tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false);
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 4, InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            EnableMemoryPattern = !gpu,
            ProfileOutputPathPrefix = Path.Combine(resultDirectory, name),
            EnableProfiling = profiling,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING
        };
        if (gpu)
        {
            options.AppendExecutionProvider_DML(adapterIndex);
            if (strict) options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
        }
        Program.Log($"Loading {name}; adapter index={adapterIndex}; strict={strict}");
        string modelFolder = floatModel ? Path.Combine(folder, "Float32") : folder;
        encoder = new InferenceSession(Path.Combine(modelFolder, floatModel ? "encoder.onnx" : "encoder_q8.onnx"), options);
        options.EnableProfiling = false;
        options.ProfileOutputPathPrefix = Path.Combine(resultDirectory, name + "-decoder");
        options.EnableProfiling = profiling;
        decoder = new InferenceSession(Path.Combine(modelFolder, floatModel ? "decoder.onnx" : "decoder_q8.onnx"), options);
        Program.Log($"Loaded {name}");
    }

    public string Translate(string text, string target)
    {
        var paragraphs = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            var output = new List<string>();
            foreach (string sentence in Regex.Split(paragraph.Trim(), "(?<=[.!?…])\\s+(?=[\"'“‘(\\[]*[A-ZÇĞİÖŞÜ0-9])"))
                output.Add(TranslateSentence(sentence, target));
            paragraphs.Add(string.Join(' ', output));
        }
        return string.Join('\n', paragraphs);
    }

    private string TranslateSentence(string text, string target)
    {
        int[] ids = tokenizer.EncodeToIds(text).Take(254).ToArray();
        if (ids.Length == 0) return "";
        long[] source = [target == "en" ? 4L : 5L, .. ids.Select(i => (long)i), 3L];
        using var cross = encoder.Run([LongInput("src", source, [1, source.Length])]);
        var crossK = cross.First(v => v.Name == "cross_k").AsTensor<float>();
        var crossV = cross.First(v => v.Name == "cross_v").AsTensor<float>();
        var crossInputs = new[] { NamedOnnxValue.CreateFromTensor("cross_k", crossK), NamedOnnxValue.CreateFromTensor("cross_v", crossV) };
        Tensor<float> pastK = new DenseTensor<float>([6, 1, 8, 0, 64]);
        Tensor<float> pastV = new DenseTensor<float>([6, 1, 8, 0, 64]);
        var beams = new List<Beam> { new([], 0, 2, 0) };
        var finished = new List<(double Score, int[] Tokens)>();
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? retained = null;
        try
        {
            for (int position = 0; position < Math.Min(2 * ids.Length + 10, 254); position++)
            {
                var inputs = new[]
                {
                    LongInput("token", beams.Select(b => (long)b.Last).ToArray(), [beams.Count, 1]),
                    LongInput("pos", [(long)position], [1]),
                    NamedOnnxValue.CreateFromTensor("past_k", pastK),
                    NamedOnnxValue.CreateFromTensor("past_v", pastV),
                    LongInput("beam_idx", beams.Select(b => (long)b.Parent).ToArray(), [beams.Count]),
                    crossInputs[0], crossInputs[1]
                };
                var result = decoder.Run(inputs);
                retained?.Dispose();
                retained = result;
                var logits = result.First(v => v.Name == "logits").AsTensor<float>().ToArray();
                var top = new List<Candidate>(9);
                for (int b = 0; b < beams.Count; b++)
                {
                    int offset = b * 32000;
                    float maximum = float.NegativeInfinity;
                    for (int token = 0; token < 32000; token++) maximum = Math.Max(maximum, logits[offset + token]);
                    double sum = 0;
                    for (int token = 0; token < 32000; token++) sum += Math.Exp(logits[offset + token] - maximum);
                    double logSum = maximum + Math.Log(sum);
                    for (int token = 1; token < 32000; token++)
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
                    if (candidate.Token == 3) finished.Add((candidate.Score / (old.Tokens.Length + 1), old.Tokens));
                    else if (next.Count < 4) next.Add(new([.. old.Tokens, candidate.Token], candidate.Score, candidate.Token, candidate.Beam));
                }
                if (next.Count == 0 || finished.Count >= 4) break;
                pastK = result.First(v => v.Name == "new_k").AsTensor<float>();
                pastV = result.First(v => v.Name == "new_v").AsTensor<float>();
                beams = next;
            }
            if (finished.Count == 0) finished.Add((0, beams[0].Tokens));
            return tokenizer.Decode(finished.MaxBy(x => x.Score).Tokens);
        }
        finally { retained?.Dispose(); }
    }

    private static NamedOnnxValue LongInput(string name, long[] data, int[] dimensions)
        => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(data, dimensions));

    public void SaveProfiles()
    {
        if (profilesSaved || !profiling) return;
        profilesSaved = true;
        Program.Log("Encoder profile: " + encoder.EndProfiling());
        Program.Log("Decoder profile: " + decoder.EndProfiling());
    }

    public void Dispose()
    {
        try { SaveProfiles(); }
        finally { encoder.Dispose(); decoder.Dispose(); }
    }

    private sealed record Beam(int[] Tokens, double Score, int Last, int Parent);
    private sealed record Candidate(int Beam, int Token, double Score);
}
