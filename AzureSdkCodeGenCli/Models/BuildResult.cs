namespace AzureSdkCodeGenCli.Models;

public record BuildResult(
    bool Success,
    IReadOnlyList<BuildError> Errors,
    IReadOnlyList<BuildWarning> Warnings,
    string RawOutput
);

public record BuildError(
    string FilePath,
    int Line,
    int Column,
    string Code,
    string Message
)
{
    public override string ToString() => $"{FilePath}({Line},{Column}): error {Code}: {Message}";
}

public record BuildWarning(
    string FilePath,
    int Line,
    int Column,
    string Code,
    string Message
);
