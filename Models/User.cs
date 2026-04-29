using System.ComponentModel.DataAnnotations;

namespace HngStageZeroClean.Models;

public class User
{
    [Key]
    public string Id { get; set; } = default!;

    [Required]
    public string GitHubId { get; set; } = default!;

    public string Username { get; set; } = default!;
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "analyst";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
