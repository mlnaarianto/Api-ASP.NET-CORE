using System.Security.Claims;
using ApiBe.Authorization;
using ApiBe.Models;
using Microsoft.AspNetCore.Identity;

namespace ApiBe.Data;

public static class DbSeeder
{
    // Peta "role -> permission yang dimilikinya", mirip role_has_permissions di Spatie.
    // Role Admin sengaja tidak ada: PermissionHandler sudah meloloskan Admin untuk semua permission.
    private static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        ["User"] = [Permissions.Users.View]
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var config = sp.GetRequiredService<IConfiguration>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // 1. Role (dibuat hanya kalau belum ada, aman dijalankan setiap start)
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var r = await roleManager.CreateAsync(new ApplicationRole { Name = role });
                if (!r.Succeeded)
                    throw new InvalidOperationException(
                        $"Gagal membuat role {role}: {string.Join(", ", r.Errors.Select(e => e.Description))}");
            }
        }

        // 2. Permission untuk role (hanya menambah yang belum ada)
        foreach (var (roleName, permissions) in RolePermissions)
        {
            var role = await roleManager.FindByNameAsync(roleName)
                       ?? throw new InvalidOperationException($"Role {roleName} tidak ditemukan");

            var existing = (await roleManager.GetClaimsAsync(role))
                .Where(c => c.Type == Permissions.Type)
                .Select(c => c.Value)
                .ToHashSet();

            foreach (var permission in permissions.Where(p => !existing.Contains(p)))
            {
                var r = await roleManager.AddClaimAsync(role, new Claim(Permissions.Type, permission));
                if (!r.Succeeded)
                    throw new InvalidOperationException(
                        $"Gagal memberi {permission} ke {roleName}: {string.Join(", ", r.Errors.Select(e => e.Description))}");
            }
        }

        // 3. Admin dan user percobaan dari konfigurasi.
        // Keduanya memakai helper yang sama, jadi tidak ada kode yang diulang.
        await SeedUserAsync(userManager, config, "Seed:Admin", "Administrator", "Admin");
        await SeedUserAsync(userManager, config, "Seed:User", "User Percobaan", "User");
    }

    // Membuat satu user dari konfigurasi (section = "Seed:Admin" atau "Seed:User").
    // Dilewati kalau konfigurasi kosong atau user-nya sudah ada.
    // Memakai `return` aman di sini, karena hanya menghentikan helper ini, bukan seluruh seeder.
    private static async Task SeedUserAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        string section,
        string defaultFullName,
        string role)
    {
        var userName = config[$"{section}:UserName"];
        var email    = config[$"{section}:Email"];
        var password = config[$"{section}:Password"];
        var fullName = config[$"{section}:FullName"] ?? defaultFullName;

        if (string.IsNullOrWhiteSpace(userName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
            return; // konfigurasi tidak diisi, lewati

        if (await userManager.FindByNameAsync(userName) is not null)
            return; // sudah ada, aman dijalankan berulang

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName
        };

        var create = await userManager.CreateAsync(user, password);
        if (!create.Succeeded)
            throw new InvalidOperationException(
                $"Gagal membuat {userName}: {string.Join(", ", create.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(user, role);
    }
}