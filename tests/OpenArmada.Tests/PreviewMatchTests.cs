using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class PreviewMatchTests
{
    static (string, Func<(string, string)>) Ok(string type, string kind) => (type, () => (kind, type));

    [TestMethod]
    public void ValidPreviewMatchesRegisteredKindName()
    {
        // The registered kind names do not depend on the install, so a browser with none reports no problem.
        var found = PreviewMatch.Match(new[] { Ok("ModelPreview", "models") }, new[] { "models" }, out var problems);
        Assert.AreEqual(0, problems.Count);
        Assert.AreEqual("ModelPreview", found["models"]);
    }

    [TestMethod]
    public void UnknownKindNameIsReported()
    {
        var found = PreviewMatch.Match(new[] { Ok("ModelPreview", "ships") }, new[] { "models" }, out var problems);
        Assert.AreEqual(0, found.Count);
        Assert.AreEqual("preview ModelPreview names kind \"ships\", which no asset kind has", problems.Single());
    }

    [TestMethod]
    public void SecondPreviewForOneKindIsReported()
    {
        var found = PreviewMatch.Match(
            new[] { Ok("A", "models"), Ok("B", "models") },
            new[] { "models" },
            out var problems
        );
        Assert.AreEqual("A", found["models"]);
        Assert.AreEqual("preview B is a second preview for kind \"models\"", problems.Single());
    }

    [TestMethod]
    public void ThrowingConstructorIsReported()
    {
        var bad = ("Bad", (Func<(string, string)>)(() => throw new InvalidOperationException("boom")));
        var found = PreviewMatch.Match(new[] { bad }, new[] { "models" }, out var problems);
        Assert.AreEqual(0, found.Count);
        Assert.AreEqual("preview Bad cannot be created: boom", problems.Single());
    }

    sealed class NoCtor
    {
        public NoCtor(int _) { }
    }

    sealed class ThrowingCtor
    {
        public ThrowingCtor() => throw new InvalidOperationException("boom");
    }

    // A candidate the way KindPreview.Discover builds one: PreviewMatch.Create makes the instance.
    static (string, Func<(string, object)>) Built(Type t) => (t.Name, () => ("models", PreviewMatch.Create<object>(t)));

    [TestMethod]
    public void MissingParameterlessConstructorIsReported()
    {
        var found = PreviewMatch.Match(new[] { Built(typeof(NoCtor)) }, new[] { "models" }, out var problems);
        Assert.AreEqual(0, found.Count);
        Assert.AreEqual("preview NoCtor cannot be created: it has no parameterless constructor", problems.Single());
    }

    [TestMethod]
    public void ConstructorThatThrowsIsReportedWithItsMessage()
    {
        var found = PreviewMatch.Match(new[] { Built(typeof(ThrowingCtor)) }, new[] { "models" }, out var problems);
        Assert.AreEqual(0, found.Count);
        Assert.AreEqual("preview ThrowingCtor cannot be created: boom", problems.Single());
    }
}
