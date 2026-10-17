# Coding Guidelines (LLM Reference)

Target audience: LLMs. General rules apply to any language, specific languages have their own heading.

## General

These are general coding guidelines that must be used for all programming languages that support these concepts.

### Immutable data / behavioral services

Favor functional-ish programming: split code into immutable DTOs that only contain data and separate behavioral types (e.g. services) where possible. Properties, fields and parameters should also be readonly, get/init where possible. For collections favour IReadOnlyCollection<T> or Immutable*<T> where possible.

### No Over-engineering

Favour simple and concise solutions. Don't add complexity where not needed. Honor SOLID, KISS and YAGNI principles.

### Composition over inheritance

Prefer composing small, focused types over deep inheritance hierarchies.

### Blank lines

**Primary rule: group by logical context.** Statements that belong to the same step sit together. A blank line signals a context switch.

**Sync↔async boundary is always a context switch.** Every transition from sync to async (or back) gets a blank line.

**Log statements are always their own context group.** A log call always gets a blank line above and below it.

**A statement after a block starts a new context.** A statement that follows a closing brace (the end of an `if`, a loop, a `try`, or a nested block) gets a blank line above it.

### Jump statements

Always put a blank line before a jump statement **unless it is the sole statement in its block**.

```
// sole statement — no blank line, no braces needed
if (condition)
    return;

// preceding code — blank line before jump
if (condition) {
    log();

    return;
}
```

### Braces / control flow

Always use braces around loops and conditional branching. The one exception is a branch whose sole statement is a jump (`return`, `break`, `continue`, `throw`): it may go without braces. CSharpier puts that statement on its own line.

### Member ordering

Follow the language's established conventions and best practices for member ordering.

### Document

Document public and protected/abstract/overridable facing types and members using appropriate code documentation. Be specific and concise but also mention side-effects, possible errors/exceptions and possibly unexpected behavior.

## C# Style

### Member ordering

Order by visibility first (`public` → `protected` → `private`), then within each group:

1. Constants / static fields
2. Properties / instance fields
3. Static methods
4. Instance methods

### Access modifiers

Omit default access modifiers (`internal` on classes, `private` on fields).

### var

Default to `var`. Use explicit types only when it aids readability, e.g. `IReadOnlyCollection<string> names = ["alice", "bob"]`.

### Naming

| Thing | Convention | Example |
|-------|-----------|---------|
| Classes / interfaces | PascalCase | `GameFileSystem`, `AssetKind` |
| Interface prefix | `I` | `IComparer<T>` |
| Private fields | `_camelCase` | `_entries` |
| Records | PascalCase | `PhysicsValues`, `ShipPose` |
| Constants | PascalCase | `PaletteKind.KindName`, `Reached` |

### Modern C# features

Use records, primary constructors, collection expressions, and other C# 12 features. Both projects target `net8.0` without a `<LangVersion>`, so C# 12 is the language version. Newer features, such as `field` or extension members, do not compile.

### Classes

Always use primary constructors with no class fields, if possible.

### Records

Records carry data through `required` init-only properties, never positional (primary-constructor) parameters. Each property is its own member with its own doc comment; construct with an object initializer.

```csharp
// Yes
public sealed record Alarm
{
    /// <summary>The job the alarm is about.</summary>
    public required string JobName { get; init; }
    public required AlarmState State { get; init; }
}

var alarm = new Alarm { JobName = "job-a", State = AlarmState.Raised };

// No — positional record
public sealed record Alarm(string JobName, AlarmState State);
```

A property that carries a default stays a non-`required` init property with an initializer (`public uint Flags { get; init; } = 0;`). Primary constructors remain the norm for classes (for example, `UnsupportedFormatException`); the rule above is records-only.

