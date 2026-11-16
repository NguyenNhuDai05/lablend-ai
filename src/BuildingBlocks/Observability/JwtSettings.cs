namespace LabLend.Observability;

public sealed class JwtSettings
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "lablend-ai";
    public string Audience { get; set; } = "lablend-web";
}
