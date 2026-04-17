using System.Security.Cryptography;

namespace HngStageZeroClean.Helpers;

public static class UuidV7Generator
{
    public static Guid Create()
    {
        var unixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var timeHigh = (int)(unixTimeMilliseconds >> 16);
        var timeLow = (short)(unixTimeMilliseconds & 0xFFFF);

        Span<byte> randomBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(randomBytes);

        var randA = (short)(((0x7 << 12) | ((randomBytes[0] << 4) | (randomBytes[1] >> 4))) & 0xFFFF);

        byte[] randB = new byte[8];
        randomBytes.CopyTo(randB);

        randB[0] = (byte)((randB[0] & 0x3F) | 0x80);

        return new Guid(timeHigh, timeLow, randA, randB);
    }
}