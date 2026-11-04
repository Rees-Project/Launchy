using Launchy.Models;
using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public class CommandStoreFindTests
{
    private static readonly List<LaunchCommand> Commands =
    [
        new() { Command = "adrenalPM", Type = TargetType.Folder, Location = @"C:\pm" },
        new() { Command = "adrenalgit", Type = TargetType.Website, Location = "https://github.com" },
        new() { Command = "notepad", Type = TargetType.Application, Location = "notepad.exe" }
    ];

    [Theory]
    [InlineData("ad")]
    [InlineData("adr")]
    [InlineData("ADRENAL")]
    [InlineData("  adrenal  ")]
    public void Find_TwoOrMoreChars_ReturnsFirstPrefixMatch(string text)
    {
        Assert.Equal("adrenalPM", CommandStore.Find(Commands, text)?.Command);
    }

    [Fact]
    public void Find_DistinguishingLetter_ReturnsThatCommand()
    {
        Assert.Equal("adrenalgit", CommandStore.Find(Commands, "adrenalg")?.Command);
    }

    [Fact]
    public void Find_ExactMatch_WinsOverEarlierPrefixMatch()
    {
        var commands = new List<LaunchCommand>
        {
            new() { Command = "gitlab", Location = "x" },
            new() { Command = "git", Location = "y" }
        };
        Assert.Equal("git", CommandStore.Find(commands, "git")?.Command);
    }

    [Fact]
    public void Find_SingleChar_RequiresUniqueMatch()
    {
        Assert.Null(CommandStore.Find(Commands, "a"));
        Assert.Equal("notepad", CommandStore.Find(Commands, "n")?.Command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zz")]
    public void Find_NoMatch_ReturnsNull(string text)
    {
        Assert.Null(CommandStore.Find(Commands, text));
    }

    [Fact]
    public void CountPrefixMatches_CountsAllCommandsStartingWithText()
    {
        Assert.Equal(2, CommandStore.CountPrefixMatches(Commands, "ad"));
        Assert.Equal(1, CommandStore.CountPrefixMatches(Commands, "adrenalg"));
        Assert.Equal(0, CommandStore.CountPrefixMatches(Commands, ""));
    }
}
