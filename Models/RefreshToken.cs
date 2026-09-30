namespace ApiBe.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    
    // Tambahkan properti lain yang biasanya dibutuhkan untuk refresh token di sini, contoh:
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    
    // Relasi ke User (opsional tapi disarankan)
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
}