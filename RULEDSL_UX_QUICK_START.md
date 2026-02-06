# RuleDSL UX - Quick Start Guide

## Accessing the Rules UI

### From the Menu
```
MainWindow
  ? Menu: SearchQuery
    ? Locations
    ? Filters
    ? Rules ? Click here
    ? Processors
    ? Plugins
```

## SearchRulesPage Features

### Rule Files Section (Top)
**Browse and manage rule JSON files**

- **Browse Button** - Opens file picker to select `.rules.json` files
- **Remove Selected** - Removes highlighted file from list
- **File List** - Shows all added rule files with:
  - ? Checkbox to enable/disable file
  - File name and full path
  - ? Green checkmark if valid

### Purpose Filter (Middle Top)
**Filter which types of rules to display**

- **All Purposes** - Show all rule sections
- **Filter** - Show only filter-purpose sections
- **Enrichment** - Show only enrichment-purpose sections
- **UML Diagram** - Show only UML-purpose sections

### Rule Sections (Middle)
**View all available rule sections**

Displays for each section:
- ? Checkbox to enable/disable
- **Name** - Section name
- **Purpose** - What type (Filter, Enrichment, UML)
- **Rules** - Number of rules in section
- **Source File** - Which file it came from

### Buttons (Bottom)
- **Cancel** - Discard changes and go back
- **Apply** - Save rules to query (will be used in next search)

## Example Workflow

### Step 1: Open Rules Page
```
Click: MainWindow Menu ? SearchQuery ? Rules
```

### Step 2: Add Rule Files
```
Click: Browse...
?
Select: FindNeedleRuleDSL/Examples/example-filter-advanced.rules.json
?
(File appears in Rule Files list)
```

### Step 3: View Available Rules
```
Rule Sections shows:
  ? EventLevelFiltering (filter, 3 rules)
  ? EventSourceFiltering (filter, 3 rules)
  ? TaskNameFiltering (filter, 3 rules)
  ... etc
```

### Step 4: Filter by Purpose
```
Click: Purpose Filter dropdown ? "Filter"
?
Displays only sections with purpose="filter"
```

### Step 5: Disable Specific Rules
```
Uncheck: TaskNameFiltering
(Only disable rules you don't want)
```

### Step 6: Apply
```
Click: Apply
?
SearchRulesPage closes
?
Rules are now loaded in NuSearchQuery
```

### Step 7: Run Search
```
Click: Menu ? View Results ? Get
?
Run the search - rules will be applied automatically
?
Results will be filtered/enriched per rules
```

## File Locations

### Example Rule Files
```
FindNeedleRuleDSL/Examples/
?? example-filter-only.rules.json
?? example-enrichment-only.rules.json
?? example-uml-only.rules.json
?? example-combined-pipeline.rules.json
?? example-filter-advanced.rules.json
?? comprehensive-pipeline.rules.json
```

### Creating Your Own Rules
See `FindPluginCore/Searching/RuleDSL/RULEDSL_USAGE.md` for syntax and examples

## Integration with Search Pipeline

### How Rules Get Applied
```
SearchQuery.RunThrough()
  ?
LoadRules()
  (Loads from NuSearchQuery.RulesConfigPaths)
  ?
GetFilteredResults()
  ? Applies filter-purpose rules
  ? Removes results that don't match
  ?
Step3_ResultsToProcessors()
  ? Applies enrichment-purpose rules
  ? Adds tags to results
  ?
ProcessAllResultsToOutput()
  (UML rules would be used for visualization)
```

## Troubleshooting

### No rule files appear
- Check file path is absolute or relative to app directory
- Verify file extension is `.rules.json` (case-sensitive on Linux)
- Check "IsValid" icon - should show green checkmark

### Rules not applied to search
- Make sure rules are enabled (checkbox checked)
- Click "Apply" button (not just "Cancel")
- Verify `NuSearchQuery.RulesConfigPaths` has files

### Section doesn't show up
- Check section has valid JSON with "purpose" field
- Try selecting "All Purposes" instead of specific filter
- Check section has "rules" array (can be empty)

### Invalid file error
- Check JSON syntax is valid (use jsonlint.com)
- Check file is really a .rules.json file
- Look at example files for correct format

## Tips & Tricks

1. **Multiple Rule Files**
   - Add multiple files to compose complex behavior
   - Rules apply in order they were added
   - Each file can have multiple sections

2. **Enable/Disable Without Editing**
   - Uncheck file/section instead of deleting
   - Re-check to re-enable later
   - Changes only apply when you click "Apply"

3. **Filter by Purpose**
   - Use to focus on specific rule types
   - Helpful when you have many rules
   - Doesn't disable sections - just hides them from view

4. **Test Rules**
   - Start with one rule file
   - Test each purpose type separately
   - Build up complexity gradually

## Related Documentation

- **RULEDSL_INTEGRATION_SUMMARY.md** - How RuleDSL integrates with SearchQuery
- **RULEDSL_QUICK_REFERENCE.md** - Field and action type reference
- **RULEDSL_USAGE.md** - Complete rule syntax and examples
- **FindNeedleRuleDSL/Examples/*.rules.json** - Example rule files

---

**Status**: ? Ready to use
**Version**: 1.0
**Last Updated**: [Current Date]
