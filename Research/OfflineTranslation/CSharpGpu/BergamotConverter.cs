using System.Text;

internal static class BergamotConverter
{
    internal sealed record Weight(long[] Shape, float[] Values);

    public static void ConvertPair(string root, string pair)
    {
        string folder = Path.Combine(root, "Models", "Mozilla", pair);
        string binary = Directory.GetFiles(folder, "model.*.bin").Single();
        var weights = ReadWeights(binary);
        string output = Path.Combine(root, "Models", "Mozilla", "Onnx", pair);
        Directory.CreateDirectory(output);
        BuildEncoder(weights).Save(Path.Combine(output, "encoder.onnx"));
        BuildDecoder(weights).Save(Path.Combine(output, "decoder.onnx"));
        Console.WriteLine($"Converted original Bergamot {pair} weights to float32 ONNX: {output}");
    }

    private static Dictionary<string, Weight> ReadWeights(string file)
    {
        using var reader = new BinaryReader(File.OpenRead(file));
        if (reader.ReadUInt64() != 1) throw new InvalidDataException("Unsupported Marian binary version.");
        int count = checked((int)reader.ReadUInt64());
        var headers = Enumerable.Range(0, count).Select(_ => new Header(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64())).ToArray();
        string[] names = headers.Select(h => Encoding.UTF8.GetString(reader.ReadBytes((int)h.NameLength)).TrimEnd('\0')).ToArray();
        long[][] shapes = headers.Select(h => Enumerable.Range(0, (int)h.ShapeLength).Select(_ => (long)reader.ReadInt32()).ToArray()).ToArray();
        ulong padding = reader.ReadUInt64();
        reader.BaseStream.Position += (long)padding;
        var output = new Dictionary<string, Weight>();
        for (int i = 0; i < count; i++)
        {
            byte[] bytes = reader.ReadBytes((int)headers[i].DataLength);
            string name = names[i];
            if (name.StartsWith("special:")) continue;
            int length = checked((int)shapes[i].Aggregate(1L, (a, b) => a * b));
            var values = new float[length];
            if (headers[i].Type == 1028) Buffer.BlockCopy(bytes, 0, values, 0, length * 4);
            else if (headers[i].Type == 16641)
            {
                float multiplier = BitConverter.ToSingle(bytes, length);
                if (!float.IsFinite(multiplier) || multiplier == 0) throw new InvalidDataException($"Invalid multiplier: {name}");
                if (name.Contains("Wemb"))
                    for (int j = 0; j < length; j++) values[j] = unchecked((sbyte)bytes[j]) / multiplier;
                else
                {
                    int columns = (int)shapes[i][^1], rows = length / columns;
                    for (int row = 0; row < rows; row++)
                        for (int column = 0; column < columns; column++)
                            values[row * columns + column] = unchecked((sbyte)bytes[column * rows + row]) / multiplier;
                }
            }
            else throw new InvalidDataException($"Unsupported tensor type {headers[i].Type}: {name}");
            long[] shape = shapes[i].Length == 2 && shapes[i][0] == 1 ? [shapes[i][1]] : shapes[i];
            output.Add(name, new(shape, values));
        }
        return output;
    }

    private static OnnxGraph BuildEncoder(Dictionary<string, Weight> weights)
    {
        var g = new OnnxGraph(weights);
        g.Input("src", 7, 1, "S");
        g.Input("positions", 1, 1, "S", 256);
        string x = g.Op("Add", g.Op("Mul", g.Op("Gather", g.W("Wemb"), "src", g.Int("axis", 0)), g.F(16)), "positions");
        for (int layer = 1; layer <= 6; layer++)
        {
            string prefix = $"encoder_l{layer}";
            string q = g.Heads(g.Dense(x, prefix + "_self_Wq", prefix + "_self_bq"));
            string k = g.Heads(g.Dense(x, prefix + "_self_Wk", prefix + "_self_bk"));
            string v = g.Heads(g.Dense(x, prefix + "_self_Wv", prefix + "_self_bv"));
            string attention = g.Attention(q, k, v);
            string joined = g.Join(attention);
            x = g.Norm(g.Op("Add", x, g.Dense(joined, prefix + "_self_Wo", prefix + "_self_bo")), prefix + "_self_Wo_ln");
            x = g.FeedForward(x, prefix + "_ffn");
        }
        var keys = new List<string>();
        var values = new List<string>();
        for (int layer = 1; layer <= 2; layer++)
        {
            string prefix = $"decoder_l{layer}_context";
            keys.Add(g.Heads(g.Dense(x, prefix + "_Wk", prefix + "_bk")));
            values.Add(g.Heads(g.Dense(x, prefix + "_Wv", prefix + "_bv")));
        }
        g.Output("cross_k", g.Op("Concat", keys[0], keys[1], g.Int("axis", 0)), 1, 2, 8, "S", 32);
        g.Output("cross_v", g.Op("Concat", values[0], values[1], g.Int("axis", 0)), 1, 2, 8, "S", 32);
        return g;
    }

