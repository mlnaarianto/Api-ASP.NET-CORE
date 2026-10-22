using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ApiBe.Authorization;

// 1) ATRIBUT: yang kamu tulis di controller, contoh [HasPermission("users.view")].
// Mewarisi [Authorize] dan memakai nama permission sebagai nama policy.
// Artinya: "endpoint ini butuh policy bernama users.view".
public class HasPermissionAttribute(string permission) : AuthorizeAttribute(permission);

// 2) REQUIREMENT: "syarat" yang harus dipenuhi user.
// Hanya wadah data, isinya nama permission yang dibutuhkan.
public record PermissionRequirement(string Permission) : IAuthorizationRequirement;

// 3) HANDLER: logika yang memeriksa syarat tadi.
public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Lolos kalau user berperan Admin (boleh semuanya, mirip Gate::before di Laravel)
        // atau token-nya memuat claim permission yang diminta.
        if (context.User.IsInRole("Admin") ||
            context.User.HasClaim(Permissions.Type, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        // Kalau tidak di-Succeed, hasilnya ditolak: 403 Forbidden.
        return Task.CompletedTask;
    }
}

// 4) POLICY PROVIDER: pembuat policy otomatis.
// Tanpa ini, setiap permission harus didaftarkan manual satu per satu di Program.cs.
// Dengan ini, nama apa pun yang dipakai di [HasPermission("...")] langsung dibuatkan policy-nya.
public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Cek dulu policy bawaan (misal "default"). Kalau ada, pakai itu.
        var policy = await base.GetPolicyAsync(policyName);
        if (policy is not null) return policy;

        // Kalau tidak ada, anggap nama policy = nama permission, lalu bangun policy-nya.
        return new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}