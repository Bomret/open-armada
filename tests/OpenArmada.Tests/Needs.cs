namespace OpenArmada.Tests;

/// <summary>The <c>[TestCategory]</c> names of the integration tests: tests that need the install or Godot. The unit test
/// commands (<c>mise.toml</c> tasks <c>test</c> and <c>check</c>, <c>tools/focused-tests.sh</c>) filter them out, and the
/// integration command (<c>test:integration</c>) runs only them.</summary>
internal static class Needs
{
    public const string Install = "Install";
    public const string Godot = "Godot";
}
