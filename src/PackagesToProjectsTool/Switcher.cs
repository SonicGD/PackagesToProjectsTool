using System.Xml.Linq;
using CliWrap;

namespace SonicGD.PackagesToProjectsTool;

public class Switcher
{
    private readonly SwitcherContext context;

    public Switcher(SwitcherContext context) => this.context = context;

    public async Task SwitchAsync()
    {
        var existingProjects = new List<ProjectDescription>();
        foreach (var folder in context.ProjectsFolders)
        {
            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*.csproj", SearchOption.AllDirectories);
                existingProjects.AddRange(files.Select(csprojPath =>
                {
                    var projectDocument = XDocument.Load(csprojPath);
                    var np = projectDocument.Root!.Attribute("xmlns")?.Value ?? string.Empty;
                    var properties = projectDocument.Root.Elements(np + "PropertyGroup")
                        .SelectMany(x => x.Nodes().OfType<XElement>())
                        .Select(x => new ProjectProperty(x.Name.LocalName, x.Value))
                        .ToArray();
                    var projectName = Path.GetFileName(csprojPath).Replace(".csproj", "");
                    var assemblyName = "";
                    var assemblyNameProperty = properties.LastOrDefault(x => x.Name == "AssemblyName");
                    if (assemblyNameProperty is not null)
                    {
                        assemblyName = assemblyNameProperty.Value;
                    }

                    return new ProjectDescription(projectName, csprojPath, assemblyName);
                }));
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
                var existingProject = existingProjects.FirstOrDefault(p => p.AssemblyName == package) ??
                                      existingProjects.FirstOrDefault(p => p.Name == package);

                if (existingProject is not null)
                {
                    Console.WriteLine($"Replace package {package} with project {existingProject}");
                    projectsToAttach.Add(existingProject.Path);
                    await Cli.Wrap("dotnet").WithArguments(new[] { "remove", project, "package", package })
                        .ExecuteCommandAsync();
                    await Cli.Wrap("dotnet").WithArguments(new[] { "add", project, "reference", existingProject.Path })
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

public record ProjectDescription(string Name, string Path, string AssemblyName);

public record ProjectProperty(string Name, string Value);
