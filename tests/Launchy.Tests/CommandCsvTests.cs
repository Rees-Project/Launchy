using System.IO;
using Launchy.Models;
using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public class CommandCsvTests
{
    [Fact]
    public void ExportThenImport_RoundTripsAllFields()
    {
        var original = new List<LaunchCommand>
        {
            new() { Command = "notepad", Type = TargetType.Application, Location = "notepad.exe", RunAsAdmin = true },
            new() { Command = "search", Type = TargetType.Website, Location = "https://example.com/?q=a,b", Parameters = "say \"hi\"" },
            new() { Command = "build", Type = TargetType.Script, Location = "", Script = "cd src\r\ndotnet build" }
        };

        var path = Path.GetTempFileName();
        try
        {
            CommandCsv.Export(path, original);
            var imported = CommandCsv.Import(path);

            Assert.Equal(original.Count, imported.Count);
            for (var i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Command, imported[i].Command);
                Assert.Equal(original[i].Type, imported[i].Type);
                Assert.Equal(original[i].Location, imported[i].Location);
                Assert.Equal(original[i].Parameters, imported[i].Parameters);
                Assert.Equal(original[i].Script, imported[i].Script);
                Assert.Equal(original[i].RunAsAdmin, imported[i].RunAsAdmin);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_RemovesDuplicatesWithinFileAndAgainstExisting()
    {
        const string csv =
            "Command,Type,Location,Parameters,Script,RunAsAdmin\r\n" +
            "notepad,Application,notepad.exe,,,False\r\n" +
            "calc,Application,calc.exe,,,False\r\n" +
            "CALC,Application,other.exe,,,False\r\n" +
            "docs,Folder,%USERPROFILE%\\Documents,,,False\r\n";

        var imported = CommandCsv.Read(new StringReader(csv));
        Assert.Equal(["notepad", "calc", "docs"], imported.Select(c => c.Command));
        Assert.Equal("calc.exe", imported[1].Location);

        var existing = new List<LaunchCommand>
        {
            new() { Command = "Notepad", Type = TargetType.Application, Location = "notepad.exe" }
        };
        var added = CommandCsv.NewCommands(existing, imported);
        Assert.Equal(["calc", "docs"], added.Select(c => c.Command));
    }

    [Fact]
    public void Import_InvalidType_Throws()
    {
        const string csv = "Command,Type,Location\r\nfoo,Spaceship,x\r\n";
        Assert.Throws<FormatException>(() => CommandCsv.Read(new StringReader(csv)));
    }
}
