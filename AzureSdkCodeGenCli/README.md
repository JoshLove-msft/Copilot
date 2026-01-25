# Azure SDK CodeGen CLI

A CLI tool that integrates with the Azure SDK for .NET code generation flow and uses the GitHub Copilot SDK to iteratively fix build issues.

## Features

- **Code Generation**: Runs `dotnet build /t:GenerateCode` to generate Azure SDK code from TypeSpec
- **Automatic Build Fix**: Uses GitHub Copilot to analyze and fix build errors
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

### Basic Usage

```bash
# Navigate to your azure-sdk-for-net clone
cd /path/to/azure-sdk-for-net

# Run code generation for a specific library
dotnet run --project /path/to/AzureSdkCodeGenCli -- generate sdk/storage/Azure.Storage.Blobs
```

### Options

```
generate <path> [options]

Arguments:
  <path>  Path to the Azure SDK library folder (e.g., sdk/storage/Azure.Storage.Blobs)

Options:
  -r, --max-retries <max-retries>  Maximum number of retry attempts to fix build errors [default: 5]
  -v, --verbose                    Show detailed build output [default: False]
  -?, -h, --help                   Show help and usage information
```

### Examples

```bash
# Generate with verbose output
dotnet run -- generate sdk/keyvault/Azure.Security.KeyVault.Secrets -v

# Generate with custom retry limit
dotnet run -- generate sdk/storage/Azure.Storage.Blobs --max-retries 3

# Combine options
dotnet run -- generate sdk/textanalytics/Azure.AI.TextAnalytics -v -r 10
```

## How It Works

1. **Generate Code**: Runs `dotnet build /t:GenerateCode` in the library's `src` folder
2. **Build Check**: Runs `dotnet build` to check for compilation errors
3. **Error Analysis**: If errors exist, sends them to GitHub Copilot for analysis
4. **Apply Fixes**: Copilot edits non-Generated files to fix issues
5. **Retry Loop**: Rebuilds and repeats until success or max retries reached

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
│   └── GenerateCommand.cs  # generate command implementation
├── Services/
│   ├── BuildRunner.cs      # Executes dotnet build, parses errors
│   ├── CopilotService.cs   # Wraps GitHub Copilot SDK
│   └── FixOrchestrator.cs  # Coordinates retry loop
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

## License

MIT
