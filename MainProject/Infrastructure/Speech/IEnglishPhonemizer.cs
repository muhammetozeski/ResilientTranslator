namespace ResilientTranslator.MainProject.Infrastructure.Speech;

/// <summary>Converts English text with the bundled pronunciation data, without language detection.</summary>
public interface IEnglishPhonemizer
{
    /// <summary>Produces sentence phonemes on a worker; cancellation stops further sentences.</summary>
    Task<string[]> ConvertAsync(string text, CancellationToken token);
}
