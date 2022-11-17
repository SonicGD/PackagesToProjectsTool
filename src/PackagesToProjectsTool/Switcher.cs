using CliWrap;

namespace SonicGD.PackagesToProjectsTool;

public class Switcher
{
    private readonly SwitcherContext context;

    public Switcher(SwitcherContext context) => this.context = context;

    public async Task SwitchAsync()
    {
        var existingProjects = new List<string>();
        foreach (var folder in context.ProjectsFolders)
        {
            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*.csproj", SearchOption.AllDirectories);
                existingProjects.AddRange(files);
            }
        }


        var slnDir = Path.GetDirectoryName(context.SolutionPath)!;
        var projects =
            (await Cli.Wrap("dotnet").WithArguments(new[] { "sln", context.SolutionPath, "list" })
                .ExecuteCommandAsync())[2..^1]
            .Select(s => Path.GetFullPath(s, slnDir)).ToArray();
        var projectsToAttach = new HashSet<string>();
        foreach (var project in projects)
        {
            Console.WriteLine($"Process project {project}");
            var packages = await Cli.Wrap("dotnet").WithArguments(new[] { "list", project, "package" })
                .ExecuteCommandAsync();
            packages = packages.Where(p => p.StartsWith("   > ", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Replace("   > ", "").Split(" ").First()).Distinct().ToArray();
            foreach (var package in packages)
            {
                var existingProject =
                    existingProjects.FirstOrDefault(p =>
                        p.EndsWith($"\\{package}.csproj", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(existingProject))
                {
                    Console.WriteLine($"Replace package {package} with project {existingProject}");
                    projectsToAttach.Add(existingProject);
                    await Cli.Wrap("dotnet").WithArguments(new[] { "remove", project, "package", package })
                        .ExecuteCommandAsync();
                    await Cli.Wrap("dotnet").WithArguments(new[] { "add", project, "reference", existingProject })
                        .ExecuteCommandAsync();
                }
            }
        }

        if (!projectsToAttach.Any())
        {
            Console.WriteLine("Nothing to add");
            return;
        }

        Console.WriteLine("Collect external projects dependencies");
        var externalProjectsDependencies = new HashSet<string>();
        foreach (var project in projectsToAttach)
        {
            var deps =
                (await Cli.Wrap("dotnet").WithArguments(new[] { "list", project, "reference" })
                    .ExecuteCommandAsync())[2..^1]
                .Select(s => Path.GetFullPath(s, Path.GetDirectoryName(project)!)).ToArray();
            foreach (var dependency in deps)
            {
                externalProjectsDependencies.Add(dependency);
            }
        }

        Console.WriteLine("Attach external projects to solution");
        var addProjectsCommandArgs = new List<string>
        {
            "sln",
            context.SolutionPath,
            "add",
            "-s",
            "external"
        };
        addProjectsCommandArgs.AddRange(projectsToAttach);
        addProjectsCommandArgs.AddRange(externalProjectsDependencies);
        await Cli.Wrap("dotnet").WithArguments(addProjectsCommandArgs).ExecuteCommandAsync();
    }
}

public record SwitcherContext
{
    public string SolutionPath { get; init; } = "";
    public List<string> ProjectsFolders { get; init; } = new();
}
