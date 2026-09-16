namespace OpenOTSTray.Services;

public static class EmailTemplateBuilder
{
    /// <summary>
    /// Supported tokens: {{link}}, {{password}}, {{passphrase}}.
    /// Any line containing {{passphrase}} is dropped entirely when no passphrase was set,
    /// so templates don't end up with a dangling "Access passphrase: " line.
    /// </summary>
    public static string BuildBody(string template, string link, string? password, string? passphrase)
    {
        var lines = template.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();

        foreach (var line in lines)
        {
            if (line.Contains("{{passphrase}}"))
            {
                if (string.IsNullOrEmpty(passphrase))
                    continue;
                kept.Add(line.Replace("{{passphrase}}", passphrase));
            }
            else
            {
                kept.Add(line);
            }
        }

        var body = string.Join("\n", kept);
        body = body.Replace("{{link}}", link);
        body = body.Replace("{{password}}", password ?? "");
        return body;
    }

    public static bool HasRequiredTokens(string template) => template.Contains("{{link}}");
}
