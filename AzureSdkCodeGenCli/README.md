# Azure SDK CodeGen CLI

A CLI tool that integrates with the Azure SDK for .NET code generation flow and uses the GitHub Copilot SDK to iteratively fix build issues.

## Features

- **Code Generation**: Runs `dotnet build /t:GenerateCode` to generate Azure SDK code from TypeSpec
- **Automatic Build Fix**: Uses GitHub Copilot to analyze and fix build errors
- **Migration Support**: Migrates libraries from the old AutoRest generator to the new TypeSpec generator
- **Iterative Retry**: Attempts fixes up to 5 times (configurable) until build succeeds
- **Safe by Design**: Never modifies files under `Generated` folders

## Prerequisites

- .NET 8.0 SDK or later
- GitHub Copilot CLI installed and in PATH
- Active GitHub Copilot subscription

## Installation

```bash
# Clone or copy the project
cd AzureSdkCodeGenCli

# Build the CLI
dotnet build

# Optionally install as a global tool
dotnet pack
dotnet tool install --global --add-source ./nupkg AzureSdkCodeGenCli
```

## Usage

### Generate Command

Runs code generation and iteratively fixes build errors using Copilot.

```bash
# Navigate to your azure-sdk-for-net clone
cd /path/to/azure-sdk-for-net

# Run code generation for a specific library
azsdkgen generate sdk/storage/Azure.Storage.Blobs
```

#### Options

```
generate <path> [options]

Arguments:
  <path>  Path to the Azure SDK library folder (e.g., sdk/storage/Azure.Storage.Blobs)

Options:
  -r, --max-retries <max-retries>  Maximum number of retry attempts to fix build errors [default: 5]
  -v, --verbose                    Show detailed build output [default: False]
  -?, -h, --help                   Show help and usage information
```

### Migrate Command

Migrates a library from the old AutoRest-based generator to the new TypeSpec generator.

```bash
# Migrate a library to the new generator
azsdkgen migrate sdk/cognitivelanguage/Azure.AI.Language.Text
```

#### What the Migrate Command Does

1. **Updates tsp-location.yaml**: Sets the emitter package path to the new TypeSpec emitter
2. **Updates Commit SHA**: Finds and updates to the latest commit for the spec in azure-rest-api-specs
3. **Updates .csproj**: Removes `<IncludeAutorestDependency>true</IncludeAutorestDependency>`
4. **Updates CodeGen Namespace**: Changes `Azure.Core.Expressions.DataFactory` to `Microsoft.TypeSpec.Generator.Customizations`
5. **Replaces Attributes**: Updates `CodeGenClient` and `CodeGenModel` to `CodeGenType`
6. **Runs Code Generation**: Executes the generate command to regenerate code and fix any issues

#### Migration Rules Applied by Copilot

During the build fix phase, Copilot automatically applies these migration patterns:

- `GeneratorPageableHelpers` → Generated `CollectionResult` types
- `foo.ToRequestContent()` → `foo` (implicit cast)
- `FromCancellationToken(cancellationToken)` → `cancellationToken.ToRequestContext()`
- `_serializedAdditionalRawData` → `_serializedAdditionalBinaryData`
- `_pipeline` → `Pipeline`
- Removes `using Autorest.CSharp.Core;`

#### Options

```
migrate <path> [options]

Arguments:
  <path>  Path to the Azure SDK library folder (e.g., sdk/storage/Azure.Storage.Blobs)

Options:
  -v, --verbose   Show detailed output [default: False]
  --dry-run       Show what would be changed without making changes [default: False]
  -?, -h, --help  Show help and usage information
```

### Examples

```bash
# Generate with verbose output
azsdkgen generate sdk/keyvault/Azure.Security.KeyVault.Secrets -v

# Generate with custom retry limit
azsdkgen generate sdk/storage/Azure.Storage.Blobs --max-retries 3

# Migrate a library (dry run to preview changes)
azsdkgen migrate sdk/textanalytics/Azure.AI.TextAnalytics --dry-run

# Migrate a library with verbose output
azsdkgen migrate sdk/cognitivelanguage/Azure.AI.Language.Text -v
```

## How It Works

### Generate Command Flow

1. **Generate Code**: Runs `dotnet build /t:GenerateCode` in the library's `src` folder
2. **Build Check**: Runs `dotnet build` to check for compilation errors
3. **Error Analysis**: If errors exist, sends them to GitHub Copilot for analysis
4. **Apply Fixes**: Copilot edits non-Generated files to fix issues
5. **Regenerate**: Runs code generation again after fixes
6. **Retry Loop**: Rebuilds and repeats until success or max retries reached

### Migrate Command Flow

1. **Update Configuration**: Updates tsp-location.yaml and .csproj files
2. **Find Latest Spec**: Uses GitHub API to find the latest commit for the spec
3. **Update Attributes**: Replaces deprecated attributes with new ones
4. **Generate & Fix**: Runs the generate command to complete migration

### Generated Folder Protection

The CLI enforces a strict rule: **files under `Generated` folders are never modified**. This ensures:

- Auto-generated code remains pristine
- Fixes are applied in customization files only
- Common patterns like partial class extensions are used

## Architecture

```
AzureSdkCodeGenCli/
├── Program.cs              # Entry point, root command setup
├── Commands/
│   ├── GenerateCommand.cs  # generate command implementation
│   └── MigrateCommand.cs   # migrate command implementation
├── Services/
│   ├── BuildRunner.cs      # Executes dotnet build, parses errors
│   ├── CopilotService.cs   # Wraps GitHub Copilot SDK
│   ├── FixOrchestrator.cs  # Coordinates retry loop
│   └── MigrationService.cs # Handles migration steps
└── Models/
    └── BuildResult.cs      # Build result data models
```

## Troubleshooting

### "copilot" command not found

Ensure the GitHub Copilot CLI is installed and in your PATH:

```bash
copilot --version
```

### Authentication errors

Make sure you're authenticated with GitHub Copilot:

```bash
copilot auth login
```

### Build errors not being fixed

Some errors require manual intervention. Check:
- The error might be in a Generated file that needs customization
- The TypeSpec definition might need updating
- There might be missing dependencies

### Migration spec path not found

If the migrate command can't find the spec path, it will use Copilot to search for the new location. This can happen when specs are renamed or moved in azure-rest-api-specs.

## License

MIT