    private static OnnxGraph BuildDecoder(Dictionary<string, Weight> weights)
    {
        var g = new OnnxGraph(weights);
        g.Input("token", 7, "B", 1);
        g.Input("positions", 1, "B", 1, 256);
        g.Input("embedding_mask", 1, 1);
        g.Input("past", 1, 2, "B0", 1, 256);
        g.Input("beam_idx", 7, "B");
        g.Input("cross_k", 1, 2, 8, "S", 32);
        g.Input("cross_v", 1, 2, 8, "S", 32);
        string embedding = g.Op("Gather", g.W("Wemb"), "token", g.Int("axis", 0));
        string x = g.Op("Add", g.Op("Mul", g.Op("Mul", embedding, "embedding_mask"), g.F(16)), "positions");
        var states = new List<string>();
        for (int layer = 1; layer <= 2; layer++)
        {
            string prefix = $"decoder_l{layer}";
            string previous = g.Op("Gather", "past", g.I(layer - 1), g.Int("axis", 0));
            previous = g.Op("Gather", previous, "beam_idx", g.Int("axis", 0));
            string f = g.Op("Sigmoid", g.Dense(x, prefix + "_rnn_Wf", prefix + "_rnn_bf"));
            string candidate = g.MatMul(x, prefix + "_rnn_W");
            string cell = g.Op("Add", g.Op("Mul", f, previous), g.Op("Mul", g.Op("Sub", g.F(1), f), candidate));
            states.Add(g.Op("Unsqueeze", cell, g.Is([0])));
            x = g.Norm(g.Op("Add", x, g.Op("Relu", cell)), prefix + "_rnn_ffn_ln");
            string q = g.Heads(g.Dense(x, prefix + "_context_Wq", prefix + "_context_bq"));
            string k = g.Op("Unsqueeze", g.Op("Gather", "cross_k", g.I(layer - 1), g.Int("axis", 0)), g.Is([0]));
            string v = g.Op("Unsqueeze", g.Op("Gather", "cross_v", g.I(layer - 1), g.Int("axis", 0)), g.Is([0]));
            string joined = g.Join(g.Attention(q, k, v));
            x = g.Norm(g.Op("Add", x, g.Dense(joined, prefix + "_context_Wo", prefix + "_context_bo")), prefix + "_context_Wo_ln");
            x = g.FeedForward(x, prefix + "_ffn");
        }
        string transposed = g.Op("Transpose", g.W("Wemb"), g.Ints("perm", [1, 0]));
        string logits = g.Op("Add", g.Op("MatMul", g.QuantizeActivation(x, "Wemb"), transposed), g.W("decoder_ff_logit_out_b"));
        g.Output("logits", logits, 1, "B", 1, 32000);
        g.Output("new_past", g.Op("Concat", states[0], states[1], g.Int("axis", 0)), 1, 2, "B", 1, 256);
        return g;
    }

    private sealed record Header(ulong NameLength, ulong Type, ulong ShapeLength, ulong DataLength);
}

internal sealed class OnnxGraph(Dictionary<string, BergamotConverter.Weight> weights)
{
    private readonly List<byte[]> nodes = [], initializers = [], inputs = [], outputs = [];
    private readonly HashSet<string> initialized = [];
    private int serial;
    internal sealed record Attribute(byte[] Bytes);

    public string W(string name)
    {
        if (initialized.Add(name))
        {
            var value = weights[name];
            byte[] data = new byte[value.Values.Length * 4];
            Buffer.BlockCopy(value.Values, 0, data, 0, data.Length);
            initializers.Add(Tensor(name, 1, value.Shape, data));
        }
        return name;
    }

    public string F(float value)
    {
        string name = "constant_" + serial++;
        initializers.Add(Tensor(name, 1, [1], BitConverter.GetBytes(value)));
        return name;
    }

    public string I(long value) => Is([value], scalar: true);
    public string Is(long[] values, bool scalar = false)
    {
        string name = "constant_" + serial++;
        byte[] bytes = new byte[values.Length * 8];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        initializers.Add(Tensor(name, 7, scalar ? [] : [values.Length], bytes));
        return name;
    }

    public string Op(string operation, params object[] arguments)
    {
        string name = "node_" + serial++;
        var p = new Proto();
        foreach (string input in arguments.OfType<string>()) p.Text(1, input);
        p.Text(2, name); p.Text(3, name); p.Text(4, operation);
        foreach (var attribute in arguments.OfType<Attribute>()) p.Bytes(5, attribute.Bytes);
        nodes.Add(p.Data);
        return name;
    }

