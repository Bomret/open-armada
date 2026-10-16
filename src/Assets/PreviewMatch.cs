using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Assets;

// Matches asset browser previews to asset kinds. Godot-free: the Viewer supplies the previews.
public static class PreviewMatch
{
    // The previews by kind name. Each candidate is a preview type's name and a function that builds it
    // and returns its kind name. A broken registration (a preview that cannot be built, a second preview
    // for one kind, a preview naming no kind in `kindNames`) is skipped and described in `problems`, so
    // the browser still opens and shows it (INV-0002). `kindNames` are the registered kinds
    // (AssetKind.Discover), which exist with or without an install.
    public static Dictionary<string, T> Match<T>(
        IEnumerable<(string TypeName, Func<(string KindName, T Preview)> Create)> candidates,
        IEnumerable<string> kindNames,
        out IReadOnlyList<string> problems)
    {
        var found = new Dictionary<string, T>();
        var list = new List<string>();
        var known = kindNames.ToHashSet();
        foreach (var (typeName, create) in candidates)
        {
            string kindName;
            T preview;
            try { (kindName, preview) = create(); }
            catch (Exception e) { list.Add($"preview {typeName} cannot be created: {(e.InnerException ?? e).Message}"); continue; }
            if (!known.Contains(kindName)) list.Add($"preview {typeName} names kind \"{kindName}\", which no asset kind has");
            else if (!found.TryAdd(kindName, preview)) list.Add($"preview {typeName} is a second preview for kind \"{kindName}\"");
        }
        problems = list;
        return found;
    }

    // A preview of type `type` built with its parameterless constructor. Throws InvalidOperationException
    // when the type has none; an exception its constructor throws arrives wrapped, and Match reports the
    // inner one's message.
    public static T Create<T>(Type type) =>
        type.GetConstructor(Type.EmptyTypes) == null
            ? throw new InvalidOperationException("it has no parameterless constructor")
            : (T)Activator.CreateInstance(type)!;
}
