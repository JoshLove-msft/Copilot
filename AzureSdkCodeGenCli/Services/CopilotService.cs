using System.Text;
using AzureSdkCodeGenCli.Models;
using GitHub.Copilot.SDK;

namespace AzureSdkCodeGenCli.Services;

public class CopilotService : IAsyncDisposable
{
    private CopilotClient? _client;
    private CopilotSession? _session;
    private readonly string _projectPath;

    public CopilotService(string projectPath)
    {
        _projectPath = Path.GetFullPath(projectPath);
    }

    public async Task InitializeAsync()
    {
        _client = new CopilotClient(new CopilotClientOptions
        {
            Cwd = _projectPath
        });

        await _client.StartAsync();

        var systemMessage = BuildSystemMessage();

        _session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = "claude-opus-4-20250514",
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = systemMessage
            },
            // Use Copilot's built-in tools (view, edit, create, grep, glob)
            // Only allow specific tools needed for code fixes
            AvailableTools = ["view", "edit", "create", "grep", "glob"],
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            Streaming = true  // Enable streaming for real-time feedback
        });
    }

    private string BuildSystemMessage()
    {
        return $"""
            You are an expert C# developer helping fix build errors in Azure SDK libraries.

            CRITICAL RULES - YOU MUST FOLLOW THESE:
            1. NEVER modify, edit, or create files under any "Generated" folder or path containing "Generated".
            2. If an error is in a Generated file, you must fix it by creating/editing a CUSTOMIZATION file instead.
            3. Do not expect all errors to be fixed simply by modifying custom files - you will need to regenerate code after making fixes. You 
               are being run in a loop where after you make fixes, code generation will be re-run. When you are finished fixing errors, and require
               a regeneration, end your current fix iteration.
            4. Common fix patterns for Generated file errors:
               - Create a partial class in the non-Generated folder to extend the generated type
               - Add missing interface implementations in customization files
               - Create wrapper methods or extension methods

            MIGRATION PATTERNS TO APPLY:
            1. GeneratorPageableHelpers -> CollectionResult pattern:
               - If you see code using GeneratorPageableHelpers.CreatePageable or similar, it needs to be replaced
               - Look in the Generated folder for types ending in "CollectionResult" or "PageableCollection"
               - Replace the helper call with instantiating the corresponding generated CollectionResult type
               - IMPORTANT: If you cannot find a CollectionResult type in the Generated folder:
                 a. The custom method is likely suppressing generation of the internal method that would create the CollectionResult
                 b. Find the [CodeGenSuppress] attribute that suppresses the generated method
                 c. Comment out or remove that attribute
                 d. re-run code generation to generate the CollectionResult type (in order to regenerate end your current build fix iteration)
                 e. After regeneration, the CollectionResult type will exist and can be used
               - Do NOT try to create the CollectionResult type manually - it must be generated

            2. ToRequestContent() removal:
               - Input models now have an implicit cast to RequestContent
               - Replace `foo.ToRequestContent()` with just `foo`
               - Example: `using RequestContent content = details.ToRequestContent();` becomes `using RequestContent content = details;`
               - IMPORTANT: do not remove the using statement - only remove the ToRequestContent() call

            3. FromCancellationToken replacement:
               - Replace `RequestContext context = FromCancellationToken(cancellationToken);`
               - With `RequestContext context = cancellationToken.ToRequestContext();`
                
            4. Mismatched factory method type names:
               - If there is a custom type ending in ModelFactory that has a different name than the 
                 generated type ending in ModelFactory, update the CodeGenType attribute in the custom type to match the generated type name. 

            5. Mismatched ClientBuilderExtensions type names. 
                - If there is a custom type ending in ClientBuilderExtensions that has a different name than the 
                  generated type ending in ClientBuilderExtensions, update the CodeGenType attribute in the custom type to match the generated type name.
            
            6. Fetch methods in custom LRO methods:
                - In the new generator, the Fetch methods are replaced by static methods called FromLroResponse on the Response models.
                - Update custom LRO methods to use ResponseModel.FromLroResponse(response) instead of calling Fetch methods.
                - Do NOT create Fetch methods manually - call the generated FromLroResponse method.
                
            7. FromResponse method removal:    
                - The FromResponse methods have been removed from models.
                - Instead, use the explicit cast from Response to the model type.
                - Example: `var model = ModelType.FromResponse(response);` becomes `var model = (ModelType)response;`
            The project is located at: {_projectPath}

            When fixing errors:
            1. Use grep/glob to explore the codebase structure
            2. Use view to read files and understand the error context
            3. Determine if the error is in a Generated file (path contains "Generated")
            4. If in Generated file: create/edit a customization file in the parallel non-Generated location
            5. Use edit or create to make your fixes
            6. Only fix files that are NOT in Generated folders
            """;
    }

    public async Task<string> RequestFixAsync(IReadOnlyList<BuildError> errors, string buildOutput)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("Session not initialized. Call InitializeAsync first.");
        }

        var prompt = BuildFixPrompt(errors, buildOutput);
        var response = new StringBuilder();
        var done = new TaskCompletionSource();

        IDisposable? subscription = null;
        subscription = _session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    // Stream the response as it comes in
                    Console.Write(delta.Data.DeltaContent);
                    break;
                case AssistantMessageEvent msg:
                    response.AppendLine(msg.Data.Content);
                    Console.WriteLine(); // New line after streaming completes
                    break;
                case ToolExecutionStartEvent toolStart:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n🔧 Using tool: {toolStart.Data.ToolName}");
                    Console.ResetColor();
                    break;
                case ToolExecutionCompleteEvent toolComplete:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"   ✓ Tool completed");
                    Console.ResetColor();
                    break;
                case SessionIdleEvent:
                    subscription?.Dispose();
                    done.TrySetResult();
                    break;
                case SessionErrorEvent err:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n❌ Error: {err.Data.Message}");
                    Console.ResetColor();
                    subscription?.Dispose();
                    done.TrySetException(new Exception(err.Data.Message));
                    break;
            }
        });

        await _session.SendAsync(new MessageOptions { Prompt = prompt });
        await done.Task;

        return response.ToString();
    }

    private static string BuildFixPrompt(IReadOnlyList<BuildError> errors, string buildOutput)
    {
        const int MaxErrors = 10;
        const int MaxBuildOutputChars = 4000;

        var sb = new StringBuilder();
        sb.AppendLine("The following build errors occurred. Please analyze and fix them.");
        sb.AppendLine();
        sb.AppendLine("REMEMBER: Do NOT edit any file in a 'Generated' folder. Create customization files instead.");
        sb.AppendLine();
        sb.AppendLine("## Errors:");
        
        var errorsToShow = errors.Take(MaxErrors).ToList();
        foreach (var error in errorsToShow)
        {
            sb.AppendLine($"- {error}");
        }
        
        if (errors.Count > MaxErrors)
        {
            sb.AppendLine($"- ... and {errors.Count - MaxErrors} more errors (fix the above first)");
        }
        
        sb.AppendLine();
        sb.AppendLine("## Build Output (truncated):");
        sb.AppendLine("```");
        
        var truncatedOutput = buildOutput.Length > MaxBuildOutputChars 
            ? buildOutput[..MaxBuildOutputChars] + "\n... [truncated]" 
            : buildOutput;
        sb.AppendLine(truncatedOutput);
        sb.AppendLine("```");

        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_session != null)
        {
            await _session.DisposeAsync();
        }
        if (_client != null)
        {
            await _client.StopAsync();
            await _client.DisposeAsync();
        }
    }
}
