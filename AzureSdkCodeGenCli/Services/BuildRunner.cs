using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AzureSdkCodeGenCli.Models;

namespace AzureSdkCodeGenCli.Services;

public partial class BuildRunner
{
    // MSBuild diagnostic format: path(line,col): error CODE: message
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<type>error|warning)\s+(?<code>[A-Z]+\d+):\s*(?<message>.+)$", RegexOptions.Multiline)]
    private static partial Regex DiagnosticRegex();

    public async Task<BuildResult> RunGenerateCodeAsync(string projectPath, bool verbose = false)
    {
        var srcPath = Path.Combine(projectPath, "src");
        if (!Directory.Exists(srcPath))
        {
            srcPath = projectPath;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build /t:GenerateCode",
            WorkingDirectory = srcPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                if (verbose)
                {
                    Console.WriteLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                if (verbose)
                {
                    Console.Error.WriteLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        var rawOutput = output.ToString();
        var (errors, warnings) = ParseDiagnostics(rawOutput);

        return new BuildResult(
            Success: process.ExitCode == 0,
            Errors: errors,
            Warnings: warnings,
            RawOutput: rawOutput
        );
    }

    public async Task<BuildResult> RunBuildAsync(string projectPath, bool verbose = false)
    {
        var srcPath = Path.Combine(projectPath, "src");
        if (!Directory.Exists(srcPath))
        {
            srcPath = projectPath;
        }

        return await RunBuildInDirectoryAsync(srcPath, verbose);
    }

    public async Task<BuildResult> RunSolutionBuildAsync(string projectPath, bool verbose = false)
    {
        // Build from the project root (parent of src) to include tests
        return await RunBuildInDirectoryAsync(projectPath, verbose);
    }

    private async Task<BuildResult> RunBuildInDirectoryAsync(string directory, bool verbose = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build",
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                if (verbose)
                {
                    Console.WriteLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                if (verbose)
                {
                    Console.Error.WriteLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        var rawOutput = output.ToString();
        var (errors, warnings) = ParseDiagnostics(rawOutput);

        return new BuildResult(
            Success: process.ExitCode == 0,
            Errors: errors,
            Warnings: warnings,
            RawOutput: rawOutput
        );
    }

    private static (List<BuildError> Errors, List<BuildWarning> Warnings) ParseDiagnostics(string output)
    {
        var errors = new List<BuildError>();
        var warnings = new List<BuildWarning>();
        var regex = DiagnosticRegex();

        foreach (Match match in regex.Matches(output))
        {
            var file = match.Groups["file"].Value.Trim();
            var line = int.Parse(match.Groups["line"].Value);
            var col = int.Parse(match.Groups["col"].Value);
            var type = match.Groups["type"].Value;
            var code = match.Groups["code"].Value;
            var message = match.Groups["message"].Value.Trim();

            if (type == "error")
            {
                errors.Add(new BuildError(file, line, col, code, message));
            }
            else
            {
                warnings.Add(new BuildWarning(file, line, col, code, message));
            }
        }

        return (errors, warnings);
    }
}
