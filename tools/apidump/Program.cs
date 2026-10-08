using System.Reflection;
var dll = args[0];
var paths = new List<string>(Directory.GetFiles(Path.GetDirectoryName(dll)!, "*.dll"));
paths.Add(dll);
var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var cssPkg = Path.Combine(home, ".nuget", "packages", "counterstrikesharp.api", "1.0.376", "lib", "net10.0");
if (Directory.Exists(cssPkg)) paths.AddRange(Directory.GetFiles(cssPkg, "*.dll"));
var dotnetShared = Path.Combine(home, "dsh-dotnet", "shared", "Microsoft.NETCore.App");
if (Directory.Exists(dotnetShared))
    foreach (var s in Directory.GetDirectories(dotnetShared)) paths.AddRange(Directory.GetFiles(s, "*.dll"));
var pl = new System.Reflection.PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(pl, "System.Private.CoreLib");
var asm = mlc.LoadFromAssemblyPath(dll);
void Dump(string n, params string[] pats) {
    var t = asm.GetTypes().FirstOrDefault(x => x.Name == n);
    if (t is null) { Console.WriteLine("[missing] " + n); return; }
    Console.WriteLine("== " + t.FullName);
    foreach (var m in t.GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) {
        if (pats.Length > 0 && !pats.Any(p => m.Name.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;
        Console.WriteLine("   " + m.MemberType + " " + m.Name);
    }
}
Dump("WasdMenu", "Key", "Display", "AddItem", "Prev", "Exit", "NumPerPage", "MenuTime", "Title");
Dump("BaseMenu", "AddItem", "Display", "PrevMenu", "ExitButton", "MenuTime");
Dump("MenuManager", "Open", "Close", "Instance", "Menu");
Dump("Config", "Sound", "Wasd", "MenuType", "Config");