    public Attribute Int(string name, long value) { var p = new Proto(); p.Text(1, name); p.Number(3, value); p.Number(20, 2); return new(p.Data); }
    public Attribute Ints(string name, long[] values) { var p = new Proto(); p.Text(1, name); foreach (long value in values) p.Number(8, value); p.Number(20, 7); return new(p.Data); }
    public Attribute Float(string name, float value) { var p = new Proto(); p.Text(1, name); p.FixedFloat(2, value); p.Number(20, 1); return new(p.Data); }
    public string QuantizeActivation(string x, string weight)
    {
        if (!weights.ContainsKey(weight + "_QuantMultA")) return x;
        string alpha = W(weight + "_QuantMultA");
        return Op("Div", Op("Clip", Op("Round", Op("Mul", x, alpha)), F(-127), F(127)), alpha);
    }
    public string MatMul(string x, string w) => Op("MatMul", QuantizeActivation(x, w), W(w));
    public string Dense(string x, string w, string b) => Op("Add", MatMul(x, w), W(b));
    public string Norm(string x, string prefix) => Op("LayerNormalization", x, W(prefix + "_scale"), W(prefix + "_bias"), Int("axis", -1), Float("epsilon", 1e-6f));
    public string FeedForward(string x, string prefix) => Norm(Op("Add", x, Dense(Op("Relu", Dense(x, prefix + "_W1", prefix + "_b1")), prefix + "_W2", prefix + "_b2")), prefix + "_ffn_ln");
    public string Heads(string x) => Op("Transpose", Op("Reshape", x, Is([0, -1, 8, 32])), Ints("perm", [0, 2, 1, 3]));
    public string Join(string x) => Op("Reshape", Op("Transpose", x, Ints("perm", [0, 2, 1, 3])), Is([0, -1, 256]));
    public string Attention(string q, string k, string v) => Op("MatMul", Op("Softmax", Op("Mul", Op("MatMul", q, Op("Transpose", k, Ints("perm", [0, 1, 3, 2]))), F(1f / MathF.Sqrt(32))), Int("axis", -1)), v);
    public void Input(string name, int type, params object[] dimensions) => inputs.Add(ValueInfo(name, type, dimensions));
    public void Output(string name, string value, int type, params object[] dimensions)
    {
        var p = new Proto(); p.Text(1, value); p.Text(2, name); p.Text(3, name); p.Text(4, "Identity"); nodes.Add(p.Data);
        outputs.Add(ValueInfo(name, type, dimensions));
    }

    public void Save(string file)
    {
        var graph = new Proto();
        foreach (byte[] node in nodes) graph.Bytes(1, node);
        graph.Text(2, "BergamotOriginalWeights");
        foreach (byte[] initializer in initializers) graph.Bytes(5, initializer);
        foreach (byte[] input in inputs) graph.Bytes(11, input);
        foreach (byte[] output in outputs) graph.Bytes(12, output);
        var model = new Proto(); model.Number(1, 8); model.Text(2, "CSharpBergamotConverter"); model.Bytes(7, graph.Data);
        var version = new Proto(); version.Text(1, ""); version.Number(2, 17); model.Bytes(8, version.Data);
        File.WriteAllBytes(file, model.Data);
    }

    private static byte[] Tensor(string name, int type, long[] dimensions, byte[] raw)
    {
        var p = new Proto(); foreach (long dimension in dimensions) p.Number(1, dimension);
        p.Number(2, type); p.Text(8, name); p.Bytes(9, raw); return p.Data;
    }

    private static byte[] ValueInfo(string name, int type, object[] dimensions)
    {
        var shape = new Proto();
        foreach (object dimension in dimensions)
        {
            var d = new Proto();
            if (dimension is string text) d.Text(2, text); else d.Number(1, Convert.ToInt64(dimension));
            shape.Bytes(1, d.Data);
        }
        var tensor = new Proto(); tensor.Number(1, type); tensor.Bytes(2, shape.Data);
        var outer = new Proto(); outer.Bytes(1, tensor.Data);
        var value = new Proto(); value.Text(1, name); value.Bytes(2, outer.Data); return value.Data;
    }

    private sealed class Proto
    {
        private readonly MemoryStream stream = new();
        public byte[] Data => stream.ToArray();
        private void Varint(ulong value) { while (value > 127) { stream.WriteByte((byte)((value & 127) | 128)); value >>= 7; } stream.WriteByte((byte)value); }
        public void Number(int field, long value) { Varint((ulong)(field << 3)); Varint(unchecked((ulong)value)); }
        public void Bytes(int field, byte[] value) { Varint((ulong)((field << 3) | 2)); Varint((ulong)value.Length); stream.Write(value); }
        public void Text(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
        public void FixedFloat(int field, float value) { Varint((ulong)((field << 3) | 5)); stream.Write(BitConverter.GetBytes(value)); }
    }
}
