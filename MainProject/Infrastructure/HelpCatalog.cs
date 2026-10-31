using System.Reflection;
using ResilientTranslator.MainProject.Infrastructure.Localization;
namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Maps each actual registered preference object to its specific translated explanation.</summary>
internal static class HelpCatalog
{
    static readonly IReadOnlyDictionary<ISetting,string> Keys=typeof(Settings).GetFields(BindingFlags.Public|BindingFlags.Static)
        .Where(field=>field.GetCustomAttribute<PreferenceAttribute>() is not null)
        .ToDictionary(field=>(ISetting)field.GetValue(null)!,field=>"Help"+field.Name);
    public static string ForSetting(ISetting setting)=>FeatureText.Get(Keys[setting]);
}
