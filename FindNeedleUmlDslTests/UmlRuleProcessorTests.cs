using FindNeedleUmlDsl;
using FindNeedleUmlDsl.MermaidUML;
using FindNeedleUmlDsl.PlantUML;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUmlDslTests;

[TestClass]
public class UmlRuleProcessorTests
{
    [TestMethod]
    public void LoadRulesFromJson_ValidJson_ParsesCorrectly()
    {
        var json = """
        {
            "title": "Test",
            "participants": [
                { "id": "A", "displayName": "Actor A", "type": "participant" }
            ],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "hello",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "World"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "hello world" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("A -> B : World"));
    }

    [TestMethod]
    public void ProcessMessages_NoMatch_ProducesEmptyDiagram()
    {
        var json = """
        {
            "title": "Test",
            "participants": [],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "hello",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "World"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "goodbye world" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("@startuml"));
        Assert.IsTrue(output.Contains("@enduml"));
        Assert.IsFalse(output.Contains("A -> B"));
    }

    [TestMethod]
    public void ProcessMessages_WithUnmatch_ExcludesMatches()
    {
        var json = """
        {
            "title": "Test",
            "participants": [],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "hello",
                    "unmatch": "exclude",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "World"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "hello exclude me" },
            new() { Content = "hello include me" }
        };

        var output = processor.ProcessMessages(messages);

        // Should only have one message (the one without "exclude")
        var messageCount = output.Split("A -> B").Length - 1;
        Assert.AreEqual(1, messageCount);
    }

    [TestMethod]
    public void ProcessMessages_Placeholder_AfterMatch()
    {
        var json = """
        {
            "title": "Test",
            "participants": [],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "SessionId=",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "Session {afterMatch}"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "Connected to SessionId=12345" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("Session 12345"));
    }

    [TestMethod]
    public void ProcessMessages_Placeholder_AfterMatchUntilSpace()
    {
        var json = """
        {
            "title": "Test",
            "participants": [],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "SessionId=",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "Session {afterMatch:untilSpace}"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "Connected to SessionId=12345 with user" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("Session 12345"));
        Assert.IsFalse(output.Contains("with user"));
    }

    [TestMethod]
    public void ProcessMessages_Placeholder_AfterMatchUntilChar()
    {
        var json = """
        {
            "title": "Test",
            "participants": [],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "Time=",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "Took {afterMatch:until:,} ms"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "perf=Time=500, SessionId=1" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("Took 500 ms"));
    }

    [TestMethod]
    public void ProcessMessages_Placeholder_BeforeMatch()
    {
        var json = """
        {
            "title": "Test",
            "participants": [],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "connected",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "Prefix: {beforeMatch}"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "User connected" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("Prefix: User "));
    }

    [TestMethod]
    public void ProcessMessages_MultipleRulesMatch()
    {
        var json = """
        {
            "title": "Test",
            "participants": [],
            "rules": [
                {
                    "name": "Rule1",
                    "match": "start",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "Started"
                    }
                },
                {
                    "name": "Rule2",
                    "match": "end",
                    "action": {
                        "type": "message",
                        "from": "B",
                        "to": "A",
                        "text": "Ended"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "operation start" },
            new() { Content = "operation end" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("A -> B : Started"));
        Assert.IsTrue(output.Contains("B -> A : Ended"));
    }

    [TestMethod]
    public void ProcessMessages_WithMermaidTranslator_ProducesMermaidSyntax()
    {
        var json = """
        {
            "title": "Test",
            "participants": [
                { "id": "A", "type": "participant" }
            ],
            "rules": [
                {
                    "name": "TestRule",
                    "match": "hello",
                    "action": {
                        "type": "message",
                        "from": "A",
                        "to": "B",
                        "text": "World"
                    }
                }
            ]
        }
        """;

        var processor = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        processor.LoadRulesFromJson(json);

        var messages = new List<LogMessage>
        {
            new() { Content = "hello world" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("sequenceDiagram"));
        Assert.IsTrue(output.Contains("A->>B: World"));
    }

    [TestMethod]
    public void LoadRules_FromDefinitionObject_Works()
    {
        var rule = new UmlRule
        {
            Name = "TestRule",
            Match = "test",
            Action = new UmlAction
            {
                Type = "message",
                From = "A",
                To = "B",
                Text = "Test message"
            }
        };

        var definition = new UmlRuleDefinition
        {
            Title = "Test",
            Participants = new List<UmlParticipant>
            {
                new() { Id = "A", Type = "participant" }
            },
            Rules = new List<UmlRule> { rule }
        };

        var processor = new UmlRuleProcessor(new PlantUmlSyntaxTranslator());
        processor.LoadRules(definition);

        var messages = new List<LogMessage>
        {
            new() { Content = "this is a test" }
        };

        var output = processor.ProcessMessages(messages);

        Assert.IsTrue(output.Contains("A -> B : Test message"));
    }
}
