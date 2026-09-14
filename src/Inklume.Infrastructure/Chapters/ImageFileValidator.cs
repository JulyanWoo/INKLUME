using System.Buffers.Binary;
using Inklume.Application.Projects;

namespace Inklume.Infrastructure.Chapters;

internal readonly record struct ImageDimensions(int PixelWidth, int PixelHeight);

internal static class ImageFileValidator
{
    internal static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    internal static async Task<ImageDimensions> ValidateAsync(
        Stream stream, string originalFileName, string extension, CancellationToken cancellationToken)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw InvalidImage(originalFileName, "The image source must provide a readable, seekable stream");
        }

        stream.Seek(0, SeekOrigin.Begin);
        byte[] header = new byte[30];
        int bytesRead = await ReadAtMostAsync(stream, header, cancellationToken);
        ImageDimensions? dimensions = extension.ToLowerInvariant() switch
        {
            ".png" => await ReadPngDimensionsAsync(stream, header, bytesRead, cancellationToken),
            ".jpg" or ".jpeg" => await ReadJpegDimensionsAsync(stream, header, bytesRead, cancellationToken),
            ".webp" => ReadWebpDimensions(stream.Length, header, bytesRead),
            _ => null
        };

        if (dimensions is not { PixelWidth: > 0, PixelHeight: > 0 })
        {
            throw InvalidImage(originalFileName, "The image file is invalid or incomplete");
        }

        stream.Seek(0, SeekOrigin.Begin);
        return dimensions.Value;
    }

    private static async Task<ImageDimensions?> ReadPngDimensionsAsync(
        Stream stream, byte[] header, int bytesRead, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytesRead < 24 || !header.AsSpan(0, 8).SequenceEqual(signature)
            || !header.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            || stream.Length < 45)
        {
            return null;
        }

        uint rawWidth = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4));
        uint rawHeight = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));
        if (rawWidth > int.MaxValue || rawHeight > int.MaxValue)
        {
            return null;
        }

        byte[] trailer = new byte[12];
        stream.Seek(-trailer.Length, SeekOrigin.End);
        bool hasTrailer = await ReadAtMostAsync(stream, trailer, cancellationToken) == trailer.Length
            && trailer.AsSpan(4, 4).SequenceEqual("IEND"u8);
        return hasTrailer ? new ImageDimensions((int)rawWidth, (int)rawHeight) : null;
    }

    private static async Task<ImageDimensions?> ReadJpegDimensionsAsync(
        Stream stream, byte[] header, int bytesRead, CancellationToken cancellationToken)
    {
        if (bytesRead < 4 || header[0] != 0xFF || header[1] != 0xD8 || stream.Length < 4)
        {
            return null;
        }

        byte[] trailer = new byte[2];
        stream.Seek(-trailer.Length, SeekOrigin.End);
        if (await ReadAtMostAsync(stream, trailer, cancellationToken) != trailer.Length
            || trailer[0] != 0xFF || trailer[1] != 0xD9)
        {
            return null;
        }

        stream.Seek(2, SeekOrigin.Begin);
        byte[] markerHeader = new byte[4];
        while (stream.Position < stream.Length - 1)
        {
            int prefix = await ReadByteAsync(stream, cancellationToken);
            if (prefix != 0xFF)
            {
                continue;
            }

            int marker;
            do
            {
                marker = await ReadByteAsync(stream, cancellationToken);
            }
            while (marker == 0xFF);

            if (marker is < 0 or 0xD9 or 0xDA)
            {
                return null;
            }

            if (marker is 0x01 or >= 0xD0 and <= 0xD8)
            {
                continue;
            }

            if (await ReadAtMostAsync(stream, markerHeader.AsMemory(0, 2), cancellationToken) != 2)
            {
                return null;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(markerHeader.AsSpan(0, 2));
            if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length)
            {
                return null;
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7
                    || await ReadAtMostAsync(stream, markerHeader.AsMemory(0, 4), cancellationToken) != 4)
                {
                    return null;
                }

                int height = BinaryPrimitives.ReadUInt16BigEndian(markerHeader.AsSpan(1, 2));
                int widthHigh = markerHeader[3];
                int widthLow = await ReadByteAsync(stream, cancellationToken);
                return widthLow < 0 ? null : new ImageDimensions((widthHigh << 8) | widthLow, height);
            }

            stream.Seek(segmentLength - 2, SeekOrigin.Current);
        }

        return null;
    }

    private static ImageDimensions? ReadWebpDimensions(long length, byte[] header, int bytesRead)
    {
        if (bytesRead < 21 || !header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !header.AsSpan(8, 4).SequenceEqual("WEBP"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)) + 8 != length)
        {
            return null;
        }

        ReadOnlySpan<byte> chunk = header.AsSpan(12, 4);
        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (bytesRead < 30 || !header.AsSpan(23, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
            {
                return null;
            }

            int width = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26, 2)) & 0x3FFF;
            int height = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28, 2)) & 0x3FFF;
            return new ImageDimensions(width, height);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (bytesRead < 25 || header[20] != 0x2F)
            {
                return null;
            }

            int width = 1 + (((header[22] & 0x3F) << 8) | header[21]);
            int height = 1 + ((header[24] << 10) | (header[23] << 2) | (header[22] >> 6));
            return new ImageDimensions(width, height);
        }

        if (chunk.SequenceEqual("VP8X"u8) && bytesRead >= 30)
        {
            return new ImageDimensions(
                1 + ReadUInt24LittleEndian(header.AsSpan(24, 3)),
                1 + ReadUInt24LittleEndian(header.AsSpan(27, 3)));
        }

        return null;
    }

    private static bool IsStartOfFrame(int marker)
        => marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes)
        => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] singleByte = new byte[1];
        int read = await stream.ReadAsync(singleByte, cancellationToken);
        return read == 0 ? -1 : singleByte[0];
    }

    private static Task<int> ReadAtMostAsync(
        Stream stream, byte[] buffer, CancellationToken cancellationToken)
        => ReadAtMostAsync(stream, buffer.AsMemory(), cancellationToken);

    private static async Task<int> ReadAtMostAsync(
        Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static ProjectOperationException InvalidImage(string originalFileName, string reason)
        => new(ProjectErrorCode.InvalidImage, $"{reason}: {originalFileName}.");
}
