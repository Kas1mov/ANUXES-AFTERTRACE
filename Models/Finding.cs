namespace ANUXES.Aftertrace.Models;

public sealed class Finding
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info";
    public string Marker => Severity switch
    {
        "Review" => "!",
        "Notice" => "i",
        _ => "•"
    };
}
