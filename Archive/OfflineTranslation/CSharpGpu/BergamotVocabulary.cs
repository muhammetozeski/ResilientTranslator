using System.Text;
using Microsoft.ML.Tokenizers;

internal static class BergamotVocabulary
{
    public static SentencePieceTokenizer Load(string file)
    {
        var pieces = new List<(string Piece, float Score)>();
        var controls = new Dictionary<string, int>();
        byte[] chars = [];
        bool dummy = true, escape = true;
        foreach (var field in Fields(File.ReadAllBytes(file)))
        {
            if (field.Number == 1)
            {
                var values = Fields(field.Bytes).ToArray();
                string piece = Encoding.UTF8.GetString(values.Single(x => x.Number == 1).Bytes);
                var scoreField = values.FirstOrDefault(x => x.Number == 2);
                float score = scoreField is null ? 0 : BitConverter.ToSingle(scoreField.Bytes);
                int type = (int)(values.FirstOrDefault(x => x.Number == 3)?.Value ?? 1);
                if (type == 3) controls[piece] = pieces.Count;
                pieces.Add((piece, score));
            }
            else if (field.Number == 3)
            {
                foreach (var spec in Fields(field.Bytes))
                {
                    if (spec.Number == 2) chars = spec.Bytes;
                    if (spec.Number == 3) dummy = spec.Value != 0;
                    if (spec.Number == 5) escape = spec.Value != 0;
                }
            }
        }
        if (pieces.Count != 32000 || pieces[1].Piece != "<unk>" || !controls.Values.Contains(0))
            throw new InvalidDataException("Unexpected Bergamot vocabulary.");
        return SentencePieceTokenizer.Create(pieces, unkId: 1, addBeginningOfSentence: false, addEndOfSentence: false,
            precompiledCharsMap: chars, addDummyPrefix: dummy, escapeWhiteSpaces: escape,
            treatWhitespaceAsSuffix: false, byteFallback: false, specialTokens: controls);
    }

    private static IEnumerable<Field> Fields(byte[] data)
    {
        int position = 0;
        ulong ReadVarint()
        {
            ulong value = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                if (position >= data.Length) throw new InvalidDataException("Truncated protobuf.");
                byte current = data[position++];
                value |= (ulong)(current & 127) << shift;
                if (current < 128) return value;
            }
            throw new InvalidDataException("Invalid protobuf integer.");
        }
        while (position < data.Length)
        {
            ulong tag = ReadVarint();
            int number = (int)(tag >> 3), wire = (int)(tag & 7);
            if (wire == 0) { yield return new(number, ReadVarint(), []); continue; }
            int length = wire switch { 1 => 8, 2 => checked((int)ReadVarint()), 5 => 4, _ => throw new InvalidDataException("Unsupported protobuf field.") };
            if (length > data.Length - position) throw new InvalidDataException("Truncated protobuf payload.");
            byte[] bytes = data.AsSpan(position, length).ToArray();
            position += length;
            yield return new(number, 0, bytes);
        }
    }
    private sealed record Field(int Number, ulong Value, byte[] Bytes);
}
