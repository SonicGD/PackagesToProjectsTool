using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SonicGD.PackagesToProjectsTool;

public class PackagesToProjectsCommand : AsyncCommand<PackagesToProjectsCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PackagesToProjectsCommandSettings settings)
    {
        var switcherContext = new SwitcherContext
        {
            SolutionPath = settings.SolutionPath, ProjectsFolders = settings.ProjectsFolders.ToList()
        };
        var switcher = new Switcher(switcherContext);
        await switcher.SwitchAsync();
        return 0;
    }
}

public class PackagesToProjectsCommandSettings : CommandSettings
{
    [Description("Path to solution")]
    [CommandArgument(0, "[path]")]
    public string SolutionPath { get; init; } = "";

    [Description("Folders to search projects in")]
    [CommandOption("-f")]
    public string[] ProjectsFolders { get; init; } = Array.Empty<string>();

    public override ValidationResult Validate()
    {
        if (string.IsNullOrEmpty(SolutionPath))
        {
            return ValidationResult.Error("Provide path to solution");
        }

        if (!File.Exists(SolutionPath))
        {
            return ValidationResult.Error($"Solution {SolutionPath} doesn't exists");
        }

        if (!ProjectsFolders.Any())
        {
            return ValidationResult.Error("Empty projects folders list. Provide at least one");
        }

        var nonExistingFolder = ProjectsFolders.Where(s => !Directory.Exists(s)).ToList();
        if (nonExistingFolder.Any())
        {
            return ValidationResult.Error(
                $"This projects folders doesn't exists: {string.Join(", ", nonExistingFolder)}");
        }

        return ValidationResult.Success();
    }
}
