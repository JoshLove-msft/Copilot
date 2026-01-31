using AzureSdkCodeGenCli.Models;

namespace AzureSdkCodeGenCli.Services;

public class FixOrchestrator
{
    private readonly BuildRunner _buildRunner;
    private readonly string _projectPath;
    private readonly int _maxRetries;
    private readonly bool _verbose;

    public FixOrchestrator(string projectPath, int maxRetries = 5, bool verbose = false)
    {
        _projectPath = Path.GetFullPath(projectPath);
        _maxRetries = maxRetries;
        _verbose = verbose;
        _buildRunner = new BuildRunner();
    }

    public async Task<OrchestratorResult> RunAsync()
    {
        Console.WriteLine($"Starting code generation for: {_projectPath}");
        Console.WriteLine();

        // Step 1: Run GenerateCode
        Console.WriteLine("Running 'dotnet build /t:GenerateCode'...");
        var generateResult = await _buildRunner.RunGenerateCodeAsync(_projectPath, _verbose);

        if (!generateResult.Success)
        {
            Console.WriteLine($"Code generation failed with {generateResult.Errors.Count} error(s).");
            if (_verbose)
            {
                foreach (var error in generateResult.Errors)
                {
                    Console.WriteLine($"  {error}");
                }
            }
        }
        else
        {
            Console.WriteLine("Code generation completed successfully.");
        }

        // Step 2: Run regular build to check for compilation errors
        Console.WriteLine();
        Console.WriteLine("Running 'dotnet build' to check for errors...");
        var buildResult = await _buildRunner.RunBuildAsync(_projectPath, _verbose);

        if (buildResult.Success)
        {
            Console.WriteLine("Build succeeded! No fixes needed.");
            return new OrchestratorResult(true, 0, []);
        }

        Console.WriteLine($"Build failed with {buildResult.Errors.Count} error(s).");
        
        // Show first few errors
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("First errors:");
        foreach (var error in buildResult.Errors.Take(5))
        {
            Console.WriteLine($"  • {error.FilePath}({error.Line}): {error.Message}");
        }
        if (buildResult.Errors.Count > 5)
        {
            Console.WriteLine($"  ... and {buildResult.Errors.Count - 5} more");
        }
        Console.ResetColor();
        Console.WriteLine();

        // Step 3: Initialize Copilot and attempt fixes
        Console.WriteLine("Initializing Copilot...");
        await using var copilot = new CopilotService(_projectPath);
        await copilot.InitializeAsync();
        Console.WriteLine("Copilot ready.");

        var attempts = new List<AttemptResult>();
        var currentErrors = buildResult.Errors;
        var currentOutput = buildResult.RawOutput;

        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"════════════════════════════════════════");
            Console.WriteLine($"  Fix Attempt {attempt}/{_maxRetries} - {currentErrors.Count} error(s)");
            Console.WriteLine($"════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            // Request fix from Copilot
            Console.WriteLine("Copilot is analyzing and fixing errors...");
            Console.WriteLine();
            var response = await copilot.RequestFixAsync(currentErrors, currentOutput);

            // Regenerate code after fixes
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("[REGEN] Regenerating code after fixes...");
            Console.ResetColor();
            var regenerateResult = await _buildRunner.RunGenerateCodeAsync(_projectPath, _verbose);

            // Then rebuild to check if fixes worked
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("[BUILD] Rebuilding to verify fixes...");
            Console.ResetColor();
            var rebuildResult = await _buildRunner.RunBuildAsync(_projectPath, _verbose);

            var attemptResult = new AttemptResult(
                attempt,
                currentErrors.ToList(),
                rebuildResult.Errors.ToList(),
                response
            );
            attempts.Add(attemptResult);

            if (rebuildResult.Success)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] Source build succeeded after {attempt} attempt(s)!");
                Console.ResetColor();
                
