# PackagesToProjectsTool

Tool to replace package references with projects references in .NET projects. Useful for developers to test changes in dependencies without publishing new packages.

## Installation

```bash
dotnet tool install --global SonicGD.PackagesToProjectsTool
```

### Upgrade

```bash
dotnet tool update --global SonicGD.PackagesToProjectsTool
```

## Usage

Run in solution folder

```bash
dotnet packages-to-projects -f D:\Projects\my-projects -f D:\Projects\more-my-projects
```

### Option `-f`

Use to pass on or more paths to local folders with projects

```bash
dotnet packages-to-projects -f D:\Projects\my-projects
dotnet packages-to-projects -f D:\Projects\my-projects -f D:\Projects\more-my-projects
```

### Option `-r`

Recursive mode. For example, we have this projects structure:

```
- Solution Folder
-- ProjectA/ProjectA.csproj
- Other Projects Folder
-- ProjectB/ProjectB.csproj
- Some other projects Folder
-- ProjectC/ProjectC.csproj
```

with this dependencies:

```
ProjectA.csproj -> ProjectB.nupkg
ProjectB.csproj -> ProjectC.nupkg
```

In normal mode this tool will update only current solution projects, eg ProjectA. So command

```bash
dotnet packages-to-projects -f "Other Projects Folder" -f "Some other projects Folder"
```
will result in
```
ProjectA.csproj -> ProjectB.csproj -> ProjectC.nupkg
```

But in recursive mode tool will switch everything possible.
```bash
dotnet packages-to-projects -f "Other Projects Folder" -f "Some other projects Folder" -r
```
will result in
```
ProjectA.csproj -> ProjectB.csproj -> ProjectC.csproj
```

### Option `--dry-run`

Use to simulate execution and analyze output. No actual files modifications.

```bash
dotnet packages-to-projects -f D:\Projects\my-projects -f D:\Projects\more-my-projects --dry-run
```

### Option `--no-cache`

This tool caches projects metadata to speed up sequential runs. If you to ignore or regenerate this cache, use this option

```bash
dotnet packages-to-projects -f D:\Projects\my-projects -f D:\Projects\more-my-projects --no-cache
```
