namespace OpenArmada.Sandbox;

/// <summary>
/// The name a built model node carries for a SOD node name. Godot replaces the characters . : @ / " % in a node
/// name with underscores (String.validate_node_name), and an empty name becomes "node". The builder and the
/// hardpoint lookup both use this, so a lookup finds the node the builder made.
/// </summary>
public static class NodeName
{
    /// <summary>The built node's name for the SOD node name <c>sodName</c>.</summary>
    public static string Of(string sodName) =>
        sodName.Length == 0
            ? "node"
            : sodName
                .Replace('.', '_')
                .Replace(':', '_')
                .Replace('@', '_')
                .Replace('/', '_')
                .Replace('"', '_')
                .Replace('%', '_');
}
