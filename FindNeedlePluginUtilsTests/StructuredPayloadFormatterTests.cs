using Microsoft.VisualStudio.TestTools.UnitTesting;
using FindNeedlePluginUtils.StructuredLog;

namespace FindNeedlePluginUtilsTests;

[TestClass]
public class StructuredPayloadFormatterTests
{
    // payload with a plain value, a value containing a space, and a numeric-looking value.
    private const string Json = "{\"id\":\"42\",\"message\":\"disk full\",\"code\":\"5\"}";

    [TestMethod]
    public void KeyValueQuoted_QuotesEveryValue()
        => Assert.AreEqual("id=\"42\" message=\"disk full\" code=\"5\"",
            StructuredPayloadFormatter.Render(Json, PayloadFormat.KeyValueQuoted));

    [TestMethod]
    public void KeyValue_QuotesOnlyWhenNeeded()
        => Assert.AreEqual("id=42 message=\"disk full\" code=5",
            StructuredPayloadFormatter.Render(Json, PayloadFormat.KeyValue));

    [TestMethod]
    public void Json_RoundTripsAsCompactObject()
        => Assert.AreEqual(Json, StructuredPayloadFormatter.Render(Json, PayloadFormat.Json));

    [TestMethod]
    public void Pipe_MatchesLegacyShape()
        => Assert.AreEqual("id: 42 | message: disk full | code: 5 | ",
            StructuredPayloadFormatter.Render(Json, PayloadFormat.Pipe));

    [TestMethod]
    public void Custom_AppliesTemplatePerField()
        => Assert.AreEqual("id->42; message->disk full; code->5; ",
            StructuredPayloadFormatter.Render(Json, PayloadFormat.Custom, "{name}->{value}; "));

    [TestMethod]
    public void Custom_DefaultTemplateIsPerfViewLike()
        => Assert.AreEqual("id=\"42\" message=\"disk full\" code=\"5\" ",
            StructuredPayloadFormatter.Render(Json, PayloadFormat.Custom));

    [TestMethod]
    public void EmptyOrNonObject_YieldsEmpty()
    {
        Assert.AreEqual("", StructuredPayloadFormatter.Render("", PayloadFormat.KeyValueQuoted));
        Assert.AreEqual("", StructuredPayloadFormatter.Render("not json", PayloadFormat.KeyValueQuoted));
        Assert.AreEqual("", StructuredPayloadFormatter.Render("[1,2,3]", PayloadFormat.KeyValueQuoted));
    }
}
