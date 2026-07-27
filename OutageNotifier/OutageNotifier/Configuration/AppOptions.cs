namespace OutageNotifier.Configuration;

public sealed class OutageApiOptions
{
    public const string SectionName = "OutageApi";

    public string Endpoint { get; set; } = string.Empty;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = new();
    public string Subject { get; set; } = "Известување за прекин на електро дистрибуција";
}

public sealed class MatchRule
{
    public string? NasMesto { get; set; }
    public string? Adresa { get; set; }
}
