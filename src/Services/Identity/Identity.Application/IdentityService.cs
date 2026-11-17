using System.Security.Cryptography;
using System.Text;
using Identity.Domain;
using LabLend.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Application;

public sealed class IdentityService(UserManager<ApplicationUser> users, IIdentityRepository repository,
    IAccessTokenIssuer issuer, IOptions<SessionSettings> settings)
{
    private readonly SessionSettings options = settings.Value;
    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new IdentityFailure(400, "Account validation failed: " +
            string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    public Task<CurrentUser> Register(RegisterRequest request, CancellationToken ct) => repository.Mutate(async () =>
    {
        if (request.Password != request.ConfirmPassword) throw new IdentityFailure(400, "Passwords must match.");
        var email = request.Email.Trim();
        if (await users.FindByEmailAsync(email) != null) throw new IdentityFailure(409, "Email is already registered.");
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email, DisplayName = Name(request.DisplayName) };
        Check(await users.CreateAsync(user, request.Password));
        Check(await users.AddToRoleAsync(user, Roles.Student));
        repository.Audit(user.Id, user.Id, "Registered");
        return await Dto(user);
    }, ct);

    public async Task<SessionResult> Login(LoginRequest request, CancellationToken ct)
    {
        // Commit failed attempts/audit before returning 401; throwing inside the transaction would undo lockout.
        var session = await repository.Mutate<SessionResult?>(async () =>
        {
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user == null)
            {
                // Same Identity password hasher work for unknown accounts; no email in audit.
                _ = users.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), dummyHash, request.Password);
                repository.Audit(null, null, "LoginFailed");
                return null;
            }
            if (!user.IsActive || await users.IsLockedOutAsync(user))
            {
                repository.Audit(user.Id, null, "LoginFailed");
                return null;
            }
            if (!await users.CheckPasswordAsync(user, request.Password))
            {
                Check(await users.AccessFailedAsync(user));
                repository.Audit(user.Id, null, "LoginFailed");
                return null;
            }
            Check(await users.ResetAccessFailedCountAsync(user));
            repository.Audit(user.Id, user.Id, "LoginSucceeded");
            return await Session(user, Guid.NewGuid());
        }, ct);
        return session ?? throw new IdentityFailure(401, "Invalid credentials or unavailable account.");
    }

    private static readonly string dummyHash = new PasswordHasher<ApplicationUser>()
        .HashPassword(new ApplicationUser(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    public async Task<SessionResult> Refresh(string? raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 512) throw new IdentityFailure(401, "Invalid session.");
        var result = await repository.Mutate<SessionResult?>(async () =>
        {
            var token = await repository.FindToken(Hash(raw), ct);
            if (token == null) return null;
            if (token.RevokedAt != null)
            {
                await repository.Revoke(token.UserId, token.TokenFamilyId, "ReuseDetected", ct);
                repository.Audit(token.UserId, null, "RefreshReuseDetected");
                return null;
            }
            var user = await users.FindByIdAsync(token.UserId.ToString());
            if (token.ExpiresAt <= DateTimeOffset.UtcNow || user is not { IsActive: true }) return null;
            var session = await Session(user, token.TokenFamilyId);
            token.RevokedAt = DateTimeOffset.UtcNow;
            token.RevocationReason = "Rotated";
            token.ReplacedByTokenId = lastTokenId;
            return session;
        }, ct);
        return result ?? throw new IdentityFailure(401, "Invalid session.");
    }

    public Task<bool> Logout(string? raw, CancellationToken ct) => repository.Mutate(async () =>
    {
        if (!string.IsNullOrWhiteSpace(raw) && raw.Length <= 512)
        {
            var token = await repository.FindToken(Hash(raw), ct);
            if (token != null)
            {
                await repository.Revoke(token.UserId, token.TokenFamilyId, "Logout", ct);
                repository.Audit(token.UserId, token.UserId, "SessionRevoked");
            }
        }
        return true;
    }, ct);

    public async Task<CurrentUser> Get(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new IdentityFailure(404, "User not found.");
        return await Dto(user);
    }

    public async Task<bool> IsActive(Guid id) => await users.FindByIdAsync(id.ToString()) is { IsActive: true };

    public Task<CurrentUser> Update(Guid id, ProfileRequest request, Guid actor, CancellationToken ct) => repository.Mutate(async () =>
    {
        var user = await Find(id);
        if (actor == id && !user.IsActive) throw new IdentityFailure(401, "Account unavailable.");
        user.DisplayName = Name(request.DisplayName);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        Check(await users.UpdateAsync(user));
        repository.Audit(id, actor, "ProfileChanged");
        return await Dto(user);
    }, ct);

    public Task<bool> ChangePassword(Guid id, PasswordRequest request, CancellationToken ct) => repository.Mutate(async () =>
    {
        var user = await Find(id);
        if (!user.IsActive) throw new IdentityFailure(401, "Account unavailable.");
        Check(await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword));
        user.UpdatedAt = DateTimeOffset.UtcNow;
        Check(await users.UpdateAsync(user));
        await repository.Revoke(id, null, "PasswordChanged", ct);
        repository.Audit(id, id, "PasswordChanged");
        return true;
    }, ct);

    public async Task<PagedResponse<CurrentUser>> List(int page, int size, CancellationToken ct, string? keyword = null, string? role = null, bool? isActive = null)
    {
        if (page < 1 || page > 1000000 || size is < 1 or > 100) throw new IdentityFailure(400, "Invalid pagination.");
        keyword = keyword?.Trim();
        if (keyword?.Length > 100) throw new IdentityFailure(400, "Keyword must be at most 100 characters.");
        if (role != null && role is not (Roles.Student or Roles.LabStaff or Roles.Admin)) throw new IdentityFailure(400, "Invalid role filter.");
        var result = await repository.List(page, size, ct, keyword, role, isActive);
        var dtos = new List<CurrentUser>();
        foreach (var user in result.Users) dtos.Add(await Dto(user));
        return new(dtos, result.Total, page, size);
    }

    public Task<CurrentUser> SetActive(Guid id, bool active, Guid actor, CancellationToken ct) => repository.Mutate(async () =>
    {
        var user = await Find(id);
        if (!active && user.IsActive && await users.IsInRoleAsync(user, Roles.Admin) && await repository.ActiveAdminCount(ct) <= 1)
            throw new IdentityFailure(409, "Cannot deactivate the last active administrator.");
        user.IsActive = active;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        Check(await users.UpdateAsync(user));
        if (!active) await repository.Revoke(id, null, "AccountDeactivated", ct);
        repository.Audit(id, actor, active ? "AccountActivated" : "AccountDeactivated");
        return await Dto(user);
    }, ct);

    public Task<CurrentUser> SetRole(Guid id, string role, Guid actor, CancellationToken ct) => repository.Mutate(async () =>
    {
        if (role is not (Roles.Student or Roles.LabStaff or Roles.Admin)) throw new IdentityFailure(400, "Invalid role.");
        var user = await Find(id);
        if (role != Roles.Admin && user.IsActive && await users.IsInRoleAsync(user, Roles.Admin) && await repository.ActiveAdminCount(ct) <= 1)
            throw new IdentityFailure(409, "Cannot demote the last active administrator.");
        var previous = await users.GetRolesAsync(user);
        Check(await users.RemoveFromRolesAsync(user, previous));
        Check(await users.AddToRoleAsync(user, role));
        user.UpdatedAt = DateTimeOffset.UtcNow;
        Check(await users.UpdateAsync(user));
        await repository.Revoke(id, null, "RoleChanged", ct);
        repository.Audit(id, actor, "RoleChanged");
        return await Dto(user);
    }, ct);

    private async Task<ApplicationUser> Find(Guid id) => await users.FindByIdAsync(id.ToString()) ?? throw new IdentityFailure(404, "User not found.");
    private async Task<CurrentUser> Dto(ApplicationUser user) => new(user.Id, user.Email!, user.DisplayName, user.IsActive,
        (await users.GetRolesAsync(user)).ToArray(), user.CreatedAt, user.UpdatedAt);
    private static string Name(string value) => value.Trim().Length is >= 2 and <= 100 ? value.Trim() : throw new IdentityFailure(400, "Invalid display name.");
    private Guid lastTokenId;
    private async Task<SessionResult> Session(ApplicationUser user, Guid family)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var token = new RefreshToken { UserId = user.Id, TokenHash = Hash(raw), TokenFamilyId = family,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(options.RefreshDays) };
        repository.AddToken(token);
        lastTokenId = token.Id;
        var dto = await Dto(user);
        return new(dto, issuer.Issue(user, dto.Roles), raw, token.ExpiresAt);
    }
}
