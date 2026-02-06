# SearchRulesPage UI Tests

This project contains automated tests for the `SearchRulesPage` component using two approaches:

## Test Types

### 1. **Unit Tests** (Recommended - Runs Immediately)
Located in: `SearchRulesPageLogicTests.cs`

These tests verify the core logic without requiring UI automation:
- Collection behavior (ObservableCollection)
- Data binding verification
- JSON parsing logic
- RuleFileItem and RuleSectionItem creation

**Run these tests:**
```
dotnet test FindNeedleUXTests --filter "ClassName=SearchRulesPageLogicTests"
```

### 2. **UI Automation Tests** (Advanced - Requires WinAppDriver)
Located in: `SearchRulesPageUITests.cs`

These tests use WinAppDriver to automate actual UI interactions:
- Browse button exists and is clickable
- ListBox displays correctly
- File picker integration
- Button visibility and state

**Prerequisites for UI tests:**
1. Install WinAppDriver from: https://github.com/Microsoft/WinAppDriver/releases
2. Start WinAppDriver:
   ```
   WinAppDriver.exe
   ```
3. Package the FindNeedleUX app or configure it for testing
4. Update the `AppId` in `SearchRulesPageUITests.cs` with your actual app ID

**Run UI tests:**
```
dotnet test FindNeedleUXTests --filter "ClassName=SearchRulesPageUITests"
```

## Test Data

Sample `.rules.json` files are provided in `TestData/`:
- `valid-test.rules.json` - Complete valid rules file with sections
- `minimal.rules.json` - Minimal valid rules file

These are used by both unit and UI tests.

## How to Use These Tests to Catch Issues

### **Issue: File picker doesn't show**
Run the unit tests first:
```
dotnet test FindNeedleUXTests --filter "ClassName=SearchRulesPageLogicTests"
```
If these pass, the problem is in the WinUI 3 file picker initialization, not the logic.

### **Issue: Selected file doesn't appear in ListBox**
1. Check unit tests for collection behavior
2. Add a test data file and manually verify it loads
3. Run: `dotnet test FindNeedleUXTests --filter "TestName=RulesCollection_FiresCollectionChanged_OnAdd"`

### **Issue: UI elements missing**
Run the UI tests to detect missing controls:
```
dotnet test FindNeedleUXTests --filter "ClassName=SearchRulesPageUITests"
```

## Recommended Approach

1. **Start with unit tests** - They run immediately without WinAppDriver
2. **Use unit tests to verify logic** - Test collection changes, JSON parsing, etc.
3. **Setup WinAppDriver later** - Only when you need to test actual UI interactions
4. **Add more tests as you find issues** - Each test documents expected behavior

## Example: Testing File Addition

To verify that adding a file works:

```csharp
[TestMethod]
public void AddRuleFile_WithValidJson_UpdatesCollectionAndUI()
{
    // This would require WinAppDriver to simulate file picker
    // For now, test the underlying logic with unit tests
}
```

Then manually test by:
1. Running the app
2. Clicking Browse
3. Selecting a `.rules.json` file
4. Verifying it appears in the ListBox

## Next Steps

If the file picker issue persists after these tests:
1. Check the Debug output for error messages
2. Verify the window handle is being obtained correctly
3. Consider refactoring with dependency injection (Option 4 from earlier)
