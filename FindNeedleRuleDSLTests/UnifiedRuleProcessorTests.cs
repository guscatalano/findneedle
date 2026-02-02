using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

[TestClass]
public class UnifiedRuleProcessorTests
{
    private UnifiedRuleProcessor? _processor;
    private UnifiedRuleSet? _ruleSet;

    [TestInitialize]
    public void Setup()
    {
        _ruleSet = new UnifiedRuleSet
        {
            Title = "Test Rules",
            Sections = new List<UnifiedRuleSection>()
        };
    }

    [TestMethod]
    public void Process_WithMatchingRule_ReturnsMatch()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object> { "This is an error message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual("TestRule", matches[0].Rule.Name);
        Assert.AreEqual("ErrorTag", matches[0].Action.Tag);
    }

    [TestMethod]
    public void Process_WithNonMatchingRule_ReturnsNoMatch()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "crash",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "CrashTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object> { "This is an error message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void Process_WithUnmatchCondition_ExcludesMatch()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Unmatch = "expected",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object> { "This is an expected error message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void Process_WithDisabledRule_IgnoresRule()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Enabled = false,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object> { "This is an error message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void Process_WithProviderMismatch_ReturnsNoMatch()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "EventLog" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "ETW");

        var results = new List<object> { "This is an error message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void Process_CaseInsensitiveMatching_MatchesDifferentCase()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object> { "This is an ERROR message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(1, matches.Count);
    }

    [TestMethod]
    public void Process_MultipleRules_ReturnsAllMatches()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "Rule1",
                    Match = "error",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "Error" }
                },
                new UnifiedRule
                {
                    Name = "Rule2",
                    Match = "warning",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "Warning" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object> { "error and warning" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(2, matches.Count);
        Assert.IsTrue(matches.Any(m => m.Action.Tag == "Error"));
        Assert.IsTrue(matches.Any(m => m.Action.Tag == "Warning"));
    }

    [TestMethod]
    public void Process_ProviderCaseInsensitive_MatchesWithDifferentCase()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "EventLog" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "eventlog");

        var results = new List<object> { "error message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(1, matches.Count);
    }

    [TestMethod]
    public void Process_WithUnmatchContainsMatch_IncludesResult()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Unmatch = "expected",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object> { "This is an error message" };

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(1, matches.Count);
    }

    [TestMethod]
    public void Process_EmptyResults_ReturnsNoMatches()
    {
        // Arrange
        var section = new UnifiedRuleSection
        {
            Name = "TestSection",
            Providers = new List<string> { "TestProvider" },
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "TestRule",
                    Match = "error",
                    Enabled = true,
                    Action = new UnifiedRuleAction { Type = "tag", Tag = "ErrorTag" }
                }
            }
        };
        _ruleSet.Sections.Add(section);

        _processor = new UnifiedRuleProcessor(_ruleSet, "TestProvider");

        var results = new List<object>();

        // Act
        var matches = _processor.Process(results, obj => obj.ToString() ?? string.Empty).ToList();

        // Assert
        Assert.AreEqual(0, matches.Count);
    }
}
