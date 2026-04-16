namespace HngStageZeroClean.Models;

public class GenderizeResponse
{
    public string? Name { get; set; }
    public string? Gender { get; set; }
    public double Probability { get; set; }
    public int Count { get; set; }
}