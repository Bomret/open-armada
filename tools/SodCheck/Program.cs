// Parses every .sod in a directory and reports which ones fail, or prints one file's node tree.
// Usage: dotnet run --project tools/SodCheck -- <Armada install>/SOD
//        dotnet run --project tools/SodCheck -- <Armada install>/SOD/Fente.SOD
using System;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

var dir = args.Length > 0 ? args[0] : throw new ArgumentException("pass the SOD directory or one .sod file");
if (File.Exists(dir)) // one file: print its node tree
{
    var sod = Sod.Load(dir);
    void Print(string parent, int depth)
    {
        foreach (var n in sod.Nodes.Where(n => n.Parent == parent && n.Name != parent))
        {
            var mesh = n.Mesh == null ? "" : $" texture={n.Mesh.Texture} verts={n.Mesh.Vertices.Length}";
            Console.WriteLine($"{new string(' ', depth * 2)}{n.Name} [{n.Type}]{mesh}");
            Print(n.Name, depth + 1);
        }
    }
    Print("", 0);
    return 0;
}
var files = Directory.GetFiles(dir).Where(f => f.EndsWith(".sod", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
int failed = 0;
foreach (var file in files)
{
    try { Sod.Load(file); }
    catch (NotSupportedException e) { Console.WriteLine($"SKIP {Path.GetFileName(file)}: {e.Message}"); }
    catch (Exception e)
    {
        failed++;
        var version = BitConverter.ToSingle(File.ReadAllBytes(file), 10);
        Console.WriteLine($"FAIL {Path.GetFileName(file)} v{version:0.0}: {e.Message}");
    }
}
Console.WriteLine($"{files.Count - failed}/{files.Count} parsed");
return failed == 0 ? 0 : 1;
