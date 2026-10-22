using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ApiBe.Models;
using ApiBe.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService) : ControllerBase
{
    // POST /api/auth/login
    // 200 = berhasil, 400 = input tidak valid (kosong dll.), 401 = kredensial salah.
    // Kenapa POST? Login membuat "sesi" baru dan membawa password, jadi tidak boleh GET
    // (password akan muncul di URL/log).
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        // Tidak perlu cek string kosong manual: [ApiController] otomatis membalas 400
        // berdasarkan atribut [Required] di LoginRequest (lihat bagian bawah).
        var user = await userManager.FindByNameAsync(req.Username);

        // Pesan sama untuk "user tidak ada" dan "password salah" supaya tidak membocorkan
        // username mana yang terdaftar. Statusnya 401 (belum terautentikasi), bukan 404.
        if (user is null || !await userManager.CheckPasswordAsync(user, req.Password))
            return Problem(
                title: "Username atau password salah",
                statusCode: StatusCodes.Status401Unauthorized);

        var refreshToken = await tokenService.IssueRefreshTokenAsync(user);
        return Ok(await BuildResponse(user, refreshToken));
    }

    // POST /api/auth/refresh
    // 200 = token baru terbit, 400 = body tidak valid, 401 = refresh token ditolak.
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var result = await tokenService.RotateAsync(req.RefreshToken);

        // Token tidak valid / kedaluwarsa / sudah dipakai -> 401, client harus login ulang.
        if (result is null)
            return Problem(
                title: "Sesi berakhir, silakan login lagi",
                statusCode: StatusCodes.Status401Unauthorized);

        return Ok(await BuildResponse(result.Value.User, result.Value.RefreshToken));
    }

    // POST /api/auth/logout
    // 204 = sukses tanpa body. Sengaja selalu 204 walau token tidak ditemukan:
    // operasi ini idempotent (hasil akhirnya sama: token tidak aktif), dan kita tidak
    // perlu memberi tahu client apakah token tersebut pernah ada.
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        await tokenService.RevokeAsync(req.RefreshToken);
        return NoContent();
    }

    // GET /api/auth/me
    // 200 = data user dari token, 401 = token tidak ada/tidak valid/kedaluwarsa.
    // 401 dikirim otomatis oleh middleware JWT sebelum method ini dijalankan.
    // GET dipakai karena hanya membaca data (aman dan idempotent).
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult Me() => Ok(new
    {
        Id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        UserName = User.Identity?.Name,
        Email = User.FindFirstValue(ClaimTypes.Email),
        Roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value)
    });

    // Helper privat untuk menyusun response yang sama di login dan refresh,
    // supaya tidak ada kode yang diulang.
    private async Task<AuthResponse> BuildResponse(ApplicationUser user, string refreshToken)
    {
        var (token, expiresAt) = await tokenService.CreateAsync(user);
        return new AuthResponse(token, expiresAt, refreshToken, user.FullName);
    }
}

// Atribut validasi dipasang di parameter record. Kalau dilanggar, ASP.NET Core otomatis
// membalas 400 dengan ValidationProblemDetails yang berisi daftar field yang salah.
public record LoginRequest(
    [Required(ErrorMessage = "Username wajib diisi")] string Username,
    [Required(ErrorMessage = "Password wajib diisi")] string Password);

public record RefreshRequest(
    [Required(ErrorMessage = "Refresh token wajib diisi")] string RefreshToken);

public record AuthResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken, string? FullName);