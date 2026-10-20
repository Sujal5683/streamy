using System;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class PolicyEngineTests
{
    private readonly MemoryControlPlaneStore _store = new();
    private readonly PolicyEngine _policyEngine;

    public PolicyEngineTests()
    {
        _policyEngine = new PolicyEngine(_store);

        // Configure default test group policy
        _store.SavePolicy(new PolicyRule
        {
            OrgId = "ORG-CORP",
            GroupId = "GRP-FINANCE",
            MinimumRole = UserRole.Operator,
            AttendedRequired = true,
            AllowedTimeStart = TimeSpan.FromHours(8),  // 08:00
            AllowedTimeEnd = TimeSpan.FromHours(18),   // 18:00
            PermittedCapabilities = SessionCapabilities.View | SessionCapabilities.Input | SessionCapabilities.Clipboard
        });
    }

    [Fact]
    public void PolicyEngine_GrantsAccess_WhenAllConditionsMet()
    {
        var op = new UserIdentity
        {
            UserId = "OP-1",
            OrgId = "ORG-CORP",
            Role = UserRole.Operator,
            AuthorizedGroupIds = new[] { "GRP-FINANCE" }
        };

        var dev = new EnrolledDevice
        {
            DeviceId = "DEV-1",
            OrgId = "ORG-CORP",
            GroupId = "GRP-FINANCE",
            Status = DeviceStatus.Online
        };

        // 10:00 UTC (within 08:00 - 18:00)
        var testTime = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        var result = _policyEngine.Evaluate(op, dev, SessionCapabilities.View | SessionCapabilities.Input, testTime);

        Assert.True(result.Allowed);
        Assert.True(result.RequiresAttendedConsent);
        Assert.Equal(SessionCapabilities.View | SessionCapabilities.Input, result.PermittedCapabilities);
        Assert.Null(result.DenialReason);
    }

    [Fact]
    public void PolicyEngine_BlocksCrossTenantAccess()
    {
        var op = new UserIdentity
        {
            UserId = "OP-1",
            OrgId = "ORG-ATTACKER",
            Role = UserRole.Administrator,
            AuthorizedGroupIds = new[] { "GRP-FINANCE" }
        };

        var dev = new EnrolledDevice
        {
            DeviceId = "DEV-1",
            OrgId = "ORG-VICTIM",
            GroupId = "GRP-FINANCE",
            Status = DeviceStatus.Online
        };

        var result = _policyEngine.Evaluate(op, dev, SessionCapabilities.View, DateTime.UtcNow);
        Assert.False(result.Allowed);
        Assert.Contains("Cross-tenant", result.DenialReason);
    }

    [Fact]
    public void PolicyEngine_FailsClosed_WhenDeviceIsDisabledOrRevoked()
    {
        var op = new UserIdentity
        {
            UserId = "OP-1",
            OrgId = "ORG-CORP",
            Role = UserRole.Administrator,
            AuthorizedGroupIds = new[] { "GRP-FINANCE" }
        };

        var dev = new EnrolledDevice
        {
            DeviceId = "DEV-1",
            OrgId = "ORG-CORP",
            GroupId = "GRP-FINANCE",
            Status = DeviceStatus.Disabled
        };

        var result = _policyEngine.Evaluate(op, dev, SessionCapabilities.View, DateTime.UtcNow);
        Assert.False(result.Allowed);
        Assert.Contains("Emergency-Disabled", result.DenialReason);

        dev.Status = DeviceStatus.Revoked;
        result = _policyEngine.Evaluate(op, dev, SessionCapabilities.View, DateTime.UtcNow);
        Assert.False(result.Allowed);
        Assert.Contains("revoked", result.DenialReason);
    }

    [Fact]
    public void PolicyEngine_BlocksOperator_NotInAuthorizedGroup()
    {
        var op = new UserIdentity
        {
            UserId = "OP-1",
            OrgId = "ORG-CORP",
            Role = UserRole.Operator,
            AuthorizedGroupIds = new[] { "GRP-HR" } // Not GRP-FINANCE
        };

        var dev = new EnrolledDevice
        {
            DeviceId = "DEV-1",
            OrgId = "ORG-CORP",
            GroupId = "GRP-FINANCE",
            Status = DeviceStatus.Online
        };

        var result = _policyEngine.Evaluate(op, dev, SessionCapabilities.View, DateTime.UtcNow);
        Assert.False(result.Allowed);
        Assert.Contains("not authorized", result.DenialReason);
    }

    [Fact]
    public void PolicyEngine_EnforcesOperatingHourRestrictions()
    {
        var op = new UserIdentity
        {
            UserId = "OP-1",
            OrgId = "ORG-CORP",
            Role = UserRole.Operator,
            AuthorizedGroupIds = new[] { "GRP-FINANCE" }
        };

        var dev = new EnrolledDevice
        {
            DeviceId = "DEV-1",
            OrgId = "ORG-CORP",
            GroupId = "GRP-FINANCE",
            Status = DeviceStatus.Online
        };

        // 23:00 UTC (outside 08:00 - 18:00 window)
        var testTime = new DateTime(2026, 10, 20, 23, 0, 0, DateTimeKind.Utc);
        var result = _policyEngine.Evaluate(op, dev, SessionCapabilities.View, testTime);

        Assert.False(result.Allowed);
        Assert.Contains("operating hours", result.DenialReason);
    }
}
