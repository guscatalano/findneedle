using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

[TestClass]
public class FindNeedleRuleDSLPluginTests
{
    private FindNeedleRuleDSLPlugin? _processor;
    private List<ISearchResult> _testResults = new();
    private string? _tempRulesFile;

    [TestInitialize]
    public void Setup()
    {
        _processor = null;
        _testResults = new();
        _tempRulesFile = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_tempRulesFile != null && File.Exists(_tempRulesFile))
        {
            try
            {
                File.Delete(_tempRulesFile);
            }
            catch { }
        }
    }

    private void CreateTestRulesFile(string rulesJson)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FindNeedleRuleDSLTests");
        Directory.CreateDirectory(tempDir);
        _tempRulesFile = Path.Combine(tempDir, $"test-rules-{Guid.NewGuid()}.json");
        File.WriteAllText(_tempRulesFile, rulesJson);
    }

    private FakeSearchResult CreateFakeResult(string searchableData)
    {
        var result = new FakeSearchResult { searchableDataString = searchableData };
        _testResults.Add(result);
        return result;
    }

    [TestMethod]
    public void GetFriendlyName_ReturnsValidName()
    {
        _processor = new FindNeedleRuleDSLPlugin();
        var name = _processor.GetFriendlyName();
        Assert.AreEqual("FindNeedle Rule DSL Processor", name);
    }

    [TestMethod]
    public void GetClassName_ReturnsFullyQualifiedName()
    {
        _processor = new FindNeedleRuleDSLPlugin();
        var className = _processor.GetClassName();
        Assert.IsTrue(className.Contains("FindNeedleRuleDSLPlugin"));
    }

    [TestMethod]
    public void GetDescription_ReturnsMeaningfulDescription()
    {
        _processor = new FindNeedleRuleDSLPlugin();
        var description = _processor.GetDescription();
        Assert.IsTrue(description.Contains("DSL") || description.Contains("rules"));
    }

    [TestMethod]
    public void ProcessResults_WithEmptyResults_ReturnsZeroMatches()
    {
        var rulesJson = @"{
            ""title"": ""Test Rules"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""Test""],
                ""rules"": [{
                    ""name"": ""TestRule"",
                    ""match"": ""crash"",
                    ""enabled"": true,
                    ""action"": { ""type"": ""tag"", ""tag"": ""Crash"" }
                }]
            }]
        }";
        
        CreateTestRulesFile(rulesJson);
        _processor = new FindNeedleRuleDSLPlugin("Test", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(0, _processor.GetFoundTags().Count());
    }

    [TestMethod]
    public void ProcessResults_BasicCrashDetection_FindsSingleMatch()
    {
        var rulesJson = @"{
            ""title"": ""Crash Rules"",
            ""sections"": [{
                ""name"": ""Crashes"",
                ""providers"": [""EventLog""],
                ""rules"": [{
                    ""name"": ""DotNetCrash"",
                    ""match"": ""A .NET application failed"",
                    ""enabled"": true,
                    ""action"": { ""type"": ""tag"", ""tag"": ""DotNetCrash"" }
                }]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("Application encountered error: A .NET application failed.");
        
        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(1, _processor.GetTagCount("DotNetCrash"));
    }

    [TestMethod]
    public void ProcessResults_MultipleMatches_CountsCorrectly()
    {
        var rulesJson = @"{
            ""title"": ""Crash Rules"",
            ""sections"": [{
                ""name"": ""Crashes"",
                ""providers"": [""EventLog""],
                ""rules"": [
                    {
                        ""name"": ""DotNetCrash"",
                        ""match"": ""A .NET application failed"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""DotNetCrash"" }
                    },
                    {
                        ""name"": ""Hang"",
                        ""match"": ""Application Hang"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""Hang"" }
                    }
                ]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("A .NET application failed in process xyz");
        CreateFakeResult("Application Hang detected in process abc");
        CreateFakeResult("A .NET application failed again in process def");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(2, _processor.GetTagCount("DotNetCrash"));
        Assert.AreEqual(1, _processor.GetTagCount("Hang"));
    }

    [TestMethod]
    public void ProcessResults_CaseInsensitiveMatching()
    {
        var rulesJson = @"{
            ""title"": ""Case Test"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""EventLog""],
                ""rules"": [{
                    ""name"": ""TestRule"",
                    ""match"": ""error"",
                    ""enabled"": true,
                    ""action"": { ""type"": ""tag"", ""tag"": ""Error"" }
                }]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("Critical ERROR found");
        CreateFakeResult("An Error occurred");
        CreateFakeResult("ERROR: something bad");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(3, _processor.GetTagCount("Error"));
    }

    [TestMethod]
    public void ProcessResults_UnmatchCondition_ExcludesMatches()
    {
        var rulesJson = @"{
            ""title"": ""Unmatch Test"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""EventLog""],
                ""rules"": [{
                    ""name"": ""AccessViolation"",
                    ""match"": ""access violation"",
                    ""unmatch"": ""allowed"",
                    ""enabled"": true,
                    ""action"": { ""type"": ""tag"", ""tag"": ""AccessViolation"" }
                }]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("access violation occurred"); // Should match
        CreateFakeResult("access violation but allowed"); // Should not match
        CreateFakeResult("Another access violation error"); // Should match

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(2, _processor.GetTagCount("AccessViolation"));
    }

    [TestMethod]
    public void ProcessResults_DisabledRules_AreIgnored()
    {
        var rulesJson = @"{
            ""title"": ""Disabled Test"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""EventLog""],
                ""rules"": [
                    {
                        ""name"": ""EnabledRule"",
                        ""match"": ""crash"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""Crash"" }
                    },
                    {
                        ""name"": ""DisabledRule"",
                        ""match"": ""error"",
                        ""enabled"": false,
                        ""action"": { ""type"": ""tag"", ""tag"": ""Error"" }
                    }
                ]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("System crash detected");
        CreateFakeResult("Error in application");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(1, _processor.GetTagCount("Crash"));
        Assert.AreEqual(0, _processor.GetTagCount("Error"));
    }

    [TestMethod]
    public void ProcessResults_ProviderFilter_OnlyProcessesRelevantSections()
    {
        var rulesJson = @"{
            ""title"": ""Provider Test"",
            ""sections"": [
                {
                    ""name"": ""EventLog Rules"",
                    ""providers"": [""EventLog""],
                    ""rules"": [{
                        ""name"": ""EventLogRule"",
                        ""match"": ""crash"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""EventLogCrash"" }
                    }]
                },
                {
                    ""name"": ""ETW Rules"",
                    ""providers"": [""ETW""],
                    ""rules"": [{
                        ""name"": ""ETWRule"",
                        ""match"": ""crash"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""ETWCrash"" }
                    }]
                }
            ]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("Application crash occurred");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(1, _processor.GetTagCount("EventLogCrash"));
        Assert.AreEqual(0, _processor.GetTagCount("ETWCrash"));
    }

    [TestMethod]
    public void GetOutputText_IncludesResultCount()
    {
        var rulesJson = @"{
            ""title"": ""Test"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""EventLog""],
                ""rules"": [{
                    ""name"": ""Rule"",
                    ""match"": ""error"",
                    ""enabled"": true,
                    ""action"": { ""type"": ""tag"", ""tag"": ""Error"" }
                }]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("An error occurred");
        CreateFakeResult("Another error occurred");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        var output = _processor.GetOutputText();
        Assert.IsTrue(output.Contains("2") || output.Contains("Processed"));
    }

    [TestMethod]
    public void GetFoundTags_ReturnsAllDiscoveredTags()
    {
        var rulesJson = @"{
            ""title"": ""Test"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""EventLog""],
                ""rules"": [
                    {
                        ""name"": ""Rule1"",
                        ""match"": ""crash"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""Crash"" }
                    },
                    {
                        ""name"": ""Rule2"",
                        ""match"": ""hang"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""Hang"" }
                    },
                    {
                        ""name"": ""Rule3"",
                        ""match"": ""freeze"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""Freeze"" }
                    }
                ]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("crash");
        CreateFakeResult("hang");
        CreateFakeResult("freeze");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        var tags = _processor.GetFoundTags().ToList();
        Assert.AreEqual(3, tags.Count);
        Assert.IsTrue(tags.Contains("Crash"));
        Assert.IsTrue(tags.Contains("Hang"));
        Assert.IsTrue(tags.Contains("Freeze"));
    }

    [TestMethod]
    public void GetMatchedResults_ReturnsOnlyMatchingResults()
    {
        var rulesJson = @"{
            ""title"": ""Test"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""EventLog""],
                ""rules"": [{
                    ""name"": ""Rule"",
                    ""match"": ""crash"",
                    ""enabled"": true,
                    ""action"": { ""type"": ""tag"", ""tag"": ""Crash"" }
                }]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("Application crash");
        CreateFakeResult("Normal event");
        CreateFakeResult("Another crash event");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        var matchedResults = _processor.GetMatchedResults().ToList();
        Assert.AreEqual(2, matchedResults.Count);
    }

    [TestMethod]
    public void ProcessResults_WithoutRulesFile_HandlesGracefully()
    {
        _processor = new FindNeedleRuleDSLPlugin("EventLog", "/nonexistent/path/rules.json");
        CreateFakeResult("crash");
        
        // Should not throw
        _processor.ProcessResults(_testResults);
        
        Assert.AreEqual(0, _processor.GetFoundTags().Count());
    }

    [TestMethod]
    public void ProcessResults_ComplexRuleSet_CrashDetection()
    {
        var rulesJson = @"{
            ""title"": ""Comprehensive Crash Detection"",
            ""sections"": [{
                ""name"": ""CrashTypes"",
                ""providers"": [""EventLog""],
                ""rules"": [
                    {
                        ""name"": ""DotNetCrash"",
                        ""match"": ""A .NET application failed"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""DotNetCrash"" }
                    },
                    {
                        ""name"": ""OutOfMemory"",
                        ""match"": ""OutOfMemoryException"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""OutOfMemory"" }
                    },
                    {
                        ""name"": ""StackOverflow"",
                        ""match"": ""StackOverflowException"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""StackOverflow"" }
                    },
                    {
                        ""name"": ""ApplicationHang"",
                        ""match"": ""Application Hang"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""ApplicationHang"" }
                    }
                ]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("A .NET application failed in module test.dll");
        CreateFakeResult("OutOfMemoryException thrown");
        CreateFakeResult("StackOverflowException in recursive call");
        CreateFakeResult("Application Hang timeout");
        CreateFakeResult("Normal operation");

        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        Assert.AreEqual(1, _processor.GetTagCount("DotNetCrash"));
        Assert.AreEqual(1, _processor.GetTagCount("OutOfMemory"));
        Assert.AreEqual(1, _processor.GetTagCount("StackOverflow"));
        Assert.AreEqual(1, _processor.GetTagCount("ApplicationHang"));
    }

    [TestMethod]
    public void ProcessResults_RulesCanBeProcessedMultipleTimes()
    {
        var rulesJson = @"{
            ""title"": ""Reusable Rules"",
            ""sections"": [{
                ""name"": ""Test"",
                ""providers"": [""EventLog""],
                ""rules"": [{
                    ""name"": ""Rule"",
                    ""match"": ""error"",
                    ""enabled"": true,
                    ""action"": { ""type"": ""tag"", ""tag"": ""Error"" }
                }]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);

        // First processing
        var results1 = new List<ISearchResult> { CreateFakeResult("error 1") };
        _processor.ProcessResults(results1);
        Assert.AreEqual(1, _processor.GetTagCount("Error"));

        // Second processing should clear previous results
        _testResults.Clear();
        var results2 = new List<ISearchResult> { CreateFakeResult("error 1"), CreateFakeResult("error 2") };
        _processor.ProcessResults(results2);
        Assert.AreEqual(2, _processor.GetTagCount("Error"));
    }

    [TestMethod]
    public void Comparison_WithWatsonPlugin_Functionality()
    {
        // This test shows how FindNeedleRuleDSLPlugin can replicate Watson functionality
        var rulesJson = @"{
            ""title"": ""Watson Replacement Rules"",
            ""sections"": [{
                ""name"": ""WatsonRules"",
                ""providers"": [""EventLog""],
                ""rules"": [
                    {
                        ""name"": ""DotNetCrash"",
                        ""match"": ""A .NET application failed"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""DotNetCrash"" }
                    },
                    {
                        ""name"": ""ApplicationHang"",
                        ""match"": ""Application Hang"",
                        ""enabled"": true,
                        ""action"": { ""type"": ""tag"", ""tag"": ""ApplicationHang"" }
                    }
                ]
            }]
        }";

        CreateTestRulesFile(rulesJson);
        CreateFakeResult("oh no A .NET application failed. oh no");
        
        _processor = new FindNeedleRuleDSLPlugin("EventLog", _tempRulesFile);
        _processor.ProcessResults(_testResults);

        // Should detect the same crash as WatsonCrashProcessor
        Assert.AreEqual(1, _processor.GetTagCount("DotNetCrash"));
        Assert.IsTrue(_processor.GetOutputText().Contains("Found 1"));
    }
}
