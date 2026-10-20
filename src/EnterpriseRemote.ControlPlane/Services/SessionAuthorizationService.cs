using System;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.ControlPlane.Services;

public sealed class SessionAuthorizationService
{
    private readonly string _privateKeyPem;
    public string PublicKeyPem { get; }

    public SessionAuthorizationService()
    {
        var (privKey, pubKey) = CryptoUtils.GenerateEcdsaKeyPair();
        _privateKeyPem = privKey;
        PublicKeyPem = pubKey;
    }

    public SessionAuthorizationTicket IssueTicket(
        string orgId,
        string deviceId,
        string operatorId,
        SessionCapabilities capabilities,
        int ttlSeconds = 60)
    {
        var now = DateTime.UtcNow;
        var ticket = new SessionAuthorizationTicket
        {
            SessionId = Guid.NewGuid().ToString("N"),
            OrgId = orgId,
            DeviceId = deviceId,
            OperatorId = operatorId,
            PermittedCapabilities = capabilities,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(ttlSeconds)
        };

        string payload = ticket.ComputeSignaturePayload();
        ticket.SignatureBase64 = CryptoUtils.SignData(payload, _privateKeyPem);
        return ticket;
    }

    public bool VerifyTicket(SessionAuthorizationTicket ticket)
    {
        if (ticket.ExpiresAtUtc < DateTime.UtcNow)
        {
            return false; // Expired
        }

        string payload = ticket.ComputeSignaturePayload();
        return CryptoUtils.VerifySignature(payload, ticket.SignatureBase64, PublicKeyPem);
    }
}
