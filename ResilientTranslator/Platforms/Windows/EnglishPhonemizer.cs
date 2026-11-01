using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ResilientTranslator.MainProject.Infrastructure.Speech;

namespace ResilientTranslator;

/// <summary>Serializes eSpeak's process-global pronunciation state off the UI thread.</summary>
public sealed class EnglishPhonemizer : IEnglishPhonemizer
{
    const bool IsLogEnabled = true;
    readonly SemaphoreSlim Gate = new(1, 1);
    nint Library;
    Phonemes? Read;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Initialize(int output, int buffer, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Voice([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint Phonemes(ref nint text, int encoding, int mode);

    /// <inheritdoc />
    public async Task<string[]> ConvertAsync(string text, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => Convert(text, token), token).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    string[] Convert(string text, CancellationToken token)
    {
        if (Read is null)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Speech", "espeak");
            if (Library == 0) Library = NativeLibrary.Load(Path.Combine(path, "espeak-ng.dll"));
            T Api<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library, name));
            if (Api<Initialize>("espeak_Initialize")(2, 0, path, 0) <= 0 || Api<Voice>("espeak_SetVoiceByName")("en-us") != 0)
                throw new InvalidOperationException("English pronunciation initialization failed.");
            Read = Api<Phonemes>("espeak_TextToPhonemes");
            Log("English pronunciation data initialized", Run: IsLogEnabled);
        }
        var sentences = new List<string>();
        foreach (string sentence in Regex.Split(text.Trim(), @"(?<=[.!?…])\s+|\n+"))
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sentence)) continue;
            nint allocation = Marshal.StringToCoTaskMemUTF8(sentence), pointer = allocation;
            try
            {
                var parts = new List<string>();
                while (pointer != 0 && Marshal.ReadByte(pointer) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    nint previous = pointer;
                    string? value = Marshal.PtrToStringUTF8(Read(ref pointer, 1, 2));
                    if (!string.IsNullOrEmpty(value)) parts.Add(value);
                    if (pointer == previous) throw new InvalidDataException("Pronunciation did not advance.");
                }
                sentences.Add(ToMisaki(string.Join(' ', parts)));
            }
            finally { Marshal.FreeCoTaskMem(allocation); }
        }
        return sentences.ToArray();
    }

    // Matches the supplied demo's American-English eSpeak-to-Misaki conversion.
    internal static string ToMisaki(string value)
    {
        foreach (var (from, to) in new (string, string)[] { ("ʔˌn̩","ʔn"),("ʔn̩","ʔn"),("aɪ","I"),("aʊ","W"),("dʒ","ʤ"),("eɪ","A"),("e","A"),("tʃ","ʧ"),("ɔɪ","Y"),("ʲo","jo"),("ʲə","jə"),("ʲ",""),("ɚ","əɹ"),("r","ɹ"),("x","k"),("ç","k"),("ɬ","l"),("̃","") }) value = value.Replace(from, to, StringComparison.Ordinal);
        value = Regex.Replace(value, @"(\S)̩", "ᵊ$1").Replace("̩", "");
        value = Regex.Replace(value, "əl(?=[\\s;:,.!?—…\"“”()]|$)", "ᵊl");
        value = Regex.Replace(value, "ɐ(?![\\s;:,.!?—…\"“”()]|$)", "ə");
        return value.Replace("oʊ","O").Replace("ɜːɹ","ɜɹ").Replace("ɜː","ɜɹ").Replace("ɪə","iə").Replace("ː","").Replace("o","ɔ").Replace("ɾ","T").Replace("ʔ","t");
    }
}
