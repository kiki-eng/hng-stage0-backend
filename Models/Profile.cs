using System.ComponentModel.DataAnnotations;

namespace HngStageZeroClean.Models;

public class Profile
{
    [Key]
    public string Id { get; set; } = default!;

    [Required]
    public string Name { get; set; } = default!;

    public string Gender { get; set; } = default!;
    public double GenderProbability { get; set; }
    public int SampleSize { get; set; }

    public int Age { get; set; }
    public string AgeGroup { get; set; } = default!;

    public string CountryId { get; set; } = default!;
    public double CountryProbability { get; set; }

    public DateTime CreatedAt { get; set; }
}