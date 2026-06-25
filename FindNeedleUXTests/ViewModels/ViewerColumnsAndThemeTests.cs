using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for two display-side view-model behaviors: <see cref="NativeResultsPageViewModel.ApplyTheme"/>
/// (copy a level-color preset onto the Levels entries) and
/// <see cref="NativeResultsPageViewModel.AutoHideEmptyColumnsFromSample"/> (hide a column that's wholly
/// empty across a sample).
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ViewerColumnsAndThemeTests
{
    [TestMethod]
    public void ApplyTheme_SetsLevelColorsFromPreset()
    {
        var vm = new NativeResultsPageViewModel();
        vm.Levels.Add(new LevelEntry { Level = "Error" });
        vm.Levels.Add(new LevelEntry { Level = "Unknown" });

        vm.ApplyTheme("Subtle");

        Assert.AreEqual(NativeResultsPageViewModel.ThemePresets["Subtle"]["Error"], vm.Levels[0].HexColor);
        Assert.AreEqual("Transparent", vm.Levels[1].HexColor); // Unknown is untinted in every preset
    }

    [TestMethod]
    public void ApplyTheme_UnknownThemeName_LeavesColorsUnchanged()
    {
        var vm = new NativeResultsPageViewModel();
        vm.Levels.Add(new LevelEntry { Level = "Error", HexColor = "#11223344" });
        vm.ApplyTheme("NoSuchTheme");
        Assert.AreEqual("#11223344", vm.Levels[0].HexColor);
    }

    [TestMethod]
    public void ApplyTheme_LevelNotInPreset_FallsBackToTransparent()
    {
        var vm = new NativeResultsPageViewModel();
        vm.Levels.Add(new LevelEntry { Level = "MadeUpLevel", HexColor = "#FFFFFFFF" });
        vm.ApplyTheme("Subtle");
        Assert.AreEqual("Transparent", vm.Levels[0].HexColor);
    }

    [TestMethod]
    public void AutoHide_HidesWhollyEmptyColumns_KeepsPopulatedOnes()
    {
        var vm = new NativeResultsPageViewModel(); // Columns seeded from defaults, all visible
        var sample = new List<LogLine>
        {
            new LogLine(new R(source: "", task: "", resultSource: "", message: "hello"), 0),
            new LogLine(new R(source: "", task: "", resultSource: "", message: "world"), 1),
        };

        vm.AutoHideEmptyColumnsFromSample(sample);

        Assert.IsFalse(vm.Columns.First(c => c.Name == "Provider").IsVisible, "empty Provider should be hidden");
        Assert.IsFalse(vm.Columns.First(c => c.Name == "TaskName").IsVisible, "empty TaskName should be hidden");
        Assert.IsTrue(vm.Columns.First(c => c.Name == "Message").IsVisible, "populated Message stays visible");
    }

    [TestMethod]
    public void AutoHide_EmptySample_NoChange()
    {
        var vm = new NativeResultsPageViewModel();
        vm.AutoHideEmptyColumnsFromSample(new List<LogLine>());
        Assert.IsTrue(vm.Columns.First(c => c.Name == "Provider").IsVisible);
    }

    private sealed class R : ISearchResult
    {
        private readonly string _source, _task, _resultSource, _message;
        public R(string source, string task, string resultSource, string message)
        { _source = source; _task = task; _resultSource = resultSource; _message = message; }
        public DateTime GetLogTime() => DateTime.MinValue;
        public string GetMachineName() => "";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "";
        public string GetTaskName() => _task;
        public string GetOpCode() => "";
        public string GetSource() => _source;
        public string GetSearchableData() => _message;
        public string GetMessage() => _message;
        public string GetResultSource() => _resultSource;
    }
}
