using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedleRuleDSL;

public class UnifiedRuleProcessor
{
    private readonly UnifiedRuleSet _ruleSet;
    private readonly string _provider;

    public UnifiedRuleProcessor(UnifiedRuleSet ruleSet, string provider)
    {
        _ruleSet = ruleSet;
        _provider = provider;
    }

    public IEnumerable<(UnifiedRule Rule, object Result, UnifiedRuleAction Action)> Process(IEnumerable<object> results, Func<object, string> getData)
    {
        var enabledSections = _ruleSet.Sections.Where(s => s.Providers.Contains(_provider, StringComparer.OrdinalIgnoreCase));
        foreach (var section in enabledSections)
        {
            foreach (var rule in section.Rules.Where(r => r.Enabled))
            {
                foreach (var result in results)
                {
                    var data = getData(result);
                    if (data.Contains(rule.Match, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(rule.Unmatch) && data.Contains(rule.Unmatch, StringComparison.OrdinalIgnoreCase))
                            continue;
                        yield return (rule, result, rule.Action);
                    }
                }
            }
        }
    }
}
