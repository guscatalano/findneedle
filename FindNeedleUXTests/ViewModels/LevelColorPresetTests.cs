using System;
using System.Linq;
using System.Text.RegularExpressions;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Fresh-install sanity for the level-color theme presets: the default theme exists, every preset
/// covers the same set of levels (so switching themes never leaves a level unstyled), every meaningful
/// Level enum value is styled, and every color token is valid. This guards the "level list out of
/// sync with the presets" class of bug.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class LevelColorPresetTests
{
    private static readonly Regex ColorToken = new(@"^(Transparent|#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8}))$", RegexOptions.Compiled);

    [TestMethod]
    public void DefaultTheme_ExistsAndIsTheDefaultLevelColors()
    {
        Assert.IsTrue(NativeResultsPageViewModel.ThemePresets.ContainsKey(NativeResultsPageViewModel.DefaultThemeName),
            "the default theme must be one of the presets");
        CollectionAssert.AreEquivalent(
            NativeResultsPageViewModel.DefaultLevelColors.Keys.ToList(),
            NativeResultsPageViewModel.ThemePresets[NativeResultsPageViewModel.DefaultThemeName].Keys.ToList());
    }

    [TestMethod]
    public void AllPresets_CoverTheSameLevelKeys()
    {
        var reference = NativeResultsPageViewModel.ThemePresets[NativeResultsPageViewModel.DefaultThemeName]
            .Keys.OrderBy(k => k).ToList();
        foreach (var (theme, colors) in NativeResultsPageViewModel.ThemePresets.Select(kv => (kv.Key, kv.Value)))
            CollectionAssert.AreEquivalent(reference, colors.Keys.ToList(),
                $"theme '{theme}' covers a different set of levels than the default — a level would be left unstyled when switching to it");
    }

    [TestMethod]
    public void EveryPreset_KeysExactlyOnTheLevelEnum()
    {
        // The presets must be keyed by exactly the Level enum names — no real level left unstyled, and
        // no decorative non-enum keys (which historically drifted in as "Critical"/"Debug").
        var levelNames = Enum.GetNames(typeof(FindNeedlePluginLib.Level)).OrderBy(n => n).ToList();
        foreach (var (theme, colors) in NativeResultsPageViewModel.ThemePresets.Select(kv => (kv.Key, kv.Value)))
            CollectionAssert.AreEquivalent(levelNames, colors.Keys.ToList(),
                $"theme '{theme}' must be keyed by exactly the Level enum values");
    }

    [TestMethod]
    public void EveryPresetColor_IsAValidToken()
    {
        foreach (var (theme, colors) in NativeResultsPageViewModel.ThemePresets.Select(kv => (kv.Key, kv.Value)))
            foreach (var (level, color) in colors.Select(kv => (kv.Key, kv.Value)))
                Assert.IsTrue(ColorToken.IsMatch(color),
                    $"theme '{theme}' level '{level}' has an invalid color token '{color}'");
    }
}
