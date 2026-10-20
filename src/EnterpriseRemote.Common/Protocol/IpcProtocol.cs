using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EnterpriseRemote.Common.Protocol;

public enum IpcMessageType : ushort
{
    HandshakeRequest = 0x0001,
    HandshakeResponse = 0x0002,
    PromptConsent = 0x0010,
    ConsentResult = 0x0011,
    SessionStateUpdate = 0x0030,
    TerminateSession = 0x0040,
    Heartbeat = 0x0050
}

public sealed class IpcFrame
{
    public const uint MagicHeader = 0x45525043; // 'ERPC'
    public const ushort CurrentVersion = 0x0001;

    public IpcMessageType MessageType { get; set; }
    public uint SequenceNumber { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Serialize(byte[]? hmacKey = null)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(MagicHeader);
        bw.Write(CurrentVersion);
        bw.Write((ushort)MessageType);
        bw.Write(SequenceNumber);
        bw.Write(Payload.Length);
        bw.Write(Payload);

        byte[] frameData = ms.ToArray();

        // 32-byte HMAC-SHA256 signature
        byte[] hmac;
        if (hmacKey != null && hmacKey.Length > 0)
        {
            using var hmacSha = new HMACSHA256(hmacKey);
            hmac = hmacSha.ComputeHash(frameData);
        }
        else
        {
            hmac = new byte[32]; // Zero padding when unkeyed
        }

        byte[] result = new byte[frameData.Length + hmac.Length];
        Buffer.BlockCopy(frameData, 0, result, 0, frameData.Length);
        Buffer.BlockCopy(hmac, 0, result, frameData.Length, hmac.Length);
        return result;
    }

    public static bool TryDeserialize(byte[] raw, out IpcFrame? frame, byte[]? hmacKey = null)
    {
        frame = null;
        if (raw == null || raw.Length < 16 + 32) return false;

        int frameDataLen = raw.Length - 32;
        byte[] frameData = new byte[frameDataLen];
        byte[] receivedHmac = new byte[32];
        Buffer.BlockCopy(raw, 0, frameData, 0, frameDataLen);
        Buffer.BlockCopy(raw, frameDataLen, receivedHmac, 0, 32);

        // Verify HMAC if key provided
        if (hmacKey != null && hmacKey.Length > 0)
        {
            using var hmacSha = new HMACSHA256(hmacKey);
            byte[] computedHmac = hmacSha.ComputeHash(frameData);
            if (!CryptographicOperations.FixedTimeEquals(computedHmac, receivedHmac))
            {
                return false; // HMAC verification failed
            }
        }

        using var ms = new MemoryStream(frameData);
        using var br = new BinaryReader(ms, Encoding.UTF8);

        uint magic = br.ReadUInt32();
        if (magic != MagicHeader) return false;

        ushort version = br.ReadUInt16();
        if (version != CurrentVersion) return false;

        var type = (IpcMessageType)br.ReadUInt16();
        uint seq = br.ReadUInt32();
        int payloadLen = br.ReadInt32();

        if (payloadLen < 0 || payloadLen > 10 * 1024 * 1024) return false; // Max 10MB sanity cap
        byte[] payload = br.ReadBytes(payloadLen);

        frame = new IpcFrame
        {
            MessageType = type,
            SequenceNumber = seq,
            Payload = payload
        };
        return true;
    }
}
