using Identity.Application;
using Identity.Domain;
using LabLend.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

public static class IdentityBootstrap
{
    public static Task<bool> Seed(IServiceProvider services, IConfiguration configuration, bool development, CancellationToken ct) =>
        services.GetRequiredService<IIdentityRepository>().Mutate(async () =>
        {
            var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var name in new[] { Roles.Student, Roles.LabStaff, Roles.Admin })
                if (!await roles.RoleExistsAsync(name)) Ensure(await roles.CreateAsync(new() { Id = Guid.NewGuid(), Name = name }));
            if (!configuration.GetValue<bool>("Bootstrap:Enabled")) return true;
            if (!development) throw new InvalidOperationException("Administrator seed is allowed only in Development.");
            var email = configuration["Bootstrap:AdminEmail"];
            var password = configuration["Bootstrap:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Bootstrap credentials must be supplied through local configuration.");
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByEmailAsync(email) != null) return true;
            if (await services.GetRequiredService<IIdentityRepository>().ActiveAdminCount(ct) > 0) return true;
            var admin = new ApplicationUser { Id = Guid.NewGuid(), Email = email.Trim(), UserName = email.Trim(), DisplayName = "Administrator" };
            Ensure(await users.CreateAsync(admin, password));
            Ensure(await users.AddToRoleAsync(admin, Roles.Admin));
            services.GetRequiredService<IIdentityRepository>().Audit(admin.Id, null, "AdministratorBootstrapped");
            return true;
        }, ct);
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Bootstrap validation failed; check supplied credentials.");
    }
}
