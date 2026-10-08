using System.Reflection;
var dll = args[0];
var paths = new List<string>(Directory.GetFiles(Path.GetDirectoryName(dll)!, "*.dll"));
paths.Add(dll);
foreach (var s in Directory.GetDirectories(@"C:\Users\ahmatjan\dsh-dotnet\shared\Microsoft.NETCore.App")) paths.AddRange(Directory.GetFiles(s, "*.dll"));
var pl = new System.Reflection.PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(pl, "System.Private.CoreLib");
var asm = mlc.LoadFromAssemblyPath(dll);
foreach (var n in new[] { "EventPlayerDisconnect", "EventPlayerPickup", "EventItemPickup" })
{
    var t = asm.GetTypes().FirstOrDefault(x => x.Name == n);
    Console.WriteLine(n + ": " + (t?.FullName ?? "MISSING"));
    if (t is not null) foreach (var p in t.GetProperties(BindingFlags.Public|BindingFlags.Instance).Take(8)) Console.WriteLine("   " + p.Name + " : " + p.PropertyType.Name);
}
var db = asm.GetTypes().FirstOrDefault(x => x.Name == "MySqlDatabase" || x.FullName!.Contains("Database"));
Console.WriteLine("builtin Database type: " + (db?.FullName ?? "none"));
