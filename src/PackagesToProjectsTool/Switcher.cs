using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CliWrap;
using Serilog;

namespace SonicGD.PackagesToProjectsTool;

public class Switcher
{
    private readonly string cachePath =
        Path.Combine(Path.GetTempPath(), nameof(PackagesToProjectsTool), "metadata_cache");

    private readonly string backupPath = Path.Combine(Path.GetTempPath(), nameof(PackagesToProjectsTool), "backup");
    private readonly SwitcherContext context;

    private readonly ILogger log = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.CurrentCulture)
        .CreateLogger();

    public Switcher(SwitcherContext context)
    {
        this.context = context;
        if (!Directory.Exists(cachePath))
        {
            Directory.CreateDirectory(cachePath);
        }

        if (!Directory.Exists(backupPath))
        {
            Directory.CreateDirectory(backupPath);
        }
    }

    public async Task SwitchAsync()
    {
        log.Information("Find existing projects...");
        var existingProjects = new List<ProjectDescription>();
        foreach (var folder in context.ProjectsFolders)
        {
            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*.csproj", SearchOption.AllDirectories);
                existingProjects.AddRange(await ReadProjectsAsync(files));
            }
        }


        log.Information("Read solution {Solution}", context.SolutionPath);
        var slnDir = Path.GetDirectoryName(context.SolutionPath)!;
        var projectFiles =
            (await Cli.Wrap("dotnet").WithArguments(new[] { "sln", context.SolutionPath, "list" })
                .ExecuteCommandAsync())[2..^1]
            .Select(s => Path.GetFullPath(s, slnDir)).ToArray();
        log.Information("Load solution projects");
        var projects = await ReadProjectsAsync(projectFiles);

        var projectsToAttach = new HashSet<ProjectDescription>();
        var processedFiles = new HashSet<string>();
        foreach (var project in projects)
        {
            try
            {
                await ReplacePackagesAsync(project, existingProjects, projectsToAttach, processedFiles);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error updating project {Project}: {ErrorText}. Rolling changes back...", project.Name,
                    ex.Message);
                foreach (var processedFile in processedFiles)
                {
                    RestoreProjectFromBackup(processedFile);
                }

                throw new InvalidOperationException("Switch aborted");
            }
        }

        if (!projectsToAttach.Any())
        {
            log.Warning("Nothing to add");
            return;
        }

        log.Information("Collect external projects dependencies");
        var externalProjectsDependencies = projectsToAttach.SelectMany(project => project.References).Distinct();

        var addProjectsCommandArgs = new List<string>
        {
            "sln",
            context.SolutionPath,
            "add",
            "-s",
            "external"
        };
        var allProjectsToAttach = projectsToAttach.Select(project => project.Path).ToList();
        allProjectsToAttach.AddRange(externalProjectsDependencies);
        addProjectsCommandArgs.AddRange(allProjectsToAttach);
        addProjectsCommandArgs = addProjectsCommandArgs.Distinct().ToList();
        log.Information("Attach {Count} external projects to solution", allProjectsToAttach.Count);
        if (!context.IsDryRun)
        {
            await Cli.Wrap("dotnet").WithArguments(addProjectsCommandArgs).ExecuteCommandAsync();
        }
        else
        {
            log.Information("Dry run. Do nothing...");
        }
    }

    private async Task ReplacePackagesAsync(ProjectDescription project,
        List<ProjectDescription> existingProjects, HashSet<ProjectDescription> projectsToAttach,
        HashSet<string> processedProjects)
    {
        if (processedProjects.Contains(project.Path))
        {
            return;
        }

        log.Information("Process project {Project}", project.Name);
        processedProjects.Add(project.Path);
        BackupProjectFile(project.Path);
        var foundProjects = new List<ProjectDescription>();
        foreach (var package in project.Packages)
        {
            var existingProject = existingProjects.FirstOrDefault(p => p.AssemblyName == package) ??
                                  existingProjects.FirstOrDefault(p => p.Name == package);

            if (existingProject is not null)
            {
                log.Information("Replace package {Package} with project {ExistingProject}", package,
                    existingProject.Name);
                projectsToAttach.Add(existingProject);
                if (!context.IsDryRun)
                {
                    await Cli.Wrap("dotnet").WithArguments(new[] { "remove", project.Path, "package", package })
                        .ExecuteCommandAsync();
                    await Cli.Wrap("dotnet")
                        .WithArguments(new[] { "add", project.Path, "reference", existingProject.Path })
                        .ExecuteCommandAsync();
                }
                else
                {
                    log.Information("Dry run, do nothing...");
                }

                foundProjects.Add(existingProject);
            }
        }

        if (context.IsRecursive)
        {
            foreach (var projectReference in project.References)
            {
                var referencedProject = await ReadProjectAsync(projectReference);
                await ReplacePackagesAsync(referencedProject, existingProjects, projectsToAttach,
                    processedProjects);
            }

            if (foundProjects.Any())
            {
                foreach (var foundProject in foundProjects)
                {
                    await ReplacePackagesAsync(foundProject, existingProjects, projectsToAttach, processedProjects);
                    foreach (var projectReference in foundProject.References)
                    {
                        var referencedProject = await ReadProjectAsync(projectReference);
                        await ReplacePackagesAsync(referencedProject, existingProjects, projectsToAttach,
                            processedProjects);
                    }
                }
            }
        }
    }

    private void BackupProjectFile(string projectPath)
    {
        var projectBackupPath = Path.Combine(backupPath, ComputeSha256Hash(projectPath));
        File.Copy(projectPath, projectBackupPath, true);
    }

    private void RestoreProjectFromBackup(string projectPath)
    {
        var projectBackupPath = Path.Combine(backupPath, ComputeSha256Hash(projectPath));
        if (File.Exists(projectBackupPath))
        {
            log.Information("Restoring file {Project} from backup", projectPath);
            File.Copy(projectBackupPath, projectPath, true);
        }
        else
        {
            log.Warning("No backup for project {Project}", projectPath);
        }
    }

    private async Task<ProjectDescription[]> ReadProjectsAsync(IEnumerable<string> files)
    {
        var projects = new List<ProjectDescription>();
        await Parallel.ForEachAsync(files, async (s, token) =>
        {
            projects.Add(await ReadProjectAsync(s));
        });

        return projects.ToArray();
    }

    private async Task<ProjectDescription> ReadProjectAsync(string csprojPath)
    {
        var projectCachePath = Path.Combine(cachePath, ComputeSha256Hash(csprojPath));
        ProjectDescription? projectDescription = null;
        if (!context.DisableCache && File.Exists(projectCachePath))
        {
            var cacheJson = await File.ReadAllTextAsync(projectCachePath);
            try
            {
                var cacheEntry = JsonSerializer.Deserialize<ProjectDescriptionCache>(cacheJson);
                if (cacheEntry is not null)
                {
                    var fileWriteTime = File.GetLastWriteTimeUtc(csprojPath);
                    if (fileWriteTime <= cacheEntry.Date)
                    {
                        projectDescription = cacheEntry.Project;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error reading cache file for project {Project}: {ErrorText}", csprojPath, ex.ToString());
                File.Delete(projectCachePath);
            }
        }

        if (projectDescription is null)
        {
            projectDescription = await DoReadProjectAsync(csprojPath);
            var json = JsonSerializer.Serialize(new ProjectDescriptionCache(DateTimeOffset.UtcNow, projectDescription));
            await File.WriteAllTextAsync(projectCachePath, json);
        }

        return projectDescription;
    }

    private static string ComputeSha256Hash(string rawData)
    {
        // Create a SHA256
        // ComputeHash - returns byte array
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));

        // Convert byte array to a string
        var builder = new StringBuilder();
        foreach (var t in bytes)
        {
            builder.Append(t.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }


    private async Task<ProjectDescription> DoReadProjectAsync(string csprojPath)
    {
        log.Information("Read {Project} project", csprojPath);
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

        await RestoreProjectAsync(csprojPath);
        var packages = await GetProjectPackagesAsync(csprojPath);
        var references = await GetProjectReferencesAsync(csprojPath);

        return new ProjectDescription(projectName, csprojPath, assemblyName, packages, references);
    }

    private async Task RestoreProjectAsync(string path)
    {
        try
        {
            await Cli.Wrap("dotnet").WithArguments(new[] { "restore", path })
                .ExecuteCommandAsync();
        }
        catch (Exception e)
        {
            log.Warning(e, "Error restoring project {Project}: {ErrorText}", path, e.Message);
        }
    }

    private static async Task<string[]> GetProjectPackagesAsync(string path)
    {
        var packages = await Cli.Wrap("dotnet").WithArguments(new[] { "list", path, "package" })
            .ExecuteCommandAsync();
        packages = packages.Where(p => p.StartsWith("   > ", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Replace("   > ", "").Split(" ").First()).Distinct().ToArray();
        return packages;
    }

    private static async Task<string[]> GetProjectReferencesAsync(string path)
    {
        var output = await Cli.Wrap("dotnet").WithArguments(new[] { "list", path, "reference" })
            .ExecuteCommandAsync();
        if (output.Length > 2)
        {
            return output[2..^1]
                .Select(s => Path.GetFullPath(s, Path.GetDirectoryName(path)!)).ToArray();
        }

        return Array.Empty<string>();
    }
}

public record SwitcherContext
{
    public string SolutionPath { get; init; } = "";
    public List<string> ProjectsFolders { get; init; } = new();
    public bool IsRecursive { get; init; }
    public bool IsDryRun { get; init; }
    public bool DisableCache { get; init; }
}

public record ProjectDescription(string Name, string Path, string AssemblyName, string[] Packages, string[] References);

public record ProjectProperty(string Name, string Value);

public record ProjectDescriptionCache(DateTimeOffset Date, ProjectDescription Project);
