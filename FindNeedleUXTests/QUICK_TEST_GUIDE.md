# Quick Test Execution Guide

## Run All Unit Tests (Recommended First)
```powershell
dotnet test FindNeedleUXTests\FindNeedleUXTests.csproj --filter "ClassName=SearchRulesPageLogicTests"
```

## Run Specific Test
```powershell
# Test that collection notifies when item is added
dotnet test FindNeedleUXTests\FindNeedleUXTests.csproj --filter "Name=RulesCollection_FiresCollectionChanged_OnAdd"

# Test that multiple items can be added
dotnet test FindNeedleUXTests\FindNeedleUXTests.csproj --filter "Name=RuleFiles_WhenMultipleItemsAdded_AllPresent"
```

## Run with Verbose Output
```powershell
dotnet test FindNeedleUXTests\FindNeedleUXTests.csproj --logger "console;verbosity=detailed"
```

## Expected Test Results

If all unit tests pass ?, your ObservableCollection and data binding logic works correctly.

If a test fails ?, check:
1. **Collection logic** - Are items being added to the collection?
2. **Binding** - Is the UI bound to the correct collection?
3. **Validation** - Are file validation errors being captured?

## What Each Test Validates

| Test | Validates |
|------|-----------|
| `RuleFiles_Collection_StartsEmpty` | Initial state is empty |
| `RuleFiles_WhenItemAdded_CollectionSizeIncreases` | Items can be added to collection |
| `RuleFiles_WhenMultipleItemsAdded_AllPresent` | Multiple items can coexist |
| `RulesCollection_FiresCollectionChanged_OnAdd` | **CRITICAL** - UI updates when item added |
| `RulesCollection_FiresCollectionChanged_OnRemove` | UI updates when item removed |
| `RuleFileItem_Sections_CanAddMultipleSections` | Sections are properly stored |
| `ValidRulesJsonFile_CanBeLoaded` | JSON parsing works |

## If Tests Pass But UI Still Doesn't Update

The problem is likely:
1. **XAML binding issue** - Check `SearchRulesPage.xaml` binding syntax
2. **File picker returning null** - Add logging to `BrowseButton_Click`
3. **File path validation failing** - Verify file ends with `.rules.json`

## Next: Add Your Own Tests

```csharp
[TestMethod]
public void MyCustomTest_WhenCondition_ThenResult()
{
    // Arrange
    var myData = new RuleFileItem { FileName = "test.rules.json" };
    
    // Act
    var result = SomeMethod(myData);
    
    // Assert
    Assert.AreEqual(expected, result);
}
```

Then run:
```powershell
dotnet test FindNeedleUXTests --filter "Name=MyCustomTest_WhenCondition_ThenResult"
```
