using System.CommandLine;

var rootCommand = new RootCommand("Fling — send clipboard content from PC to phone");

return await rootCommand.Parse(args).InvokeAsync();
