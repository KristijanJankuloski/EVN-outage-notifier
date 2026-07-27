namespace OutageNotifier.Configuration;

public static class AppOptionsValidator
{
    public static IReadOnlyList<string> Validate(
        OutageApiOptions api,
        DatabaseOptions database,
        EmailOptions email,
        IReadOnlyList<MatchRule> matchRules)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(api.Endpoint))
            errors.Add("OutageApi:Endpoint is required.");

        if (string.IsNullOrWhiteSpace(database.ConnectionString))
            errors.Add("Database:ConnectionString is required.");

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            errors.Add("Email:SmtpHost is required.");

        if (string.IsNullOrWhiteSpace(email.From))
            errors.Add("Email:From is required.");

        if (email.To.Count == 0)
            errors.Add("Email:To must contain at least one recipient.");

        if (matchRules.Count == 0)
            errors.Add("MatchRules must contain at least one rule.");

        for (var i = 0; i < matchRules.Count; i++)
        {
            var rule = matchRules[i];
            if (string.IsNullOrWhiteSpace(rule.NasMesto) && string.IsNullOrWhiteSpace(rule.Adresa))
                errors.Add($"MatchRules[{i}] must set at least one of NasMesto or Adresa.");
        }

        return errors;
    }
}
