using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GitHub.Copilot.SDK;

namespace AzureSdkCodeGenCli.Services;

public partial class MigrationService
{
    private readonly string _projectPath;
    private readonly bool _verbose;
    private readonly bool _quiet;
    private readonly bool _dryRun;
    private static readonly HttpClient _httpClient = new();
    private CopilotClient? _copilotClient;
    private CopilotSession? _copilotSession;

    public MigrationService(string projectPath, bool verbose = false, bool quiet = false, bool dryRun = false)
    {
        _projectPath = Path.GetFullPath(projectPath);
        _verbose = verbose;
        _quiet = quiet;
        _dryRun = dryRun;
    }

    public async Task<MigrationResult> MigrateAsync()
    {
        var completedSteps = new List<string>();
        var warnings = new List<string>();

        try
        {
            Console.WriteLine($"Migrating library at: {_projectPath}");
            Console.WriteLine();

            // Step 1: Update tsp-location.yaml
            Console.WriteLine("Step 1: Updating tsp-location.yaml...");
            var tspResult = await UpdateTspLocationAsync();
            if (tspResult.Success)
            {
                completedSteps.Add("Updated tsp-location.yaml with new emitterPackageJsonPath");
            }
            else if (tspResult.Warning != null)
            {
                warnings.Add(tspResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, tspResult.Error);
            }

            // Step 2: Update commit SHA to latest
            Console.WriteLine("Step 2: Updating commit SHA to latest...");
            var shaResult = await UpdateCommitShaAsync();
            if (shaResult.Success)
            {
                completedSteps.Add("Updated commit SHA to latest");
            }
            else if (shaResult.Warning != null)
            {
                warnings.Add(shaResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, shaResult.Error);
            }

            // Step 3: Update csproj to remove IncludeAutorestDependency
            Console.WriteLine("Step 3: Updating .csproj files...");
            var csprojResult = await UpdateCsprojAsync();
            if (csprojResult.Success)
            {
                completedSteps.Add("Removed IncludeAutorestDependency from .csproj");
            }
            else if (csprojResult.Warning != null)
            {
                warnings.Add(csprojResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, csprojResult.Error);
            }

            // Step 4: Update CodeGen attributes namespace
            Console.WriteLine("Step 4: Updating CodeGen attributes namespace...");
            var namespaceResult = await UpdateCodeGenNamespaceAsync();
            if (namespaceResult.Success)
            {
                completedSteps.Add($"Updated CodeGen attributes to Microsoft.TypeSpec.Generator.Customizations ({namespaceResult.FilesChanged} files)");
            }
            else if (namespaceResult.Warning != null)
            {
                warnings.Add(namespaceResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, namespaceResult.Error);
            }

            // Step 5: Replace CodeGenClient/CodeGenModel with CodeGenType
            Console.WriteLine("Step 5: Replacing CodeGenClient/CodeGenModel with CodeGenType...");
            var codegenResult = await ReplaceCodeGenAttributesAsync();
            if (codegenResult.Success)
            {
                completedSteps.Add($"Replaced CodeGenClient/CodeGenModel with CodeGenType ({codegenResult.FilesChanged} files)");
            }
            else if (codegenResult.Warning != null)
            {
                warnings.Add(codegenResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, codegenResult.Error);
            }

            // Step 5.5: Replace _pipeline with Pipeline
            Console.WriteLine("Step 5.5: Replacing _pipeline with Pipeline...");
            var pipelineResult = await ReplacePipelineFieldAsync();
            if (pipelineResult.Success)
            {
                completedSteps.Add($"Replaced _pipeline with Pipeline ({pipelineResult.FilesChanged} files)");
            }
            else if (pipelineResult.Warning != null)
            {
                warnings.Add(pipelineResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, pipelineResult.Error);
            }

            // Step 5.6: Remove Autorest.CSharp.Core using statements
            Console.WriteLine("Step 5.6: Removing Autorest.CSharp.Core using statements...");
            var autorestResult = await RemoveAutorestCSharpCoreUsingAsync();
            if (autorestResult.Success)
            {
                completedSteps.Add($"Removed Autorest.CSharp.Core using statements ({autorestResult.FilesChanged} files)");
            }
            else if (autorestResult.Warning != null)
            {
                warnings.Add(autorestResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, autorestResult.Error);
            }

            // Step 5.7: Replace _serializedAdditionalRawData with _serializedAdditionalBinaryData
            Console.WriteLine("Step 5.7: Replacing _serializedAdditionalRawData with _serializedAdditionalBinaryData...");
            var rawDataResult = await ReplaceSerializedAdditionalRawDataAsync();
            if (rawDataResult.Success)
            {
                completedSteps.Add($"Replaced _serializedAdditionalRawData with _serializedAdditionalBinaryData ({rawDataResult.FilesChanged} files)");
            }
            else if (rawDataResult.Warning != null)
            {
                warnings.Add(rawDataResult.Warning);
            }
            else
            {
                return new MigrationResult(false, completedSteps, warnings, rawDataResult.Error);
            }

            // Step 6: Run code generation and fix build errors
            Console.WriteLine("Step 6: Running code generation...");
            if (_dryRun)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("   Skipping code generation in dry-run mode");
                Console.ResetColor();
                completedSteps.Add("Code generation skipped (dry-run)");
            }
            else
            {
                var orchestrator = new FixOrchestrator(_projectPath, maxRetries: 5, verbose: _verbose);
                var generateResult = await orchestrator.RunAsync();
                
                if (generateResult.Success)
                {
                    completedSteps.Add($"Code generation succeeded after {generateResult.AttemptsUsed} attempt(s)");
                }
                else
                {
                    warnings.Add($"Code generation completed with {generateResult.Attempts.LastOrDefault()?.ErrorsAfter.Count ?? 0} remaining errors - manual fixes may be needed");
                }
            }

            // Step 9: Note about CodeGenType attributes
            Console.WriteLine("Step 9: CodeGenType attribute updates...");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("   Note: If types are regenerated with different names,");
            Console.WriteLine("   you may need to update CodeGenType attributes in the corresponding custom partial classes.");
            Console.ResetColor();
            warnings.Add("CodeGenType attributes may need manual updates if type names changed");

            await CleanupCopilotAsync();
            return new MigrationResult(true, completedSteps, warnings, null);
        }
        catch (Exception ex)
        {
            await CleanupCopilotAsync();
            return new MigrationResult(false, completedSteps, warnings, ex.Message);
        }
    }

    private async Task CleanupCopilotAsync()
    {
        if (_copilotSession != null)
        {
            await _copilotSession.DisposeAsync();
            _copilotSession = null;
        }
        if (_copilotClient != null)
        {
            await _copilotClient.StopAsync();
            await _copilotClient.DisposeAsync();
            _copilotClient = null;
        }
    }

    private async Task<StepResult> UpdateTspLocationAsync()
    {
        var tspLocationPath = Path.Combine(_projectPath, "tsp-location.yaml");
        
        if (!File.Exists(tspLocationPath))
        {
            return new StepResult(false, null, "tsp-location.yaml not found", 0);
        }

        var content = await File.ReadAllTextAsync(tspLocationPath);
        var originalContent = content;

        // Check if already migrated
        if (content.Contains("eng/azure-typespec-http-client-csharp-emitter-package.json"))
        {
            Log("tsp-location.yaml already has the new emitter path");
            return new StepResult(true, "tsp-location.yaml already migrated", null, 0);
        }

        // Update emitterPackageJsonPath
        var emitterRegex = EmitterPathRegex();
        if (emitterRegex.IsMatch(content))
        {
            content = emitterRegex.Replace(content, "emitterPackageJsonPath: eng/azure-typespec-http-client-csharp-emitter-package.json");
        }
        else
        {
            // Add the property if it doesn't exist
            content = content.TrimEnd() + "\nemitterPackageJsonPath: eng/azure-typespec-http-client-csharp-emitter-package.json\n";
        }

        if (!_dryRun)
        {
            await File.WriteAllTextAsync(tspLocationPath, content);
        }

        Log($"Updated tsp-location.yaml");
        return new StepResult(true, null, null, 1);
    }

    private async Task<StepResult> UpdateCommitShaAsync()
    {
        var tspLocationPath = Path.Combine(_projectPath, "tsp-location.yaml");
        
        if (!File.Exists(tspLocationPath))
        {
            return new StepResult(false, null, "tsp-location.yaml not found", 0);
        }

        var content = await File.ReadAllTextAsync(tspLocationPath);

        // Parse repo and directory from tsp-location.yaml
        var repoMatch = RepoRegex().Match(content);
        var directoryMatch = DirectoryRegex().Match(content);

        if (!repoMatch.Success)
        {
            return new StepResult(true, "No repo specified in tsp-location.yaml, skipping SHA update", null, 0);
        }

        var repo = repoMatch.Groups[1].Value.Trim();
        var directory = directoryMatch.Success ? directoryMatch.Groups[1].Value.Trim() : "";

        // Parse repo URL to get owner/repo
        // Formats: https://github.com/Azure/azure-rest-api-specs or Azure/azure-rest-api-specs
        string owner, repoName;
        if (repo.Contains("github.com"))
        {
            var uri = new Uri(repo);
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 2)
            {
                return new StepResult(true, $"Could not parse repo URL: {repo}", null, 0);
            }
            owner = parts[0];
            repoName = parts[1];
        }
        else if (repo.Contains('/'))
        {
            var parts = repo.Split('/');
            owner = parts[0];
            repoName = parts[1];
        }
        else
        {
            return new StepResult(true, $"Could not parse repo: {repo}", null, 0);
        }

        Log($"Looking up latest commit for {owner}/{repoName} path: {directory}");

        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AzureSdkCodeGenCli/1.0");

            // Check if the directory exists on main branch
            var pathExists = await CheckPathExistsAsync(owner, repoName, directory);
            
            string? latestSha = null;

            if (pathExists)
            {
                // Get latest commit for the path
                latestSha = await GetLatestCommitForPathAsync(owner, repoName, directory);
            }
            else
            {
                // Path doesn't exist - use Copilot to find the new location
                Log($"Path '{directory}' not found on main branch");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"   ⚠ Spec path '{directory}' not found in repo.");
                Console.WriteLine($"   Using Copilot to find the correct path...");
                Console.ResetColor();

                if (_dryRun)
                {
                    return new StepResult(true, "Spec path not found (dry-run, skipping Copilot lookup)", null, 0);
                }

                // Ask Copilot to find the new path
                var (newPath, newSha) = await FindSpecPathWithCopilotAsync(owner, repoName, directory);

                if (!string.IsNullOrEmpty(newPath) && !string.IsNullOrEmpty(newSha))
                {
                    // Update both directory and commit
                    content = DirectoryRegex().Replace(content, $"directory: {newPath}");
                    
                    var currentShaMatch = CommitRegex().Match(content);
                    if (currentShaMatch.Success)
                    {
                        content = CommitRegex().Replace(content, $"commit: {newSha}");
                    }
                    else
                    {
                        content = content.TrimEnd() + $"\ncommit: {newSha}\n";
                    }

                    await File.WriteAllTextAsync(tspLocationPath, content);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"   ✓ Found new path: {newPath}");
                    Console.WriteLine($"   ✓ Updated commit: {newSha[..8]}...");
                    Console.ResetColor();

                    return new StepResult(true, null, null, 1);
                }
                else
                {
                    // Copilot couldn't find it, use latest main commit
                    latestSha = await GetLatestCommitAsync(owner, repoName);
                    
                    if (!string.IsNullOrEmpty(latestSha))
                    {
                        var currentShaMatch = CommitRegex().Match(content);
                        if (currentShaMatch.Success)
                        {
                            content = CommitRegex().Replace(content, $"commit: {latestSha}");
                        }
                        else
                        {
                            content = content.TrimEnd() + $"\ncommit: {latestSha}\n";
                        }

                        await File.WriteAllTextAsync(tspLocationPath, content);

                        return new StepResult(true, $"Could not find new spec path. Updated SHA to latest main ({latestSha[..8]}...). Manual path update required.", null, 1);
                    }
                    
                    return new StepResult(true, "Spec path not found and could not determine new location", null, 0);
                }
            }

            if (string.IsNullOrEmpty(latestSha))
            {
                // Fall back to latest commit on main
                Log("Could not find path-specific commits, using latest main commit");
                latestSha = await GetLatestCommitAsync(owner, repoName);
            }

            if (string.IsNullOrEmpty(latestSha))
            {
                return new StepResult(true, "Could not determine latest commit SHA", null, 0);
            }

            Log($"Latest commit SHA: {latestSha}");

            // Check if already up to date
            var shaMatch = CommitRegex().Match(content);
            if (shaMatch.Success && shaMatch.Groups[1].Value.Trim() == latestSha)
            {
                Log("Commit SHA already up to date");
                return new StepResult(true, "Commit SHA already up to date", null, 0);
            }

            // Update the commit SHA
            if (shaMatch.Success)
            {
                content = CommitRegex().Replace(content, $"commit: {latestSha}");
            }
            else
            {
                content = content.TrimEnd() + $"\ncommit: {latestSha}\n";
            }

            if (!_dryRun)
            {
                await File.WriteAllTextAsync(tspLocationPath, content);
            }

            Log($"Updated commit SHA to {latestSha[..8]}...");
            return new StepResult(true, null, null, 1);
        }
        catch (Exception ex)
        {
            return new StepResult(true, $"Could not fetch latest commit: {ex.Message}", null, 0);
        }
    }

    private async Task<(string? path, string? commit)> FindSpecPathWithCopilotAsync(string owner, string repoName, string oldDirectory)
    {
        try
        {
            // Initialize Copilot if not already done
            if (_copilotClient == null)
            {
                _copilotClient = new CopilotClient();
                await _copilotClient.StartAsync();

                _copilotSession = await _copilotClient.CreateSessionAsync(new SessionConfig
                {
                    Model = "claude-sonnet-4-20250514",
                    SystemMessage = new SystemMessageConfig
                    {
                        Mode = SystemMessageMode.Append,
                        Content = """
                            You are helping find the new location of a TypeSpec specification in the azure-rest-api-specs repository.
                            The specification may have been moved, renamed, or restructured.
                            
                            When searching:
                            1. Use the GitHub MCP tools to search and browse the repository
                            2. Look for TypeSpec projects (directories containing tspconfig.yaml or main.tsp)
                            3. Match based on service name, resource provider, and API type
                            4. Consider common restructuring patterns (e.g., data-plane -> service-specific folders)
                            
                            Return ONLY a JSON response in this exact format, nothing else:
                            {"path": "specification/...", "commit": "abc123..."}
                            
                            If you cannot find the new location, return:
                            {"path": null, "commit": null}
                            """
                    },
                    InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
                    Streaming = true
                });
            }

            if (_copilotSession == null)
            {
                return (null, null);
            }

            var jsonExample = """{"path": "new/path/here", "commit": "sha_here"}""";
            var prompt = $"""
                The TypeSpec specification at path '{oldDirectory}' no longer exists in the {owner}/{repoName} repository on the main branch.
                
                Please search the repository to find where this specification may have been moved to.
                Look for:
                1. Similar directory names under specification/
                2. TypeSpec projects (with tspconfig.yaml) that match the service
                3. The service name appears to be related to: {ExtractServiceHint(oldDirectory)}
                
                Once you find the new location, get the latest commit SHA for that path.
                
                Return your answer as JSON: {jsonExample}
                """;

            var response = new StringBuilder();
            var done = new TaskCompletionSource();

            _copilotSession.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        if (!_quiet) Console.Write(delta.Data.DeltaContent);
                        break;
                    case AssistantMessageEvent msg:
                        response.AppendLine(msg.Data.Content);
                        if (!_quiet) Console.WriteLine();
                        break;
                    case ToolExecutionStartEvent toolStart:
                        if (!_quiet)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"   🔧 {toolStart.Data.ToolName}");
                            Console.ResetColor();
                        }
                        break;
                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;
                    case SessionErrorEvent err:
                        done.TrySetException(new Exception(err.Data.Message));
                        break;
                }
            });

            await _copilotSession.SendAsync(new MessageOptions { Prompt = prompt });
            await done.Task;

            // Parse the JSON response
            var responseText = response.ToString().Trim();
            
            // Try to extract JSON from the response
            var jsonMatch = Regex.Match(responseText, @"\{[^}]+\}");
            if (jsonMatch.Success)
            {
                using var doc = JsonDocument.Parse(jsonMatch.Value);
                var path = doc.RootElement.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
                var commit = doc.RootElement.TryGetProperty("commit", out var commitProp) ? commitProp.GetString() : null;
                
                return (path, commit);
            }

            return (null, null);
        }
        catch (Exception ex)
        {
            Log($"Copilot search failed: {ex.Message}");
            return (null, null);
        }
    }

    private static string ExtractServiceHint(string directory)
    {
        // Extract hints from the directory path
        var parts = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var hints = new List<string>();
        
        foreach (var part in parts)
        {
            if (part != "specification" && part != "data-plane" && part != "resource-manager")
            {
                hints.Add(part);
            }
        }
        
        return string.Join(", ", hints);
    }

    private async Task<bool> CheckPathExistsAsync(string owner, string repoName, string directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return true;
        }

        var contentsUrl = $"https://api.github.com/repos/{owner}/{repoName}/contents/{Uri.EscapeDataString(directory)}?ref=main";
        var response = await _httpClient.GetAsync(contentsUrl);
        return response.IsSuccessStatusCode;
    }

    private async Task<string?> GetLatestCommitForPathAsync(string owner, string repoName, string path)
    {
        var apiUrl = $"https://api.github.com/repos/{owner}/{repoName}/commits?path={Uri.EscapeDataString(path)}&sha=main&per_page=1";
        var response = await _httpClient.GetAsync(apiUrl);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.GetArrayLength() == 0)
        {
            return null;
        }

        return root[0].GetProperty("sha").GetString();
    }

    private async Task<string?> GetLatestCommitAsync(string owner, string repoName)
    {
        var apiUrl = $"https://api.github.com/repos/{owner}/{repoName}/commits?sha=main&per_page=1";
        var response = await _httpClient.GetAsync(apiUrl);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.GetArrayLength() == 0)
        {
            return null;
        }

        return root[0].GetProperty("sha").GetString();
    }

    private async Task<StepResult> UpdateCsprojAsync()
    {
        var srcPath = Path.Combine(_projectPath, "src");
        var searchPath = Directory.Exists(srcPath) ? srcPath : _projectPath;
        
        var csprojFiles = Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories);
        
        if (csprojFiles.Length == 0)
        {
            return new StepResult(false, null, "No .csproj files found", 0);
        }

        var filesChanged = 0;
        foreach (var csprojFile in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(csprojFile);
            
            if (!content.Contains("IncludeAutorestDependency"))
            {
                continue;
            }

            var newContent = AutorestDependencyRegex().Replace(content, "");
            
            // Clean up any resulting empty lines
            newContent = MultipleNewlinesRegex().Replace(newContent, "\n\n");

            if (content != newContent)
            {
                if (!_dryRun)
                {
                    await File.WriteAllTextAsync(csprojFile, newContent);
                }
                filesChanged++;
                Log($"Updated {Path.GetFileName(csprojFile)}");
            }
        }

        if (filesChanged == 0)
        {
            return new StepResult(true, "No IncludeAutorestDependency found in .csproj files", null, 0);
        }

        return new StepResult(true, null, null, filesChanged);
    }

    private async Task<StepResult> UpdateCodeGenNamespaceAsync()
    {
        var srcPath = Path.Combine(_projectPath, "src");
        var searchPath = Directory.Exists(srcPath) ? srcPath : _projectPath;
        
        var csFiles = Directory.GetFiles(searchPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Generated" + Path.DirectorySeparatorChar))
            .ToList();

        var filesChanged = 0;
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile);
            var originalContent = content;

            // Replace old namespace with new namespace
            // Common old namespaces: Azure.Core.Codegen, Azure.CodeGen
            content = content.Replace("using Azure.Core;", "using Azure.Core;\nusing Microsoft.TypeSpec.Generator.Customizations;");
            
            // Remove duplicate usings if we added one that already exists
            if (content.Contains("using Microsoft.TypeSpec.Generator.Customizations;") && 
                originalContent.Contains("using Microsoft.TypeSpec.Generator.Customizations;"))
            {
                content = originalContent; // revert, already has the using
            }

            // Check for CodeGen attributes that need the namespace
            if (content.Contains("[CodeGen") && !content.Contains("Microsoft.TypeSpec.Generator.Customizations"))
            {
                // Add the using statement after the last using
                var lastUsingIndex = content.LastIndexOf("using ");
                if (lastUsingIndex >= 0)
                {
                    var endOfLine = content.IndexOf('\n', lastUsingIndex);
                    if (endOfLine >= 0)
                    {
                        content = content.Insert(endOfLine + 1, "using Microsoft.TypeSpec.Generator.Customizations;\n");
                    }
                }
            }

            if (content != originalContent)
            {
                if (!_dryRun)
                {
                    await File.WriteAllTextAsync(csFile, content);
                }
                filesChanged++;
                Log($"Updated namespace in {Path.GetFileName(csFile)}");
            }
        }

        if (filesChanged == 0)
        {
            return new StepResult(true, "No files needed namespace updates", null, 0);
        }

        return new StepResult(true, null, null, filesChanged);
    }

    private async Task<StepResult> ReplaceCodeGenAttributesAsync()
    {
        var srcPath = Path.Combine(_projectPath, "src");
        var searchPath = Directory.Exists(srcPath) ? srcPath : _projectPath;
        
        var csFiles = Directory.GetFiles(searchPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Generated" + Path.DirectorySeparatorChar))
            .ToList();

        var filesChanged = 0;
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile);
            var originalContent = content;

            // Replace CodeGenClient with CodeGenType
            content = CodeGenClientRegex().Replace(content, "CodeGenType");
            
            // Replace CodeGenModel with CodeGenType
            content = CodeGenModelRegex().Replace(content, "CodeGenType");

            if (content != originalContent)
            {
                if (!_dryRun)
                {
                    await File.WriteAllTextAsync(csFile, content);
                }
                filesChanged++;
                Log($"Replaced CodeGen attributes in {Path.GetFileName(csFile)}");
            }
        }

        if (filesChanged == 0)
        {
            return new StepResult(true, "No files contained CodeGenClient/CodeGenModel", null, 0);
        }

        return new StepResult(true, null, null, filesChanged);
    }

    private async Task<StepResult> ReplacePipelineFieldAsync()
    {
        var srcPath = Path.Combine(_projectPath, "src");
        var searchPath = Directory.Exists(srcPath) ? srcPath : _projectPath;
        
        var csFiles = Directory.GetFiles(searchPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Generated" + Path.DirectorySeparatorChar))
            .ToList();

        var filesChanged = 0;
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile);
            var originalContent = content;

            // Replace _pipeline with Pipeline
            content = PipelineFieldRegex().Replace(content, "Pipeline");

            if (content != originalContent)
            {
                if (!_dryRun)
                {
                    await File.WriteAllTextAsync(csFile, content);
                }
                filesChanged++;
                Log($"Replaced _pipeline in {Path.GetFileName(csFile)}");
            }
        }

        if (filesChanged == 0)
        {
            return new StepResult(true, "No files contained _pipeline", null, 0);
        }

        return new StepResult(true, null, null, filesChanged);
    }

    private async Task<StepResult> RemoveAutorestCSharpCoreUsingAsync()
    {
        var srcPath = Path.Combine(_projectPath, "src");
        var searchPath = Directory.Exists(srcPath) ? srcPath : _projectPath;
        
        var csFiles = Directory.GetFiles(searchPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Generated" + Path.DirectorySeparatorChar))
            .ToList();

        var filesChanged = 0;
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile);
            var originalContent = content;

            // Remove using Autorest.CSharp.Core;
            content = AutorestCSharpCoreUsingRegex().Replace(content, "");

            if (content != originalContent)
            {
                if (!_dryRun)
                {
                    await File.WriteAllTextAsync(csFile, content);
                }
                filesChanged++;
                Log($"Removed Autorest.CSharp.Core using in {Path.GetFileName(csFile)}");
            }
        }

        if (filesChanged == 0)
        {
            return new StepResult(true, "No files contained Autorest.CSharp.Core using", null, 0);
        }

        return new StepResult(true, null, null, filesChanged);
    }

    private async Task<StepResult> ReplaceSerializedAdditionalRawDataAsync()
    {
        var srcPath = Path.Combine(_projectPath, "src");
        var searchPath = Directory.Exists(srcPath) ? srcPath : _projectPath;
        
        var csFiles = Directory.GetFiles(searchPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Generated" + Path.DirectorySeparatorChar))
            .ToList();

        var filesChanged = 0;
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile);
            var originalContent = content;

            // Replace _serializedAdditionalRawData and serializedAdditionalRawData with binary variant
            content = SerializedAdditionalRawDataRegex().Replace(content, match =>
                match.Value.StartsWith("_") ? "_additionalBinaryDataProperties" : "additionalBinaryDataProperties");

            if (content != originalContent)
            {
                if (!_dryRun)
                {
                    await File.WriteAllTextAsync(csFile, content);
                }
                filesChanged++;
                Log($"Replaced serializedAdditionalRawData in {Path.GetFileName(csFile)}");
            }
        }

        if (filesChanged == 0)
        {
            return new StepResult(true, "No files contained serializedAdditionalRawData", null, 0);
        }

        return new StepResult(true, null, null, filesChanged);
    }

    private void Log(string message)
    {
        if (_verbose)
        {
            Console.WriteLine($"  {message}");
        }
    }

    [GeneratedRegex(@"emitterPackageJsonPath:\s*[^\n]+")]
    private static partial Regex EmitterPathRegex();

    [GeneratedRegex(@"\s*<IncludeAutorestDependency>true</IncludeAutorestDependency>\s*")]
    private static partial Regex AutorestDependencyRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();

    [GeneratedRegex(@"repo:\s*(.+)")]
    private static partial Regex RepoRegex();

    [GeneratedRegex(@"directory:\s*(.+)")]
    private static partial Regex DirectoryRegex();

    [GeneratedRegex(@"commit:\s*(\S+)")]
    private static partial Regex CommitRegex();

    [GeneratedRegex(@"\bCodeGenClient\b")]
    private static partial Regex CodeGenClientRegex();

    [GeneratedRegex(@"\bCodeGenModel\b")]
    private static partial Regex CodeGenModelRegex();

    [GeneratedRegex(@"\b_pipeline\b")]
    private static partial Regex PipelineFieldRegex();

    [GeneratedRegex(@"^\s*using\s+Autorest\.CSharp\.Core\s*;\s*\r?\n?", RegexOptions.Multiline)]
    private static partial Regex AutorestCSharpCoreUsingRegex();

    [GeneratedRegex(@"\b_?serializedAdditionalRawData\b")]
    private static partial Regex SerializedAdditionalRawDataRegex();
}

public record MigrationResult(
    bool Success,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> Warnings,
    string? Error
);

public record StepResult(
    bool Success,
    string? Warning,
    string? Error,
    int FilesChanged
);
