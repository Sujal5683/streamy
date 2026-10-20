using System;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Storage;

namespace EnterpriseRemote.ControlPlane.Services;

public readonly record struct PolicyEvaluationResult(
    bool Allowed,
    bool RequiresAttendedConsent,
    SessionCapabilities PermittedCapabilities,
    string? DenialReason);

public sealed class PolicyEngine
{
    private readonly IControlPlaneStore _store;

    public PolicyEngine(IControlPlaneStore store)
    {
        _store = store;
    }

    public PolicyEvaluationResult Evaluate(
        UserIdentity operatorUser,
        EnrolledDevice targetDevice,
        SessionCapabilities requestedCapabilities,
        DateTime requestUtc)
    {
        // 1. Check Tenant Isolation
        if (!operatorUser.OrgId.Equals(targetDevice.OrgId, StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyEvaluationResult(false, false, SessionCapabilities.None, "Cross-tenant access prohibited.");
        }

        // 2. Check Device Operational State
        if (targetDevice.Status == DeviceStatus.Disabled)
        {
            return new PolicyEvaluationResult(false, false, SessionCapabilities.None, "Device is in Emergency-Disabled state.");
        }
        if (targetDevice.Status == DeviceStatus.Revoked)
        {
            return new PolicyEvaluationResult(false, false, SessionCapabilities.None, "Device cryptographic enrollment has been revoked.");
        }

        // 3. Check Operator Group Scoping
        if (operatorUser.Role != UserRole.Administrator)
        {
            bool hasGroupAccess = false;
            foreach (var groupId in operatorUser.AuthorizedGroupIds)
            {
                if (groupId.Equals(targetDevice.GroupId, StringComparison.OrdinalIgnoreCase))
                {
                    hasGroupAccess = true;
                    break;
                }
            }
            if (!hasGroupAccess)
            {
                return new PolicyEvaluationResult(false, false, SessionCapabilities.None, "Operator not authorized for target device group.");
            }
        }

        // 4. Retrieve Group Policy Rule
        var policy = _store.GetPolicyForGroup(targetDevice.OrgId, targetDevice.GroupId);
        if (policy == null)
        {
            // Default fail-safe policy: Operator role, attended consent required, View+Input only
            policy = new PolicyRule
            {
                OrgId = targetDevice.OrgId,
                GroupId = targetDevice.GroupId,
                MinimumRole = UserRole.Operator,
                AttendedRequired = true,
                PermittedCapabilities = SessionCapabilities.View | SessionCapabilities.Input
            };
        }

        // 5. Check Minimum Role
        if (operatorUser.Role < policy.MinimumRole)
        {
            return new PolicyEvaluationResult(false, false, SessionCapabilities.None, $"Requires minimum role of {policy.MinimumRole}.");
        }

        // 6. Check Time Schedule Window
        TimeSpan timeOfDay = requestUtc.TimeOfDay;
        if (policy.AllowedTimeStart < policy.AllowedTimeEnd)
        {
            if (timeOfDay < policy.AllowedTimeStart || timeOfDay > policy.AllowedTimeEnd)
            {
                return new PolicyEvaluationResult(false, false, SessionCapabilities.None, "Session requested outside permitted operating hours.");
            }
        }

        // 7. Compute Permitted Capabilities (Intersection of requested and policy ceiling)
        var effectiveCapabilities = requestedCapabilities & policy.PermittedCapabilities;
        if (effectiveCapabilities == SessionCapabilities.None)
        {
            return new PolicyEvaluationResult(false, false, SessionCapabilities.None, "Requested capabilities exceed policy allowance.");
        }

        return new PolicyEvaluationResult(
            Allowed: true,
            RequiresAttendedConsent: policy.AttendedRequired,
            PermittedCapabilities: effectiveCapabilities,
            DenialReason: null);
    }
}
