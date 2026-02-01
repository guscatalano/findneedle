using System.Text.Json;
using FindNeedleUmlDsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedlePluginUtilsTests;

[TestClass]
public class UmlRuleModelTests
{
    [TestMethod]
    public void UmlRule_DefaultValues()
    {
        var rule = new UmlRule();

        Assert.AreEqual(string.Empty, rule.Name);
        Assert.AreEqual(string.Empty, rule.Match);
        Assert.IsNull(rule.Unmatch);
        Assert.IsNotNull(rule.Action);
    }

    [TestMethod]
    public void UmlAction_DefaultValues()
    {
        var action = new UmlAction();

        Assert.AreEqual("message", action.Type);
        Assert.IsNull(action.From);
        Assert.IsNull(action.To);
        Assert.AreEqual(string.Empty, action.Text);
        Assert.AreEqual("solid", action.ArrowStyle);
        Assert.IsNull(action.NotePosition);
    }

    [TestMethod]
    public void UmlParticipant_DefaultValues()
    {
        var participant = new UmlParticipant();

        Assert.AreEqual(string.Empty, participant.Id);
        Assert.IsNull(participant.DisplayName);
        Assert.AreEqual("participant", participant.Type);
    }

    [TestMethod]
    public void UmlRuleDefinition_DefaultValues()
    {
        var definition = new UmlRuleDefinition();

        Assert.IsNull(definition.Title);
        Assert.IsNotNull(definition.Participants);
        Assert.AreEqual(0, definition.Participants.Count);
        Assert.IsNotNull(definition.Rules);
        Assert.AreEqual(0, definition.Rules.Count);
    }

    [TestMethod]
    public void UmlRule_SerializesToJson()
    {
        var rule = new UmlRule
        {
            Name = "TestRule",
            Match = "test pattern",
            Unmatch = "exclude",
            Action = new UmlAction
            {
                Type = "message",
                From = "A",
                To = "B",
                Text = "Test message",
                ArrowStyle = "dashed"
            }
        };

        var json = JsonSerializer.Serialize(rule);

        Assert.IsTrue(json.Contains("\"name\":\"TestRule\""));
        Assert.IsTrue(json.Contains("\"match\":\"test pattern\""));
        Assert.IsTrue(json.Contains("\"unmatch\":\"exclude\""));
        Assert.IsTrue(json.Contains("\"type\":\"message\""));
        Assert.IsTrue(json.Contains("\"from\":\"A\""));
        Assert.IsTrue(json.Contains("\"to\":\"B\""));
    }

    [TestMethod]
    public void UmlRule_DeserializesFromJson()
    {
        var json = """
        {
            "name": "TestRule",
            "match": "test pattern",
            "unmatch": "exclude",
            "action": {
                "type": "message",
                "from": "A",
                "to": "B",
                "text": "Hello",
                "arrowStyle": "async"
            }
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rule = JsonSerializer.Deserialize<UmlRule>(json, options);

        Assert.IsNotNull(rule);
        Assert.AreEqual("TestRule", rule.Name);
        Assert.AreEqual("test pattern", rule.Match);
        Assert.AreEqual("exclude", rule.Unmatch);
        Assert.AreEqual("message", rule.Action.Type);
        Assert.AreEqual("A", rule.Action.From);
        Assert.AreEqual("B", rule.Action.To);
        Assert.AreEqual("Hello", rule.Action.Text);
        Assert.AreEqual("async", rule.Action.ArrowStyle);
    }

    [TestMethod]
    public void UmlRuleDefinition_DeserializesCompleteFile()
    {
        var json = """
        {
            "title": "Session Management",
            "participants": [
                { "id": "LSM", "displayName": "Local Session Manager", "type": "participant" },
                { "id": "User", "type": "actor" }
            ],
            "rules": [
                {
                    "name": "Logon",
                    "match": "user logged on",
                    "action": {
                        "type": "message",
                        "from": "User",
                        "to": "LSM",
                        "text": "Logon request"
                    }
                }
            ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var definition = JsonSerializer.Deserialize<UmlRuleDefinition>(json, options);

        Assert.IsNotNull(definition);
        Assert.AreEqual("Session Management", definition.Title);
        Assert.AreEqual(2, definition.Participants.Count);
        Assert.AreEqual("LSM", definition.Participants[0].Id);
        Assert.AreEqual("Local Session Manager", definition.Participants[0].DisplayName);
        Assert.AreEqual("actor", definition.Participants[1].Type);
        Assert.AreEqual(1, definition.Rules.Count);
        Assert.AreEqual("Logon", definition.Rules[0].Name);
    }

    [TestMethod]
    public void ResolvedUmlElement_DefaultValues()
    {
        var element = new ResolvedUmlElement();

        Assert.AreEqual("message", element.Type);
        Assert.IsNull(element.From);
        Assert.IsNull(element.To);
        Assert.AreEqual(string.Empty, element.Text);
        Assert.AreEqual("solid", element.ArrowStyle);
        Assert.IsNull(element.NotePosition);
        Assert.IsNull(element.Timestamp);
    }

    [TestMethod]
    public void LogMessage_DefaultValues()
    {
        var message = new LogMessage();

        Assert.AreEqual(string.Empty, message.Content);
        Assert.AreEqual(string.Empty, message.Source);
        Assert.IsNull(message.Timestamp);
    }
}
