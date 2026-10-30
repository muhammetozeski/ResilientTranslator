using System.Text;

internal static class OnnxFloatConverter
{
    public static void Convert(string input, string output)
    {
        var model = Message.Parse(File.ReadAllBytes(input));
        var graph = Message.Parse(model.Fields.Single(x => x.Number == 7).Data);
        var nodes = graph.Fields.Where(x => x.Number == 1).Select(x => Message.Parse(x.Data)).ToList();
        var initializers = graph.Fields.Where(x => x.Number == 5).Select(x => Message.Parse(x.Data)).ToDictionary(x => x.Text(8));
        string[] Inputs(Message node) => node.Texts(1).ToArray();
        string[] Outputs(Message node) => node.Texts(2).ToArray();
        var producers = nodes.SelectMany(n => Outputs(n).Select(o => (o, n))).ToDictionary(x => x.o, x => x.n);
        var removed = new HashSet<Message>();
        int converted = 0;
        foreach (var matmul in nodes.Where(x => x.Text(4) == "MatMulInteger"))
        {
            string[] args = Inputs(matmul);
            var quantize = producers[args[0]];
            if (quantize.Text(4) != "DynamicQuantizeLinear") throw new InvalidDataException("Unexpected activation quantization.");
            string integerOutput = Outputs(matmul).Single();
            var cast = nodes.Single(n => n.Text(4) == "Cast" && Inputs(n).Contains(integerOutput));
            string castOutput = Outputs(cast).Single();
            var multiply = nodes.Single(n => n.Text(4) == "Mul" && Inputs(n).Contains(castOutput));
            string scaleName = Inputs(multiply).Single(x => x != castOutput);
            var scaleNode = producers[scaleName];
            if (scaleNode.Text(4) != "Mul") throw new InvalidDataException("Unexpected rescale operation.");
            string weightScaleName = Inputs(scaleNode).Single(initializers.ContainsKey);
            var weight = initializers[args[1]];
            float scale = ReadSingle(initializers[weightScaleName]);
            int zero = args.Length > 3 ? ReadInteger(initializers[args[3]]) : 0;
            byte[] raw = weight.Fields.Single(x => x.Number == 9).Data;
            bool signed = weight.Number(2) == 3;
            var floats = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++) floats[i] = ((signed ? unchecked((sbyte)raw[i]) : raw[i]) - zero) * scale;
            string floatName = args[1] + "_dequantized_float32";
            if (!initializers.ContainsKey(floatName))
            {
                var copy = new Message(weight.Fields.Where(x => x.Number is not (2 or 8 or 9)).ToList());
                copy.Fields.Add(Field.Integer(2, 1));
                copy.Fields.Add(Field.String(8, floatName));
                byte[] bytes = new byte[floats.Length * 4];
                Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
                copy.Fields.Add(Field.Bytes(9, bytes));
                initializers.Add(floatName, copy);
            }
            string finalOutput = Outputs(multiply).Single();
            string nodeName = multiply.Text(3);
            multiply.Fields.Clear();
            multiply.Fields.AddRange([Field.String(1, Inputs(quantize).Single()), Field.String(1, floatName),
                Field.String(2, finalOutput), Field.String(3, nodeName), Field.String(4, "MatMul")]);
            removed.Add(matmul); removed.Add(cast);
            converted++;
        }
        if (converted == 0) throw new InvalidDataException("No quantized matrix multiplications found.");
        var needed = graph.Fields.Where(x => x.Number == 12).Select(x => Message.Parse(x.Data).Text(1)).ToHashSet();
        var kept = new List<Message>();
        foreach (var node in nodes.AsEnumerable().Reverse())
        {
            if (removed.Contains(node) || !Outputs(node).Any(needed.Contains)) continue;
            kept.Add(node);
            foreach (string source in Inputs(node)) needed.Add(source);
        }
        kept.Reverse();
        graph.Fields.RemoveAll(x => x.Number is 1 or 5);
        graph.Fields.AddRange(kept.Select(x => Field.Bytes(1, x.Serialize())));
        graph.Fields.AddRange(initializers.Where(x => needed.Contains(x.Key)).Select(x => Field.Bytes(5, x.Value.Serialize())));
        model.Fields.RemoveAll(x => x.Number == 7);
        model.Fields.Add(Field.Bytes(7, graph.Serialize()));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, model.Serialize());
        Console.WriteLine($"Converted {converted} quantized MatMul operations: {output}");
    }

    private static float ReadSingle(Message tensor)
    {
        byte[] raw = tensor.Fields.FirstOrDefault(x => x.Number == 9)?.Data
            ?? tensor.Fields.First(x => x.Number == 4).Data;
        if (raw.Length != 4) throw new InvalidDataException("Only per-tensor quantization is supported.");
        return BitConverter.ToSingle(raw);
    }

    private static int ReadInteger(Message tensor)
    {
        byte[]? raw = tensor.Fields.FirstOrDefault(x => x.Number == 9)?.Data;
        if (raw is null)
        {
            var field = tensor.Fields.Single(x => x.Number == 5);
            if (field.Wire == 0) return unchecked((int)field.Value);
            ulong number = 0;
            int shift = 0;
            foreach (byte value in field.Data)
            {
                number |= (ulong)(value & 127) << shift;
                if (value < 128) return unchecked((int)number);
                shift += 7;
            }
            throw new InvalidDataException("Invalid packed zero point.");
        }
        if (raw.Length != 1) throw new InvalidDataException("Unexpected zero point shape.");
        return tensor.Number(2) == 3 ? unchecked((sbyte)raw[0]) : raw[0];
    }

    private sealed class Message(List<Field> fields)
    {
        public List<Field> Fields { get; } = fields;
        public string Text(int field) => Texts(field).SingleOrDefault() ?? "";
        public IEnumerable<string> Texts(int field) => Fields.Where(x => x.Number == field).Select(x => Encoding.UTF8.GetString(x.Data));
        public ulong Number(int field) => Fields.Single(x => x.Number == field).Value;
        public static Message Parse(byte[] data)
        {
            int position = 0;
            ulong Read()
            {
                ulong value = 0;
                for (int shift = 0; shift < 70; shift += 7)
                {
                    if (position >= data.Length) throw new InvalidDataException("Truncated protobuf.");
                    byte b = data[position++]; value |= (ulong)(b & 127) << shift;
                    if (b < 128) return value;
                }
                throw new InvalidDataException("Invalid protobuf varint.");
            }
            var result = new List<Field>();
            while (position < data.Length)
            {
                ulong tag = Read(); int number = (int)(tag >> 3), wire = (int)(tag & 7);
                if (wire == 0) { result.Add(new(number, wire, Read(), [])); continue; }
                int length = wire switch { 1 => 8, 2 => checked((int)Read()), 5 => 4, _ => throw new InvalidDataException("Unsupported protobuf wire type.") };
                if (length > data.Length - position) throw new InvalidDataException("Truncated protobuf field.");
                result.Add(new(number, wire, 0, data.AsSpan(position, length).ToArray())); position += length;
            }
            return new(result);
        }
        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            void Write(ulong value) { while (value > 127) { stream.WriteByte((byte)((value & 127) | 128)); value >>= 7; } stream.WriteByte((byte)value); }
            foreach (var field in Fields)
            {
                Write((ulong)((field.Number << 3) | field.Wire));
                if (field.Wire == 0) Write(field.Value);
                else { if (field.Wire == 2) Write((ulong)field.Data.Length); stream.Write(field.Data); }
            }
            return stream.ToArray();
        }
    }
    private sealed record Field(int Number, int Wire, ulong Value, byte[] Data)
    {
        public static Field Integer(int field, ulong value) => new(field, 0, value, []);
        public static Field Bytes(int field, byte[] data) => new(field, 2, 0, data);
        public static Field String(int field, string text) => Bytes(field, Encoding.UTF8.GetBytes(text));
    }
}
