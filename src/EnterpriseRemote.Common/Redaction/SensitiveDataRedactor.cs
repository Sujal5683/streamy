using System;
using System.Text.RegularExpressions;

namespace EnterpriseRemote.Common.Redaction;

public static class SensitiveDataRedactor
{
    private static readonly Regex BearerTokenRegex = new(
        @"(Bearer\s+)[A-Za-z0-9\-_=]+\.[A-Za-z0-9\-_=]+\.?[A-Za-z0-9\-_.+/=]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SatTokenRegex = new(
        @"(sat_[A-Za-z0-9_\-]{16,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PrivateKeyRegex = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex InvitationTokenRegex = new(
        @"(INV-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PasswordFieldRegex = new(
        @"(password[""\s]*[:=]?\s*"")[^""]*("")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SecretFieldRegex = new(
        @"(""(secret|client_secret|token|credential)""\s*:\s*"")[^""]*("")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var redacted = BearerTokenRegex.Replace(input, "$1[REDACTED_JWT]");
        redacted = SatTokenRegex.Replace(redacted, "[REDACTED_SAT_TOKEN]");
        redacted = PrivateKeyRegex.Replace(redacted, "[REDACTED_PRIVATE_KEY]");
        redacted = InvitationTokenRegex.Replace(redacted, "[REDACTED_INVITATION]");
        redacted = PasswordFieldRegex.Replace(redacted, "$1[REDACTED_PASSWORD]$2");
        redacted = SecretFieldRegex.Replace(redacted, "$1[REDACTED_SECRET]$2");

        return redacted;
    }
}
