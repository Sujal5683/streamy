using System;
using System.Security.Cryptography;
using System.Text;
using EnterpriseRemote.Common.Audit;
using EnterpriseRemote.Common.Clipboard;
using EnterpriseRemote.Common.FileTransfer;
using EnterpriseRemote.Common.Models;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class AuditAndFileTransferTests
{
    [Fact]
    public void SiemAuditExporter_ExportsValidJsonCefAndSyslog_WithRedaction()
    {
        var audit = new AuditEvent
        {
            EventId = "EVT-1001",
            OrgId = "ORG-CORP",
            TimestampUtc = new DateTime(2026, 10, 20, 16, 45, 0, DateTimeKind.Utc),
            EventType = "SessionAuthorized",
            OperatorId = "USR-ALICE",
            DeviceId = "DEV-FIN-01",
            Details = "Session sat_0123456789abcdef0123456789abcdef issued with password \"SecretKey123!\"",
            ClientIp = "192.168.1.50"
        };

        // 1. JSON
        string json = SiemAuditExporter.ToJson(audit);
        Assert.Contains("\"EventType\":\"SessionAuthorized\"", json);
        Assert.Contains("[REDACTED_SAT_TOKEN]", json);
        Assert.Contains("[REDACTED_PASSWORD]", json);
        Assert.DoesNotContain("SecretKey123!", json);

        // 2. CEF
        string cef = SiemAuditExporter.ToCef(audit);
        Assert.StartsWith("CEF:0|EnterpriseRemote|RemoteAccessPlatform|1.0|SessionAuthorized", cef);
        Assert.Contains("dvc=DEV-FIN-01", cef);
        Assert.Contains("suser=USR-ALICE", cef);
        Assert.Contains("[REDACTED_PASSWORD]", cef);

        // 3. Syslog RFC 5424
        string syslog = SiemAuditExporter.ToRfc5424Syslog(audit, "siem-collector-01");
        Assert.StartsWith("<134>1 2026-10-20T16:45:00.000Z siem-collector-01 EnterpriseRemote", syslog);
        Assert.Contains("[remoteAccess@48201", syslog);
        Assert.Contains("[REDACTED_PASSWORD]", syslog);
    }

    [Fact]
    public void ClipboardManager_EnforcesCapabilitiesAndSizeCeiling()
    {
        // Manager without clipboard capability
        var disabledManager = new ClipboardManager(SessionCapabilities.View | SessionCapabilities.Input);
        Assert.False(disabledManager.CanSynchronizeClipboard);
        Assert.False(disabledManager.TryCreateTextPayload("Hello World", out _));

        // Manager with clipboard capability
        var enabledManager = new ClipboardManager(SessionCapabilities.View | SessionCapabilities.Input | SessionCapabilities.Clipboard);
        Assert.True(enabledManager.CanSynchronizeClipboard);

        bool success = enabledManager.TryCreateTextPayload("Confidential enterprise text", out var payload);
        Assert.True(success);
        Assert.Equal(ClipboardFormat.TextPlain, payload.Format);

        // Reconstruct text
        bool processed = enabledManager.ProcessIncomingPayload(payload, out string? textOut, out _);
        Assert.True(processed);
        Assert.Equal("Confidential enterprise text", textOut);

        // Size ceiling: 5 MB payload must be rejected
        byte[] oversized = new byte[5 * 1024 * 1024];
        Assert.False(enabledManager.TryCreateImagePayload(oversized, out _));
    }

    [Fact]
    public void FileTransferEngine_SanitizesFilenames_PreventingPathTraversal()
    {
        Assert.Equal("passwd", FileTransferEngine.SanitizeFileName("../../etc/passwd"));
        Assert.Equal("cmd.exe", FileTransferEngine.SanitizeFileName(@"C:\Windows\System32\cmd.exe"));
        Assert.Equal("file_name_.log", FileTransferEngine.SanitizeFileName("file*name?.log"));
    }

    [Fact]
    public void FileTransferEngine_TransfersAndReassemblesFile_VerifyingSha256()
    {
        var engine = new FileTransferEngine(SessionCapabilities.View | SessionCapabilities.FileTransfer);

        // Generate 150 KB test file (crosses multiple 64KB chunks)
        byte[] originalFile = new byte[150 * 1024];
        RandomNumberGenerator.Fill(originalFile);

        bool initiated = engine.TryInitiateTransfer(@"C:\Sensitive\payroll_report.xlsx", originalFile, out var manifest);
        Assert.True(initiated);
        Assert.Equal("payroll_report.xlsx", manifest.SanitizedFileName);
        Assert.Equal(originalFile.Length, manifest.FileSizeBytes);

        // Receiver accepts manifest
        bool accepted = engine.AcceptManifest(manifest);
        Assert.True(accepted);

        // Chunk and send
        int chunkSize = FileTransferEngine.ChunkSizeBytes;
        int totalChunks = (int)Math.Ceiling((double)originalFile.Length / chunkSize);

        byte[]? reassembledFile = null;
        for (int i = 0; i < totalChunks; i++)
        {
            int offset = i * chunkSize;
            int count = Math.Min(chunkSize, originalFile.Length - offset);
            byte[] chunkData = new byte[count];
            Buffer.BlockCopy(originalFile, offset, chunkData, 0, count);

            var chunk = new FileChunk(manifest.TransferId, i, totalChunks, chunkData);
            bool chunkOk = engine.ProcessIncomingChunk(chunk, out var completedFile, out var error);
            Assert.True(chunkOk, error);

            if (completedFile != null)
            {
                reassembledFile = completedFile;
            }
        }

        Assert.NotNull(reassembledFile);
        Assert.Equal(originalFile.Length, reassembledFile.Length);
        Assert.Equal(originalFile, reassembledFile);
    }

    [Fact]
    public void FileTransferEngine_RejectsCorruptedFile_WhenSha256Mismatches()
    {
        var engine = new FileTransferEngine(SessionCapabilities.View | SessionCapabilities.FileTransfer);
        byte[] originalFile = Encoding.UTF8.GetBytes("Genuine uncorrupted payroll data");

        engine.TryInitiateTransfer("report.csv", originalFile, out var manifest);
        engine.AcceptManifest(manifest);

        // Tamper with data chunk
        byte[] corruptedData = Encoding.UTF8.GetBytes("Tampered malicious payload data!");
        var chunk = new FileChunk(manifest.TransferId, 0, 1, corruptedData);

        bool processed = engine.ProcessIncomingChunk(chunk, out var completed, out string? error);
        Assert.False(processed);
        Assert.Null(completed);
        Assert.Contains("integrity check failed", error);
    }
}
