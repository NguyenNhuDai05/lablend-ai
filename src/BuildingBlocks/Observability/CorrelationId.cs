namespace LabLend.Observability;

public static class CorrelationId
{
    public const string Header = "X-Correlation-ID";

    // Restrict reflected values: no whitespace, control characters or unbounded log content.
    public static bool IsValid(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    public static string Resolve(string? value) => IsValid(value) ? value! : Guid.NewGuid().ToString("N");
}
