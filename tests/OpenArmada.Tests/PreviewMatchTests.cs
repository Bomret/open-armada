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
    public void ValidPreviewMatchesRegisteredKindWithoutAnInstall()
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
        var found = PreviewMatch.Match(new[] { Ok("A", "models"), Ok("B", "models") }, new[] { "models" }, out var problems);
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
}
