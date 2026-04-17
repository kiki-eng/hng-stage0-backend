namespace HngStageZeroClean.Models;

public class NationalizeResponse
{
    public string? Name { get; set; }
    public List<NationalizeCountry> Country { get; set; } = new();
}

public class NationalizeCountry
{
    public string Country_Id { get; set; } = default!;
    public double Probability { get; set; }
}