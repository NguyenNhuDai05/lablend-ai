using System.ComponentModel.DataAnnotations;
using Identity.Domain;
using LabLend.Contracts;

namespace Identity.Application;

public sealed record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string DisplayName,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required] string ConfirmPassword);
public sealed record LoginRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128)] string Password);
public sealed record ProfileRequest([Required, StringLength(100, MinimumLength = 2)] string DisplayName);
public sealed record PasswordRequest([Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record ActiveRequest(bool IsActive);
public sealed record RoleRequest([Required, RegularExpression("^(STUDENT|LAB_STAFF|ADMIN)$")] string Role);
public sealed record CurrentUser(Guid Id, string Email, string DisplayName, bool IsActive,
    IReadOnlyList<string> Roles, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
public sealed record SessionResult(CurrentUser User, AccessToken AccessToken, string RefreshToken, DateTimeOffset RefreshExpiresAt);
public sealed record SessionResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, CurrentUser User);

public sealed class IdentityFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IAccessTokenIssuer
{
    AccessToken Issue(ApplicationUser user, IEnumerable<string> roles);
}

public interface IIdentityRepository
{
    // One short, database-wide account mutation lock protects refresh rotation and last-admin invariants.
    Task<T> Mutate<T>(Func<Task<T>> work, CancellationToken ct);
    Task<RefreshToken?> FindToken(string hash, CancellationToken ct);
    void AddToken(RefreshToken token);
    Task Revoke(Guid userId, Guid? familyId, string reason, CancellationToken ct);
    void Audit(Guid? userId, Guid? actor, string action);
    Task<(IReadOnlyList<ApplicationUser> Users, long Total)> List(int page, int size, CancellationToken ct);
    Task<int> ActiveAdminCount(CancellationToken ct);
}

public sealed class SessionSettings
{
    public int AccessMinutes { get; set; } = 10;
    public int RefreshDays { get; set; } = 7;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
