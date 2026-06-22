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
    public void Reformat_SwapsPayloadSuffix_TemplatelessAndTemplated()
    {
        // templateless: whole message is the KeyValueQuoted payload → fully reformatted.
        var kvq = StructuredPayloadFormatter.Render(Json, PayloadFormat.KeyValueQuoted);
        Assert.AreEqual(Json, StructuredPayloadFormatter.Reformat(kvq, Json, PayloadFormat.KeyValueQuoted, PayloadFormat.Json));

        // templated: "<message> == <payload>" → only the payload suffix changes.
        var templated = "User logged in == " + kvq;
        Assert.AreEqual("User logged in == " + Json,
            StructuredPayloadFormatter.Reformat(templated, Json, PayloadFormat.KeyValueQuoted, PayloadFormat.Json));
    }

    [TestMethod]
    public void Reformat_LeavesUnrelatedMessageUnchanged()
    {
        // A message that doesn't end with the from-render (e.g. a CSV/JSON row) is untouched.
        const string other = "some plain message text";
        Assert.AreEqual(other, StructuredPayloadFormatter.Reformat(other, Json, PayloadFormat.KeyValueQuoted, PayloadFormat.Json));
        // No-op when from == to.
        Assert.AreEqual("x", StructuredPayloadFormatter.Reformat("x", Json, PayloadFormat.KeyValueQuoted, PayloadFormat.KeyValueQuoted));
    }

    [TestMethod]
    public void EmptyOrNonObject_YieldsEmpty()
    {
        Assert.AreEqual("", StructuredPayloadFormatter.Render("", PayloadFormat.KeyValueQuoted));
        Assert.AreEqual("", StructuredPayloadFormatter.Render("not json", PayloadFormat.KeyValueQuoted));
        Assert.AreEqual("", StructuredPayloadFormatter.Render("[1,2,3]", PayloadFormat.KeyValueQuoted));
    }
}
