using System;
using System.Text.Json;
using FindNeedleUX.Pages;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the web viewer's DataTables-request → query-spec translators (TESTING_PLAN.md U-D).
/// <see cref="ResultsWebPage.BuildFilterSpec"/> / <see cref="ResultsWebPage.BuildSortSpec"/> are
/// pure (JsonElement → FilterSpec/SortSpec), so they're testable without WebView2. This pins the
/// column-index mapping, the Level regex-anchor stripping, time parsing, and sort direction —
/// exactly the bits that silently break when the JS column order or ajax payload changes.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ResultsWebPageSpecTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    // Columns: 0 Index, 1 Time, 2 Provider, 3 TaskName, 4 Message, 5 Source, 6 Level, 7 More.
    private static string Cols(string provider = "", string taskName = "", string message = "",
                              string source = "", string level = "")
        => $@"[
            {{ ""search"": {{ ""value"": """" }} }},
            {{ ""search"": {{ ""value"": """" }} }},
            {{ ""search"": {{ ""value"": ""{provider}"" }} }},
            {{ ""search"": {{ ""value"": ""{taskName}"" }} }},
            {{ ""search"": {{ ""value"": ""{message}"" }} }},
            {{ ""search"": {{ ""value"": ""{source}"" }} }},
            {{ ""search"": {{ ""value"": ""{level}"" }} }},
            {{ ""search"": {{ ""value"": """" }} }}
        ]";

    [TestMethod]
    public void BuildFilterSpec_GlobalAndColumnSearches_MapToRightFields()
    {
        var cols = Cols(provider: "svcA", message: "failed");
        var req = Json($@"{{ ""search"": {{ ""value"": ""boom"" }}, ""columns"": {cols} }}");

        var f = ResultsWebPage.BuildFilterSpec(req);

        Assert.AreEqual("boom", f.Search);
        Assert.AreEqual("svcA", f.Provider);
        Assert.AreEqual("failed", f.Message);
        Assert.AreEqual("", f.TaskName);
        Assert.AreEqual("", f.Source);
        Assert.AreEqual("", f.Level);
    }

    [TestMethod]
    public void BuildFilterSpec_LevelDropdown_StripsRegexAnchors()
    {
        var cols = Cols(level: "^Error$");
        var req = Json($@"{{ ""columns"": {cols} }}");

        var f = ResultsWebPage.BuildFilterSpec(req);

        Assert.AreEqual("Error", f.Level, "the ^...$ anchors DataTables adds for a dropdown filter should be stripped");
    }

    [TestMethod]
    public void BuildFilterSpec_ParsesTimeRange()
    {
        var req = Json(@"{ ""timeFrom"": ""2020-01-01T00:00:00Z"", ""timeTo"": ""2020-01-02T00:00:00Z"" }");

        var f = ResultsWebPage.BuildFilterSpec(req);

        Assert.IsTrue(f.FromTime.HasValue && f.ToTime.HasValue);
        Assert.AreEqual(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), f.FromTime!.Value.ToUniversalTime());
        Assert.AreEqual(new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), f.ToTime!.Value.ToUniversalTime());
    }

    [TestMethod]
    public void BuildFilterSpec_EmptyRequest_IsEmptySpec()
    {
        var f = ResultsWebPage.BuildFilterSpec(Json("{}"));
        Assert.IsTrue(f.IsEmpty);
    }

    [TestMethod]
    public void BuildSortSpec_OrderColumnDesc_MapsToColumnNameAndDescending()
    {
        var req = Json(@"{ ""order"": [ { ""column"": 1, ""dir"": ""desc"" } ] }");

        var s = ResultsWebPage.BuildSortSpec(req);

        Assert.AreEqual("Time", s.Column);
        Assert.IsTrue(s.Descending);
        Assert.IsTrue(s.IsSorted);
    }

    [TestMethod]
    public void BuildSortSpec_AscendingDefault_WhenDirOmitted()
    {
        var req = Json(@"{ ""order"": [ { ""column"": 4 } ] }");

        var s = ResultsWebPage.BuildSortSpec(req);

        Assert.AreEqual("Message", s.Column);
        Assert.IsFalse(s.Descending);
    }

    [TestMethod]
    public void BuildSortSpec_NoOrder_ReturnsNone()
    {
        Assert.AreSame(SortSpec.None, ResultsWebPage.BuildSortSpec(Json("{}")));
    }

    [TestMethod]
    public void BuildSortSpec_UnknownColumnIndex_ReturnsNone()
    {
        var req = Json(@"{ ""order"": [ { ""column"": 7, ""dir"": ""asc"" } ] }"); // 7 = "More", not sortable
        Assert.AreSame(SortSpec.None, ResultsWebPage.BuildSortSpec(req));
    }
}
