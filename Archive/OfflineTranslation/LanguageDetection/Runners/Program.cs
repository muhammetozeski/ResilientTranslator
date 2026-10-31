using System.Diagnostics;
using System.Text.Json;
using Lingua;
var root = Path.GetFullPath(args[0]);
var low = args.Length > 1 && args[1].StartsWith("low");
var trimmed = args.Length > 1 && args[1] == "low-trimmed";
var timer = Stopwatch.StartNew();
var builder = LanguageDetectorBuilder.FromLanguages(Language.English, Language.Turkish, Language.German, Language.French, Language.Spanish, Language.Italian, Language.Russian)
    .WithLanguageModelsDirectory(Path.Combine(root,"Sources","Lingua",trimmed?"models-low":"models"));
if (low) builder = builder.WithLowAccuracyMode();
var detector = builder.WithPreloadedLanguageModels().Build();
var loadMs = timer.Elapsed.TotalMilliseconds;
var cases = JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(Path.Combine(root,"Corpus","cases.json")))!;
string Detect(string text) { var language = detector.DetectLanguageOf(text); return language == Language.Unknown ? "und" : LanguageInfo.IsoCode6391(language).ToString().ToLowerInvariant(); }
Console.WriteLine(JsonSerializer.Serialize(new {loadMs,smoke=Detect("Bu cümle Türkçe olarak yazılmıştır.")}));
for(var i=0;i<3;i++) foreach(var c in cases) Detect(c.text);
var rows=new List<object>();
foreach(var c in cases) {
 var samples=new double[15];var predicted=Detect(c.text);
 for(var i=0;i<15;i++){var start=Stopwatch.GetTimestamp();Detect(c.text);samples[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}
 Array.Sort(samples);
 rows.Add(new {c.id,c.source,c.category,c.text,predicted,correct=predicted==c.source,ms=samples[7]});
}
var mode=trimmed?"lingua-low-trimmed":low?"lingua-low":"lingua";
File.WriteAllText(Path.Combine(root,"Results",mode+".json"),JsonSerializer.Serialize(new{mode,loadMs,rows},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{mode}: {rows.Count} results saved");
record Sample(string id,string source,string category,string text);
