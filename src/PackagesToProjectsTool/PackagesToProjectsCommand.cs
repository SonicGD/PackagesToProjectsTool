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
    public string SolutionPath { get; set; } = "";

    [Description("Folders to search projects in")]
    [CommandOption("-f")]
    public string[] ProjectsFolders { get; init; } = Array.Empty<string>();

    public override ValidationResult Validate()
    {
        if (string.IsNullOrEmpty(SolutionPath))
        {
            var slnFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.sln");
            if (slnFiles.Any())
            {
                if (slnFiles.Length == 1)
                {
                    SolutionPath = slnFiles.First();
                }
                else
                {
                    return ValidationResult.Error(
                        $"Multiple sln files found in current directory. Specify solution: packages-to-projects my.sln");
                }
            }
            else
            {
                return ValidationResult.Error(
                    $"No sln files found in current directory. Specify solution: packages-to-projects my.sln");
            }
        }
        else
        {
            SolutionPath = Path.GetFullPath(SolutionPath);
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
