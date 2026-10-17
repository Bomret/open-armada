namespace OpenArmada.Sandbox;

// The name a built model node carries for a SOD node name. Godot replaces the characters . : @ / " % in a node
// name with underscores (String.validate_node_name), and an empty name becomes "node". The builder and the
// hardpoint lookup both use this, so a lookup finds the node the builder made.
public static class NodeName
{
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
