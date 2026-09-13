using System.Buffers.Binary;
using Inklume.Application.Projects;

namespace Inklume.Infrastructure.Chapters;

internal static class ImageFileValidator
{
    internal static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    internal static async Task ValidateAsync(
        Stream stream, string originalFileName, string extension, CancellationToken cancellationToken)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidImage,
                $"The image source must provide a readable, seekable stream: {originalFileName}.");
        }

        stream.Seek(0, SeekOrigin.Begin);
        byte[] header = new byte[30];
        int bytesRead = await ReadAtMostAsync(stream, header, cancellationToken);
        bool isValid = extension.ToLowerInvariant() switch
        {
            ".png" => await IsPngAsync(stream, header, bytesRead, cancellationToken),
            ".jpg" or ".jpeg" => await IsJpegAsync(stream, header, bytesRead, cancellationToken),
            ".webp" => IsWebp(stream.Length, header, bytesRead),
            _ => false
        };

        if (!isValid)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidImage, $"The image file is invalid or incomplete: {originalFileName}.");
        }

        stream.Seek(0, SeekOrigin.Begin);
    }

    private static async Task<bool> IsPngAsync(
        Stream stream, byte[] header, int bytesRead, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytesRead < 24 || !header.AsSpan(0, 8).SequenceEqual(signature)
            || !header.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4)) == 0
            || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4)) == 0
            || stream.Length < 45)
        {
            return false;
        }

        byte[] trailer = new byte[12];
        stream.Seek(-trailer.Length, SeekOrigin.End);
        return await ReadAtMostAsync(stream, trailer, cancellationToken) == trailer.Length
            && trailer.AsSpan(4, 4).SequenceEqual("IEND"u8);
    }

    private static async Task<bool> IsJpegAsync(
        Stream stream, byte[] header, int bytesRead, CancellationToken cancellationToken)
    {
        if (bytesRead < 4 || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF || stream.Length < 4)
        {
            return false;
        }

        byte[] trailer = new byte[2];
        stream.Seek(-trailer.Length, SeekOrigin.End);
        return await ReadAtMostAsync(stream, trailer, cancellationToken) == trailer.Length
            && trailer[0] == 0xFF && trailer[1] == 0xD9;
    }

    private static bool IsWebp(long length, byte[] header, int bytesRead)
    {
        if (bytesRead < 21 || !header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !header.AsSpan(8, 4).SequenceEqual("WEBP"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)) + 8 != length)
        {
            return false;
        }

        ReadOnlySpan<byte> chunk = header.AsSpan(12, 4);
        if (chunk.SequenceEqual("VP8 "u8))
        {
            return bytesRead >= 30 && header.AsSpan(23, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A })
                && (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26, 2)) & 0x3FFF) > 0
                && (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28, 2)) & 0x3FFF) > 0;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            return bytesRead >= 25 && header[20] == 0x2F;
        }

        return chunk.SequenceEqual("VP8X"u8) && bytesRead >= 30
            && ReadUInt24LittleEndian(header.AsSpan(24, 3)) < 0xFFFFFF
            && ReadUInt24LittleEndian(header.AsSpan(27, 3)) < 0xFFFFFF;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes)
        => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static async Task<int> ReadAtMostAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
