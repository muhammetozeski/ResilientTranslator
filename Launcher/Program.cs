using System.Diagnostics;
string target = Path.Combine(AppContext.BaseDirectory, "AppData", "ResilientTranslator.exe");
var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
foreach (string argument in args) start.ArgumentList.Add(argument);
Process.Start(start);
