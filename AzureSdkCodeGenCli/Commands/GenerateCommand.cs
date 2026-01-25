using System.CommandLine;
using AzureSdkCodeGenCli.Services;

namespace AzureSdkCodeGenCli.Commands;

public static class GenerateCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "Path to the Azure SDK library folder (e.g., sdk/storage/Azure.Storage.Blobs)"
        );

        var maxRetriesOption = new Option<int>(
            name: "--max-retries",
            getDefaultValue: () => 5,
            description: "Maximum number of retry attempts to fix build errors"
        );
        maxRetriesOption.AddAlias("-r");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            getDefaultValue: () => false,
            description: "Show detailed build output"
        );
        verboseOption.AddAlias("-v");

        var command = new Command("generate", "Generate Azure SDK code and fix build errors using Copilot")
        {
            pathArgument,
            maxRetriesOption,
            verboseOption
        };

        command.SetHandler(async (path, maxRetries, verbose) =>
        {
            await RunGenerateAsync(path, maxRetries, verbose);
        }, pathArgument, maxRetriesOption, verboseOption);

        return command;
    }

    private static async Task RunGenerateAsync(string path, int maxRetries, bool verbose)
    {
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: Directory not found: {path}");
            Environment.Exit(1);
            return;
        }

        var orchestrator = new FixOrchestrator(path, maxRetries, verbose);
        var result = await orchestrator.RunAsync();

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("Summary");
        Console.WriteLine("========================================");
        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Attempts used: {result.AttemptsUsed}");

        if (!result.Success && result.Attempts.Count > 0)
        {
            var lastAttempt = result.Attempts[^1];
            Console.WriteLine($"Remaining errors: {lastAttempt.ErrorsAfter.Count}");
            Console.WriteLine();
            Console.WriteLine("Remaining error details:");
            foreach (var error in lastAttempt.ErrorsAfter)
            {
                Console.WriteLine($"  {error}");
            }
        }

        Environment.Exit(result.Success ? 0 : 1);
    }
}
