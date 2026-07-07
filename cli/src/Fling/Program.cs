using System.CommandLine;
using Fling.Commands;
using Fling.Config;

var store = new ConfigStore();

var rootCommand = new RootCommand("Fling — send clipboard content from PC to phone");
rootCommand.Subcommands.Add(ConfigCommand.Create(store));
rootCommand.Subcommands.Add(PairCommand.Create(store));

return await rootCommand.Parse(args).InvokeAsync();
