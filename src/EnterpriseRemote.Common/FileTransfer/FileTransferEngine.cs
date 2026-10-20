using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Common.FileTransfer;

public readonly record struct FileTransferManifest(
    string TransferId,
    string SanitizedFileName,
    long FileSizeBytes,
    string ExpectedSha256);

public readonly record struct FileChunk(
    string TransferId,
    int ChunkIndex,
    int TotalChunks,
    byte[] Data);

public sealed class FileTransferSession
{
    public FileTransferManifest Manifest { get; }
    public MemoryStream DataStream { get; }
    public int ReceivedChunks { get; set; }
    public bool IsComplete { get; set; }

    public FileTransferSession(FileTransferManifest manifest)
    {
        Manifest = manifest;
        DataStream = new MemoryStream((int)Math.Min(manifest.FileSizeBytes, 64 * 1024 * 1024)); // Pre-allocate up to 64MB
    }
}

public sealed class FileTransferEngine
{
    public const int ChunkSizeBytes = 64 * 1024; // 64 KB
    public const long MaxFileSizeBytes = 500L * 1024 * 1024; // 500 MB ceiling
    private readonly SessionCapabilities _sessionCapabilities;
    private readonly ConcurrentDictionary<string, FileTransferSession> _activeTransfers = new();

    public event Action<string>? OnFileTransferAuditLogged;

    public FileTransferEngine(SessionCapabilities capabilities)
    {
        _sessionCapabilities = capabilities;
    }

    public bool CanTransferFiles => _sessionCapabilities.HasFlag(SessionCapabilities.FileTransfer);

    public static string SanitizeFileName(string rawFileName)
    {
        string fileName = Path.GetFileName(rawFileName); // Strips directory paths
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(fileName) ? "unnamed_transfer.bin" : fileName;
    }

    public bool TryInitiateTransfer(string rawFileName, byte[] fileData, out FileTransferManifest manifest)
    {
        manifest = default;
        if (!CanTransferFiles || fileData == null || fileData.Length == 0) return false;
        if (fileData.Length > MaxFileSizeBytes) return false;

        string sanitizedName = SanitizeFileName(rawFileName);
        string hash = Convert.ToHexString(SHA256.HashData(fileData)).ToLowerInvariant();
        string transferId = Guid.NewGuid().ToString("N");

        manifest = new FileTransferManifest(transferId, sanitizedName, fileData.Length, hash);
        OnFileTransferAuditLogged?.Invoke($"FileTransferInitiated: '{sanitizedName}' ({fileData.Length} bytes, SHA256: {hash[..8]}...)");
        return true;
    }

    public bool AcceptManifest(FileTransferManifest manifest)
    {
        if (!CanTransferFiles) return false;
        if (manifest.FileSizeBytes > MaxFileSizeBytes) return false;

        var session = new FileTransferSession(manifest);
        return _activeTransfers.TryAdd(manifest.TransferId, session);
    }

    public bool ProcessIncomingChunk(in FileChunk chunk, out byte[]? completedFileData, out string? errorMessage)
    {
        completedFileData = null;
        errorMessage = null;

        if (!CanTransferFiles)
        {
            errorMessage = "File transfer capability not granted in active session.";
            return false;
        }

        if (!_activeTransfers.TryGetValue(chunk.TransferId, out var session))
        {
            errorMessage = "Unknown transfer session ID.";
            return false;
        }

        lock (session)
        {
            session.DataStream.Write(chunk.Data, 0, chunk.Data.Length);
            session.ReceivedChunks++;

            if (session.ReceivedChunks >= chunk.TotalChunks)
            {
                byte[] fullFile = session.DataStream.ToArray();
                string actualHash = Convert.ToHexString(SHA256.HashData(fullFile)).ToLowerInvariant();

                if (!actualHash.Equals(session.Manifest.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _activeTransfers.TryRemove(chunk.TransferId, out _);
                    errorMessage = "SHA-256 integrity check failed. File transmission corrupted or tampered.";
                    OnFileTransferAuditLogged?.Invoke($"FileTransferIntegrityFailure: Transfer {chunk.TransferId} hash mismatch.");
                    return false;
                }

                session.IsComplete = true;
                completedFileData = fullFile;
                _activeTransfers.TryRemove(chunk.TransferId, out _);
                OnFileTransferAuditLogged?.Invoke($"FileTransferCompleted: '{session.Manifest.SanitizedFileName}' ({fullFile.Length} bytes verified).");
                return true;
            }

            return true; // Chunk accepted, waiting for remaining chunks
        }
    }
}
