namespace ApiBe.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string TokenHash { get; set; } = string.Empty;   // sebelumnya "Token"
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }                // baru

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
}