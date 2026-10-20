using System;
using System.Text;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Common.Clipboard;

public enum ClipboardFormat
{
    TextPlain,
    ImagePng
}

public readonly record struct ClipboardPayload(
    ClipboardFormat Format,
    byte[] Data,
    int Length);

public sealed class ClipboardManager
{
    public const int MaxClipboardSizeBytes = 4 * 1024 * 1024; // 4 MB limit
    private readonly SessionCapabilities _sessionCapabilities;

    public event Action<string>? OnClipboardAuditLogged;

    public ClipboardManager(SessionCapabilities capabilities)
    {
        _sessionCapabilities = capabilities;
    }

    public bool CanSynchronizeClipboard => _sessionCapabilities.HasFlag(SessionCapabilities.Clipboard);

    public bool TryCreateTextPayload(string text, out ClipboardPayload payload)
    {
        payload = default;
        if (!CanSynchronizeClipboard || string.IsNullOrEmpty(text)) return false;

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxClipboardSizeBytes)
        {
            return false; // Exceeds size ceiling
        }

        payload = new ClipboardPayload(ClipboardFormat.TextPlain, bytes, bytes.Length);
        OnClipboardAuditLogged?.Invoke($"ClipboardSync: text/plain ({bytes.Length} bytes)");
        return true;
    }

    public bool TryCreateImagePayload(byte[] pngData, out ClipboardPayload payload)
    {
        payload = default;
        if (!CanSynchronizeClipboard || pngData == null || pngData.Length == 0) return false;

        if (pngData.Length > MaxClipboardSizeBytes)
        {
            return false; // Exceeds size ceiling
        }

        payload = new ClipboardPayload(ClipboardFormat.ImagePng, pngData, pngData.Length);
        OnClipboardAuditLogged?.Invoke($"ClipboardSync: image/png ({pngData.Length} bytes)");
        return true;
    }

    public bool ProcessIncomingPayload(in ClipboardPayload payload, out string? textResult, out byte[]? imageResult)
    {
        textResult = null;
        imageResult = null;

        if (!CanSynchronizeClipboard) return false;
        if (payload.Length > MaxClipboardSizeBytes || payload.Data.Length != payload.Length) return false;

        if (payload.Format == ClipboardFormat.TextPlain)
        {
            textResult = Encoding.UTF8.GetString(payload.Data);
            OnClipboardAuditLogged?.Invoke($"ClipboardReceived: text/plain ({payload.Length} bytes)");
            return true;
        }

        if (payload.Format == ClipboardFormat.ImagePng)
        {
            imageResult = payload.Data;
            OnClipboardAuditLogged?.Invoke($"ClipboardReceived: image/png ({payload.Length} bytes)");
            return true;
        }

        return false;
    }
}
