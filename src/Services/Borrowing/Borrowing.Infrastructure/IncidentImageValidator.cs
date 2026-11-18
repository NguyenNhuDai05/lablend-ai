using System.Buffers.Binary;
using System.IO.Compression;
using Borrowing.Application;

namespace Borrowing.Infrastructure;

// Bounded structural validation of raster formats. No filenames, MIME declarations, URLs or executable formats are trusted.
public static class IncidentImageValidator
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public static string Validate(byte[] data)
    {
        if (data.Length is < 20 or > MaximumBytes) throw Invalid();
        try
        {
            if (data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) { Png(data); return "image/png"; }
            if (data[0] == 0xff && data[1] == 0xd8) { Jpeg(data); return "image/jpeg"; }
            if (data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8)) { Webp(data); return "image/webp"; }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or OverflowException or IndexOutOfRangeException) { throw Invalid(); }
        throw Invalid();
    }
    private static LoanFailure Invalid() => new(400, "Ảnh không hợp lệ. Dùng JPEG, PNG hoặc WebP thực, tối đa 5 MB và 4.096 × 4.096 pixel; PNG dùng 8-bit không xen kẽ.", "INVALID_IMAGE");
    private static void Dimensions(int width, int height) { if (width is < 1 or > 4096 || height is < 1 or > 4096) throw new InvalidDataException(); }
    private static void Png(byte[] data)
    {
        var offset = 8; var width = 0; var height = 0; var channels = 0; var palette = false; var ended = false;
        using var compressed = new MemoryStream();
        while (offset + 12 <= data.Length)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)));
            if (size < 0 || size > data.Length - offset - 12) throw new InvalidDataException();
            var type = data.AsSpan(offset + 4, 4); var body = data.AsSpan(offset + 8, size);
            if (Crc(data.AsSpan(offset + 4, size + 4)) != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + size + 8, 4))) throw new InvalidDataException();
            if (offset == 8 && !type.SequenceEqual("IHDR"u8)) throw new InvalidDataException();
            if (type.SequenceEqual("IHDR"u8))
            {
                if (offset != 8 || size != 13 || body[8] != 8 || body[10] != 0 || body[11] != 0 || body[12] != 0) throw new InvalidDataException();
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[..4])); height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4))); Dimensions(width, height);
                channels = body[9] switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException() };
                palette = body[9] != 3;
            }
            else if (type.SequenceEqual("PLTE"u8)) { if (size == 0 || size > 768 || size % 3 != 0) throw new InvalidDataException(); palette = true; }
            else if (type.SequenceEqual("IDAT"u8)) compressed.Write(body);
            else if (type.SequenceEqual("IEND"u8)) { if (size != 0 || offset + 12 != data.Length) throw new InvalidDataException(); ended = true; break; }
            else if ((type[0] & 32) == 0) throw new InvalidDataException();
            offset += size + 12;
        }
        if (!ended || !palette || compressed.Length == 0 || channels == 0) throw new InvalidDataException();
        compressed.Position = 0;
        using var pixels = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[checked(width * channels + 1)];
        for (var y = 0; y < height; y++) { pixels.ReadExactly(row); if (row[0] > 4) throw new InvalidDataException(); }
        if (pixels.ReadByte() != -1) throw new InvalidDataException();
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var b in bytes) { crc ^= b; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return ~crc;
    }
    private static void Jpeg(byte[] data)
    {
        if (data[^2] != 0xff || data[^1] != 0xd9) throw new InvalidDataException();
        var pos = 2; var dimensions = false; var scan = false;
        while (pos < data.Length - 2)
        {
            if (data[pos++] != 0xff) { if (!scan) throw new InvalidDataException(); continue; }
            while (pos < data.Length && data[pos] == 0xff) pos++;
            var marker = data[pos++];
            if (scan && (marker == 0 || marker is >= 0xd0 and <= 0xd7)) continue;
            if (marker == 0xd9) { if (pos != data.Length || !dimensions || !scan) throw new InvalidDataException(); return; }
            if (pos + 2 > data.Length || marker == 0xd8) throw new InvalidDataException();
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
            if (length < 2 || pos + length > data.Length) throw new InvalidDataException();
            if (marker is 0xc0 or 0xc2)
            {
                if (length < 11 || data[pos + 2] != 8) throw new InvalidDataException();
                Dimensions(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 3, 2))); dimensions = true;
            }
            if (marker == 0xda) { if (!dimensions || length < 6) throw new InvalidDataException(); scan = true; }
            pos += length;
        }
        if (!scan || !dimensions || pos != data.Length - 2) throw new InvalidDataException();
    }
    private static void Webp(byte[] data)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)) != data.Length - 8) throw new InvalidDataException();
        var pos = 12; var image = false;
        while (pos + 8 <= data.Length)
        {
            var type = data.AsSpan(pos, 4); var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4, 4))); pos += 8;
            if (length <= 0 || length > data.Length - pos) throw new InvalidDataException();
            var chunk = data.AsSpan(pos, length);
            if (type.SequenceEqual("VP8 "u8))
            {
                if (length < 11 || (chunk[0] & 1) != 0 || !chunk.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 1, 0x2a })) throw new InvalidDataException();
                Dimensions(BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(6, 2)) & 0x3fff, BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(8, 2)) & 0x3fff); image = true;
            }
            else if (type.SequenceEqual("VP8L"u8))
            {
                if (length < 6 || chunk[0] != 0x2f) throw new InvalidDataException();
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(1, 4)); if (bits >> 29 != 0) throw new InvalidDataException();
                Dimensions((int)(bits & 0x3fff) + 1, (int)((bits >> 14) & 0x3fff) + 1); image = true;
            }
            else if (!type.SequenceEqual("VP8X"u8) && !type.SequenceEqual("ALPH"u8) && !type.SequenceEqual("ICCP"u8) && !type.SequenceEqual("EXIF"u8) && !type.SequenceEqual("XMP "u8)) throw new InvalidDataException();
            pos += length + (length & 1);
        }
        if (pos != data.Length || !image) throw new InvalidDataException();
    }
}