                // Now build the full solution (parent directory) to verify tests compile
                var solutionBuildResult = await BuildSolutionAsync(attempt, attempts);
                return solutionBuildResult;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] Build still has {rebuildResult.Errors.Count} error(s).");
            Console.ResetColor();

            // Check if we made progress
            if (rebuildResult.Errors.Count >= currentErrors.Count)
            {
                var newErrorMessages = rebuildResult.Errors.Select(e => e.Message).ToHashSet();
                var oldErrorMessages = currentErrors.Select(e => e.Message).ToHashSet();

                if (newErrorMessages.SetEquals(oldErrorMessages))
                {
                    Console.WriteLine("   No progress made on errors. Will try a different approach.");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"   [PROGRESS] Reduced errors from {currentErrors.Count} to {rebuildResult.Errors.Count}");
                Console.ResetColor();
            }

            currentErrors = rebuildResult.Errors;
            currentOutput = rebuildResult.RawOutput;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] Failed to fix all errors after {_maxRetries} attempts.");
        Console.WriteLine($"   Remaining errors: {currentErrors.Count}");
        Console.ResetColor();

        return new OrchestratorResult(false, _maxRetries, attempts);
    }

    private async Task<OrchestratorResult> BuildSolutionAsync(int srcAttempts, List<AttemptResult> attempts)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("[BUILD] Building full solution (including tests)...");
        Console.ResetColor();

        var solutionResult = await _buildRunner.RunSolutionBuildAsync(_projectPath, _verbose);

        if (solutionResult.Success)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[OK] Full solution build succeeded!");
            Console.ResetColor();
            return new OrchestratorResult(true, srcAttempts, attempts);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN] Solution build has {solutionResult.Errors.Count} error(s) (likely in tests).");
        Console.ResetColor();

        // Show first few errors
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Errors:");
        foreach (var error in solutionResult.Errors.Take(5))
        {
            Console.WriteLine($"  • {error.FilePath}({error.Line}): {error.Message}");
        }
        if (solutionResult.Errors.Count > 5)
        {
            Console.WriteLine($"  ... and {solutionResult.Errors.Count - 5} more");
        }
        Console.ResetColor();

        // Initialize Copilot for test fixes (use parent directory as project path)
        Console.WriteLine();
        Console.WriteLine("Initializing Copilot to fix test/solution errors...");
        await using var copilot = new CopilotService(_projectPath);
        await copilot.InitializeAsync();

        var currentErrors = solutionResult.Errors;
        var currentOutput = solutionResult.RawOutput;

        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"════════════════════════════════════════");
            Console.WriteLine($"  Solution Fix Attempt {attempt}/{_maxRetries} - {currentErrors.Count} error(s)");
            Console.WriteLine($"════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            // Request fix from Copilot
            Console.WriteLine("Copilot is analyzing and fixing errors...");
            Console.WriteLine();
            var response = await copilot.RequestFixAsync(currentErrors, currentOutput);

            // Rebuild solution to check if fixes worked
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("[BUILD] Rebuilding solution to verify fixes...");
            Console.ResetColor();
            var rebuildResult = await _buildRunner.RunSolutionBuildAsync(_projectPath, _verbose);

            var attemptResult = new AttemptResult(
                srcAttempts + attempt,
                currentErrors.ToList(),
                rebuildResult.Errors.ToList(),
                response
            );
            attempts.Add(attemptResult);

            if (rebuildResult.Success)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] Full solution build succeeded after {attempt} solution fix attempt(s)!");
                Console.ResetColor();
                return new OrchestratorResult(true, srcAttempts + attempt, attempts);
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] Solution build still has {rebuildResult.Errors.Count} error(s).");
            Console.ResetColor();

            // Check if we made progress
            if (rebuildResult.Errors.Count < currentErrors.Count)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"   [PROGRESS] Reduced errors from {currentErrors.Count} to {rebuildResult.Errors.Count}");
                Console.ResetColor();
            }

            currentErrors = rebuildResult.Errors;
            currentOutput = rebuildResult.RawOutput;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] Failed to fix all solution errors after {_maxRetries} attempts.");
        Console.WriteLine($"   Remaining errors: {currentErrors.Count}");
        Console.ResetColor();

        return new OrchestratorResult(false, srcAttempts + _maxRetries, attempts);
    }
}

public record OrchestratorResult(
    bool Success,
    int AttemptsUsed,
    IReadOnlyList<AttemptResult> Attempts
);

public record AttemptResult(
    int AttemptNumber,
    IReadOnlyList<BuildError> ErrorsBefore,
    IReadOnlyList<BuildError> ErrorsAfter,
    string CopilotResponse
);
