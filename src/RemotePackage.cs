using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SayusGagExtender;

public sealed class RemotePackage
{
    private const byte Magic0 = (byte)'S';
    private const byte Magic1 = (byte)'G';
    private const byte FormatVersion = 1;

    private readonly MemoryStream writeStream = new();
    private readonly List<RemotePackageField> readFields = [];
    private int readIndex;

    public byte PackageType { get; }

    public RemotePackage(byte packageType)
    {
        PackageType = packageType;
    }

    private RemotePackage(byte packageType, IEnumerable<RemotePackageField> fields)
    {
        PackageType = packageType;
        readFields.AddRange(fields);
    }

    public void WriteBool(bool value)
    {
        WriteField(RemotePackageFieldType.Bool, [(byte)(value ? 1 : 0)]);
    }

    public void WriteByte(byte value)
    {
        WriteField(RemotePackageFieldType.Byte, [value]);
    }

    public void WriteInt(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        WriteField(RemotePackageFieldType.Int32, buffer.ToArray());
    }

    public void WriteLong(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        WriteField(RemotePackageFieldType.Int64, buffer.ToArray());
    }

    public void WriteDouble(double value)
    {
        WriteLong(BitConverter.DoubleToInt64Bits(value));
    }

    public void WriteString(string? value)
    {
        WriteField(RemotePackageFieldType.String, Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    public void WriteBytes(byte[]? value)
    {
        WriteField(RemotePackageFieldType.Bytes, value ?? []);
    }

    public void WriteDateTimeUtc(DateTime value)
    {
        WriteLong(value.ToUniversalTime().Ticks);
    }

    public void WriteTimeSpan(TimeSpan value)
    {
        WriteLong(value.Ticks);
    }

    public bool ReadBool()
    {
        var data = ReadField(RemotePackageFieldType.Bool);
        if (data.Length != 1) throw new InvalidDataException("Invalid bool field length.");
        return data[0] != 0;
    }

    public byte ReadByte()
    {
        var data = ReadField(RemotePackageFieldType.Byte);
        if (data.Length != 1) throw new InvalidDataException("Invalid byte field length.");
        return data[0];
    }

    public int ReadInt()
    {
        var data = ReadField(RemotePackageFieldType.Int32);
        if (data.Length != 4) throw new InvalidDataException("Invalid int32 field length.");
        return BinaryPrimitives.ReadInt32LittleEndian(data);
    }

    public long ReadLong()
    {
        var data = ReadField(RemotePackageFieldType.Int64);
        if (data.Length != 8) throw new InvalidDataException("Invalid int64 field length.");
        return BinaryPrimitives.ReadInt64LittleEndian(data);
    }

    public double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadLong());
    }

    public string ReadString()
    {
        return Encoding.UTF8.GetString(ReadField(RemotePackageFieldType.String));
    }

    public byte[] ReadBytes()
    {
        return ReadField(RemotePackageFieldType.Bytes).ToArray();
    }

    public DateTime ReadDateTimeUtc()
    {
        return new DateTime(ReadLong(), DateTimeKind.Utc);
    }

    public TimeSpan ReadTimeSpan()
    {
        return TimeSpan.FromTicks(ReadLong());
    }

    public byte[] ToBytes()
    {
        using var output = new MemoryStream();
        output.WriteByte(Magic0);
        output.WriteByte(Magic1);
        output.WriteByte(FormatVersion);
        output.WriteByte(PackageType);
        writeStream.Position = 0;
        writeStream.CopyTo(output);
        return output.ToArray();
    }

    public string ToBase64Url()
    {
        return Base64Url.Encode(ToBytes());
    }

    public static RemotePackage FromBytes(byte[] data)
    {
        if (data.Length < 4) throw new InvalidDataException("Package is too short.");
        if (data[0] != Magic0 || data[1] != Magic1) throw new InvalidDataException("Invalid package magic.");
        if (data[2] != FormatVersion) throw new InvalidDataException($"Unsupported package version {data[2]}.");

        var packageType = data[3];
        var fields = new List<RemotePackageField>();
        var offset = 4;

        while (offset < data.Length)
        {
            if (offset + 3 > data.Length) throw new InvalidDataException("Truncated package field header.");

            var fieldType = (RemotePackageFieldType)data[offset];
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 1, 2));
            offset += 3;

            if (offset + length > data.Length) throw new InvalidDataException("Truncated package field content.");

            fields.Add(new RemotePackageField(fieldType, data.Skip(offset).Take(length).ToArray()));
            offset += length;
        }

        return new RemotePackage(packageType, fields);
    }

    public static RemotePackage FromBase64Url(string value)
    {
        return FromBytes(Base64Url.Decode(value));
    }

    public static bool TryFromBase64Url(string value, out RemotePackage package)
    {
        try
        {
            package = FromBase64Url(value);
            return true;
        }
        catch
        {
            package = new RemotePackage(0);
            return false;
        }
    }

    private void WriteField(RemotePackageFieldType type, byte[] content)
    {
        if (content.Length > ushort.MaxValue) throw new InvalidDataException($"Package field is too large: {content.Length} bytes.");

        Span<byte> header = stackalloc byte[3];
        header[0] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(header[1..], (ushort)content.Length);
        writeStream.Write(header);
        writeStream.Write(content);
    }

    private ReadOnlySpan<byte> ReadField(RemotePackageFieldType expectedType)
    {
        if (readIndex >= readFields.Count) throw new InvalidDataException($"Missing package field {expectedType}.");

        var field = readFields[readIndex++];
        if (field.Type != expectedType) throw new InvalidDataException($"Expected package field {expectedType}, got {field.Type}.");

        return field.Content;
    }

    private readonly record struct RemotePackageField(RemotePackageFieldType Type, byte[] Content);
}

public enum RemotePackageFieldType : byte
{
    Bool = 1,
    Byte = 2,
    Int32 = 3,
    Int64 = 4,
    String = 5,
    Bytes = 6,
}
