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
    public void Matches_ReturnsAllPrefixMatchesInListOrder()
    {
        var commands = new List<LaunchCommand>
        {
            new() { Command = "adrenalPM", Location = "a" },
            new() { Command = "notepad", Location = "b" },
            new() { Command = "adrenalgit", Location = "c" },
            new() { Command = "adrenalDocs", Location = "d" }
        };
        Assert.Equal(["adrenalPM", "adrenalgit", "adrenalDocs"], CommandStore.Matches(commands, "ad").Select(c => c.Command));
        Assert.Equal(["adrenalgit"], CommandStore.Matches(commands, "adrenalg").Select(c => c.Command));
        Assert.Empty(CommandStore.Matches(commands, "  "));
    }

    [Fact]
    public void Matches_PutsExactMatchFirst()
    {
        var commands = new List<LaunchCommand>
        {
            new() { Command = "gitlab", Location = "x" },
            new() { Command = "github", Location = "y" },
            new() { Command = "git", Location = "z" }
        };
        Assert.Equal(["git", "gitlab", "github"], CommandStore.Matches(commands, "git").Select(c => c.Command));
    }
}
