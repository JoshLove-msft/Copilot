using System.CommandLine;
using AzureSdkCodeGenCli.Commands;

var rootCommand = new RootCommand("Azure SDK Code Generation CLI with Copilot-powered build fix assistance")
{
    GenerateCommand.Create(),
    MigrateCommand.Create()
};

return await rootCommand.InvokeAsync(args);
