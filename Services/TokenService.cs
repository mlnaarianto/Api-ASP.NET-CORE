using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ApiBe.Authorization;   // BARU: untuk Permissions.Type
using ApiBe.Data;
using ApiBe.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ApiBe.Services;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int ExpiresMinutes { get; set; } = 15;
    public int RefreshExpiresHours { get; set; } = 8;
}

public interface ITokenService
{
    Task<(string Token, DateTime ExpiresAt)> CreateAsync(ApplicationUser user);
    Task<string> IssueRefreshTokenAsync(ApplicationUser user, DateTime? expiresAt = null);
    Task<(ApplicationUser User, string RefreshToken)?> RotateAsync(string refreshToken);
    Task RevokeAsync(string refreshToken);
}

public class TokenService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _jwt = options.Value;

    // Access token (JWT) berumur pendek.
    // Isinya "kartu identitas" user: siapa dia, role apa, dan permission apa saja.
    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(ApplicationUser user)
    {
        // Claim dasar: identitas user.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName!),
            new(ClaimTypes.Email, user.Email!)
        };

        // Claim role (Admin, Editor, dst).
        foreach (var role in await userManager.GetRolesAsync(user))
            claims.Add(new Claim(ClaimTypes.Role, role));

        // ---------- BARU: permission ----------

        // Subquery: daftar RoleId milik user ini (dari tabel AspNetUserRoles).
        // Belum dijalankan ke database di baris ini, baru dijalankan saat dipakai di bawah.
        var roleIds = db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId);

        // Permission yang dimiliki lewat role (tabel AspNetRoleClaims).
        // EF menggabungkan subquery di atas menjadi satu query SQL (WHERE RoleId IN (...)),
        // jadi berapa pun jumlah role, tetap satu kali ke database.
        var rolePermissions = await db.RoleClaims
            .Where(rc => roleIds.Contains(rc.RoleId) && rc.ClaimType == Permissions.Type)
            .Select(rc => rc.ClaimValue!)
            .ToListAsync();

        // Permission yang diberikan langsung ke user, di luar role (tabel AspNetUserClaims).
        var userPermissions = await db.UserClaims
            .Where(uc => uc.UserId == user.Id && uc.ClaimType == Permissions.Type)
            .Select(uc => uc.ClaimValue!)
            .ToListAsync();

        // Gabungkan keduanya. Distinct supaya permission yang sama dari dua role
        // tidak muncul ganda di token (menghemat ukuran token).
        foreach (var permission in rolePermissions.Concat(userPermissions).Distinct())
            claims.Add(new Claim(Permissions.Type, permission));

        // ---------- sisanya tidak berubah ----------

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiresMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    // Membuat refresh token acak. Yang disimpan di DB hanya hash-nya.
    // expiresAt diisi saat rotasi, supaya batas 8 jam tidak ikut bergeser.
    public async Task<string> IssueRefreshTokenAsync(ApplicationUser user, DateTime? expiresAt = null)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(raw),
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(_jwt.RefreshExpiresHours)
        });
        await db.SaveChangesAsync();

        return raw;
    }

    // Menukar refresh token lama dengan yang baru. Null berarti tidak valid.
    public async Task<(ApplicationUser User, string RefreshToken)?> RotateAsync(string refreshToken)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is null) return null;

        // Token yang sudah dicabut dipakai lagi: kemungkinan dicuri, cabut semua sesi user
        if (stored.RevokedAt is not null)
        {
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
            return null;
        }

        if (stored.ExpiresAt <= DateTime.UtcNow) return null;

        // Cabut token lama. Pencabutan ikut tersimpan saat token baru disimpan.
        stored.RevokedAt = DateTime.UtcNow;
        var newRaw = await IssueRefreshTokenAsync(stored.User, stored.ExpiresAt);

        return (stored.User, newRaw);
    }

    // Dipakai oleh logout
    public async Task RevokeAsync(string refreshToken)
    {
        var hash = Hash(refreshToken);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
    }

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}