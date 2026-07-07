using System.CommandLine;
using Fling.Commands;
using Fling.Config;
using Fling.Content;

var store = new ConfigStore();
var clipboardReader = new WindowsClipboardReader();

var rootCommand = new RootCommand("Fling — send clipboard content from PC to phone");
rootCommand.Subcommands.Add(ConfigCommand.Create(store));
rootCommand.Subcommands.Add(PairCommand.Create(store));
rootCommand.Subcommands.Add(SendCommand.Create(store, clipboardReader));

return await rootCommand.Parse(args).InvokeAsync();
