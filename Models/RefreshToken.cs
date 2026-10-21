namespace ApiBe.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
}