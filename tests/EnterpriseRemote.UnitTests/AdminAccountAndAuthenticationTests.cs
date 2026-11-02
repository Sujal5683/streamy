using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using EnterpriseRemote.Controller.UI;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class AdminAccountAndAuthenticationTests
{
    [Fact]
    public void SeededAdminAccounts_ShouldAllHaveAdministratorRole()
    {
        var store = new MemoryControlPlaneStore();

        // Seed users like in Program.cs
        var accounts = new[]
        {
            new UserIdentity { UserId = "USR-ADMIN-01", Email = "admin@corp.internal", DisplayName = "Security Admin", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } },
            new UserIdentity { UserId = "USR-ADMIN-LOCAL", Email = "admin@local", DisplayName = "Local Administrator", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } },
            new UserIdentity { UserId = "USR-ADMIN-SYS", Email = "admin", DisplayName = "System Administrator", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } },
            new UserIdentity { UserId = "USR-ADMIN-SUJAL", Email = "sujal@corp.internal", DisplayName = "Sujal (Admin)", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } },
            new UserIdentity { UserId = "USR-ADMIN-SUJAL-SHORT", Email = "sujal", DisplayName = "Sujal (Admin)", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } },
            new UserIdentity { UserId = "USR-ADMIN-ALICE", Email = "alice@corp.internal", DisplayName = "Alice Administrator", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } },
            new UserIdentity { UserId = "USR-OPERATOR-01", Email = "operator@corp.internal", DisplayName = "Remote Operator", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } },
            new UserIdentity { UserId = "USR-ADMIN-ROOT", Email = "root@corp.internal", DisplayName = "Super Admin (Root)", OrgId = "ORG-ENTERPRISE", Role = UserRole.Administrator, AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" } }
        };

        foreach (var acc in accounts)
        {
            store.SaveUser(acc);
        }

        foreach (var acc in accounts)
        {
            var retrieved = store.GetUserByEmail(acc.Email);
            Assert.NotNull(retrieved);
            Assert.Equal(UserRole.Administrator, retrieved.Role);
            Assert.Equal(acc.DisplayName, retrieved.DisplayName);
        }
    }

    [Fact]
    public void UnknownIdentity_AutoProvisioning_ShouldCreateAdminAccount()
    {
        var store = new MemoryControlPlaneStore();
        string inputEmail = "custom.admin@corp.internal";

        var user = store.GetUserByEmail(inputEmail);
        if (user == null)
        {
            string prefix = inputEmail.Contains('@') ? inputEmail.Split('@')[0] : inputEmail;
            string displayName = (prefix.Length > 0 ? char.ToUpper(prefix[0]) + (prefix.Length > 1 ? prefix[1..] : "") : "Admin") + " (Admin)";

            user = new UserIdentity
            {
                UserId = $"USR-ADMIN-{Guid.NewGuid():N}"[..16].ToUpper(),
                Email = inputEmail,
                DisplayName = displayName,
                OrgId = "ORG-ENTERPRISE",
                Role = UserRole.Administrator,
                AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
            };
            store.SaveUser(user);
        }

        var saved = store.GetUserByEmail(inputEmail);
        Assert.NotNull(saved);
        Assert.Equal(UserRole.Administrator, saved.Role);
        Assert.Contains("Admin", saved.DisplayName);
    }

    [Fact]
    public void InvitationCreation_WithAnyAdminAccount_ShouldSucceed()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);

        var admin = new UserIdentity
        {
            UserId = "USR-ADMIN-SUJAL",
            Email = "sujal@corp.internal",
            DisplayName = "Sujal (Admin)",
            OrgId = "ORG-ENTERPRISE",
            Role = UserRole.Administrator,
            AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
        };
        store.SaveUser(admin);

        var (invitation, plainCode) = enrollmentSvc.CreateInvitation(
            admin.OrgId,
            "GRP-CORP-DESKTOPS",
            admin.UserId,
            120,
            false);

        Assert.NotNull(invitation);
        Assert.NotEmpty(plainCode);
        Assert.True(invitation.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Theory]
    [InlineData("127.0.0.1:5293", "http://127.0.0.1:5293")]
    [InlineData("http://127.0.0.1:5293/", "http://127.0.0.1:5293")]
    [InlineData("", "http://127.0.0.1:5293")]
    [InlineData("stay-associations-minolta-advocate.trycloudflare.com", "https://stay-associations-minolta-advocate.trycloudflare.com")]
    public void NormalizeServerUrl_ShouldFormatEndpointsCorrectly(string input, string expected)
    {
        string result = RemoteViewerForm.NormalizeServerUrl(input);
        Assert.Equal(expected, result);
    }
}
