using System.Text;
using SonicGD.PackagesToProjectsTool;
using Spectre.Console.Cli;

Console.OutputEncoding = Encoding.UTF8;
var app = new CommandApp<PackagesToProjectsCommand>();
return app.Run(args);
