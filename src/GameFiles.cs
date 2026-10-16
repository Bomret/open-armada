using System;
using System.Collections.Generic;
using System.IO;

namespace OpenArmada;

// Read-only access to a user-supplied Armada install. The original files mix name casing
// freely ("Fente.SOD", "fconst.sod"), so lookups ignore case on every platform.
public sealed class GameFiles
{
    public readonly string Root;
    readonly Dictionary<string, Dictionary<string, string>> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public GameFiles(string root)
    {
        if (!Directory.Exists(Path.Combine(root, "SOD")))
            throw new DirectoryNotFoundException($"no Armada install at '{root}' (missing SOD folder)");
        Root = root;
    }

    // Returns the full path of dir/name, or null when the file does not exist.
    public string? Find(string dir, string name)
    {
        if (!_dirs.TryGetValue(dir, out var files))
        {
            files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var full = Path.Combine(Root, dir);
            if (Directory.Exists(full))
                foreach (var f in Directory.GetFiles(full))
                    files[Path.GetFileName(f)] = f;
            _dirs[dir] = files;
        }
        return files.GetValueOrDefault(name);
    }
}
