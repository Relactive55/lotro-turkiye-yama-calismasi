using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LotroTrGemini;

/// <summary>
/// Stable source identity for a translated catalog row.
///
/// The digest is deliberately independent from the row's absolute position and
/// neighboring text. It binds the source text, the protected argument/token
/// stream and the DAT record shape. This lets a safe move retain its translation
/// while a reworded source is rejected at patch time.
/// </summary>
public static class SourceDigest
{
    public const int HexLength = 64;
    private const string Domain = "lotro-turkish-source-digest-v1";

    public static string ForRecord(CatalogRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        return Compute(
            record.Did,
            record.Source,
            record.RecordFingerprint,
            record.StructuralFingerprint,
            record.TokenSignature);
    }

    public static string Compute(
        int did,
        string source,
        string recordFingerprint,
        string structuralFingerprint,
        string tokenSignature)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
        using (SHA256 sha = SHA256.Create())
        {
            WriteField(writer, Domain);
            WriteField(writer, did.ToString("X8"));
            WriteField(writer, CatalogIdentity.NormalizeSource(source));
            WriteField(writer, recordFingerprint ?? string.Empty);
            WriteField(writer, structuralFingerprint ?? string.Empty);
            WriteField(writer, tokenSignature ?? ProtectedFormat.GetTokenSignature(source));
            writer.Flush();
            return ToHex(sha.ComputeHash(stream.ToArray()));
        }
    }

    public static bool IsValid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != HexLength) return false;
        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }
        return true;
    }

    public static bool Matches(string left, string right)
    {
        return IsValid(left) && IsValid(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteField(BinaryWriter writer, string value)
    {
        string normalized = value ?? string.Empty;
        byte[] bytes = Encoding.UTF8.GetBytes(normalized);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder text = new StringBuilder(bytes.Length * 2);
        foreach (byte value in bytes) text.Append(value.ToString("x2"));
        return text.ToString();
    }
}
