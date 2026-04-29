using System.ComponentModel.DataAnnotations;

namespace HngStageZeroClean.Models;

public class RefreshToken
{
    [Key]
    public string Id { get; set; } = default!;
    public string Token { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
}
