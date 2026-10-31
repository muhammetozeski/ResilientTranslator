using System.Reflection;
namespace ResilientTranslator.MainProject.Infrastructure.Localization;

/// <summary>Resolves dynamic feature metadata through the existing translated Strings fields.</summary>
public static class FeatureText
{
    static readonly IReadOnlyDictionary<string,FieldInfo> Fields=typeof(Strings).GetFields(BindingFlags.Public|BindingFlags.Static)
        .Where(field=>field.FieldType==typeof(string)).ToDictionary(field=>field.Name);
    public static string Get(string key) => Fields.TryGetValue(key,out var field)
        ? (string)field.GetValue(null)! : throw new ArgumentException("Unknown localized resource.",nameof(key));
}
