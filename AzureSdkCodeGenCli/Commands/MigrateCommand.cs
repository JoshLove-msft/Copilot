using System.CommandLine;
using AzureSdkCodeGenCli.Services;

namespace AzureSdkCodeGenCli.Commands;

public static class MigrateCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "Path to the Azure SDK library folder to migrate (e.g., sdk/storage/Azure.Storage.Blobs)"
        );

        var verboseOption = new Option<bool>(
            name: "--verbose",
            getDefaultValue: () => false,
            description: "Show detailed output"
        );
        verboseOption.AddAlias("-v");

        var quietOption = new Option<bool>(
            name: "--quiet",
            getDefaultValue: () => false,
            description: "Suppress Copilot streaming output, only show progress"
        );
        quietOption.AddAlias("-q");

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            getDefaultValue: () => false,
            description: "Show what would be changed without making actual changes"
        );

        var command = new Command("migrate", "Migrate an Azure SDK library from the old generator to the new TypeSpec generator")
        {
            pathArgument,
            verboseOption,
            quietOption,
            dryRunOption
        };

        command.SetHandler(async (path, verbose, quiet, dryRun) =>
        {
            await RunMigrateAsync(path, verbose, quiet, dryRun);
        }, pathArgument, verboseOption, quietOption, dryRunOption);

        return command;
    }

    private static async Task RunMigrateAsync(string path, bool verbose, bool quiet, bool dryRun)
    {
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: Directory not found: {path}");
            Environment.Exit(1);
            return;
        }

        var migrator = new MigrationService(path, verbose, quiet, dryRun);
        var result = await migrator.MigrateAsync();

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("Migration Summary");
        Console.WriteLine("========================================");
        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Steps completed: {result.CompletedSteps.Count}");
        
        if (result.CompletedSteps.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Completed steps:");
            foreach (var step in result.CompletedSteps)
            {
                Console.WriteLine($"  ✓ {step}");
            }
        }

        if (result.Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warnings:");
            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"  ⚠ {warning}");
            }
            Console.ResetColor();
        }

        if (!result.Success && !string.IsNullOrEmpty(result.Error))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {result.Error}");
            Console.ResetColor();
        }

        if (dryRun)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("This was a dry run. No changes were made.");
            Console.ResetColor();
        }

        Environment.Exit(result.Success ? 0 : 1);
    }
}
