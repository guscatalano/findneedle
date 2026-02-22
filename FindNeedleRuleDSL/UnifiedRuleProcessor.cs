using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
                    var data = getData(result) ?? string.Empty;

                    bool isMatch = false;
                    // Try regex match first (allow rules like "ERROR|CRITICAL"). Fall back to substring if regex invalid.
                    if (!string.IsNullOrEmpty(rule.Match))
                    {
                        try
                        {
                            isMatch = Regex.IsMatch(data, rule.Match, RegexOptions.IgnoreCase);
                        }
                        catch (Exception)
                        {
                            isMatch = data.IndexOf(rule.Match, StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                    }

                    if (!isMatch)
                        continue;

                    if (!string.IsNullOrEmpty(rule.Unmatch))
                    {
                        bool isUnmatch = false;
                        try
                        {
                            isUnmatch = Regex.IsMatch(data, rule.Unmatch, RegexOptions.IgnoreCase);
                        }
                        catch (Exception)
                        {
                            isUnmatch = data.IndexOf(rule.Unmatch, StringComparison.OrdinalIgnoreCase) >= 0;
                        }

                        if (isUnmatch)
                            continue;
                    }

                    yield return (rule, result, rule.Action);
                }
            }
        }
    }
}
