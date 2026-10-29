namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Connects a registered setting to a translated UI field and its accepted numeric interval.</summary>
/// <param name="resourceName">The nameof a public Strings field translated by every language file.</param>
/// <param name="technical">Whether reflection places the setting in the technical section.</param>
/// <param name="minimum">Inclusive numeric lower bound.</param>
/// <param name="maximum">Inclusive numeric upper bound.</param>
[AttributeUsage(AttributeTargets.Field)]
internal sealed class PreferenceAttribute(string resourceName, bool technical = false, double minimum = double.MinValue, double maximum = double.MaxValue) : Attribute
{
    public readonly System.Reflection.FieldInfo Resource = typeof(Localization.Strings).GetField(resourceName)
        ?? throw new ArgumentException("Unknown UI string field.", nameof(resourceName));
    public readonly bool Technical = technical;
    public readonly double Minimum = minimum;
    public readonly double Maximum = maximum;
}
