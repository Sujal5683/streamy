using EnterpriseRemote.Common.Redaction;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class RedactionTests
{
    [Fact]
    public void SensitiveDataRedactor_RedactsTokensKeysAndSecrets()
    {
        string rawLog = "Operator authenticated with Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.t-ID manually. " +
                        "Generated SAT token sat_0123456789abcdef0123456789abcdef. " +
                        "Invitation code INV-4A82-99F1-0B32 redeemed. " +
                        "Payload: {\"password\":\"SuperSecret123!\", \"client_secret\":\"shh_secret_key\"}. " +
                        "Cert: -----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----";

        string sanitized = SensitiveDataRedactor.Redact(rawLog);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", sanitized);
        Assert.Contains("[REDACTED_JWT]", sanitized);

        Assert.DoesNotContain("sat_0123456789abcdef0123456789abcdef", sanitized);
        Assert.Contains("[REDACTED_SAT_TOKEN]", sanitized);

        Assert.DoesNotContain("INV-4A82-99F1-0B32", sanitized);
        Assert.Contains("[REDACTED_INVITATION]", sanitized);

        Assert.DoesNotContain("SuperSecret123!", sanitized);
        Assert.Contains("[REDACTED_PASSWORD]", sanitized);

        Assert.DoesNotContain("shh_secret_key", sanitized);
        Assert.Contains("[REDACTED_SECRET]", sanitized);

        Assert.DoesNotContain("MIIEvgIBADANBgkqhkiG9w0BAQEFAASC", sanitized);
        Assert.Contains("[REDACTED_PRIVATE_KEY]", sanitized);
    }
}
