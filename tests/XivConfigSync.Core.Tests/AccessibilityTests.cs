using XivConfigSync.Core.Rules;
using XivConfigSync.Core.ViewModels;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>
/// A screen reader announces a combo box item by its text form. Records gave their full form,
/// device IDs included, found in the interface test of 2026-11-05.
/// </summary>
public sealed class AccessibilityTests
{
    [Fact]
    public void ComboBoxItemsAnnounceTheirNameOnly()
    {
        Assert.Equal("PC 2", new SourceOption("f3d34a6e432c436d9b3ee7fba32f0387", "PC 2").ToString());
        Assert.Equal("Latest backup", new RevisionOption("20261105T093411814Z-b1a68aa1", "Latest backup").ToString());
        Assert.Equal("PC 2", new ExistingDevice("f3d34a6e432c436d9b3ee7fba32f0387", "PC 2", DateTimeOffset.UnixEpoch).ToString());
        Assert.Equal("Always", new RuleChoice(ApplyRule.Always, "Always").ToString());
        Assert.Equal("Deutsch", new LanguageChoice("de", "Deutsch").ToString());
    }
}
