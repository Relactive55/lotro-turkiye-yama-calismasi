using System;
using System.IO;
using System.Linq;
using System.Text;
using LotroTrGemini;

internal static class HiddenTooltipTests
{
    internal static int Run()
    {
        byte[] source = Fixture(false), expected = Fixture(true);
        byte[] actual = KnownUiFixes.ApplyHiddenTooltipFixes(source);
        Check(actual.SequenceEqual(expected), "hidden tooltip translations preserve record IDs, empty variants, parameter IDs and surrounding bytes");
        Check(KnownUiFixes.ApplyHiddenTooltipFixes(actual).SequenceEqual(actual), "hidden tooltip corrections are idempotent");
        byte[] bad = (byte[])source.Clone(); bad[1] ^= 1;
        Reject(bad, "changed hidden record ID is rejected");
        byte[] parameter = (byte[])source.Clone(); parameter[33] ^= 1;
        Reject(parameter, "changed hidden parameter is rejected");
        Reject(source.Concat(source).ToArray(), "duplicate hidden records are rejected");
        Reject(source.Take(40).ToArray(), "missing hidden records are rejected");
        byte[] gondolin = GondolinFixture(false), gondolinExpected = GondolinFixture(true);
        Check(KnownUiFixes.ApplyTranslatedPayload(unchecked((int)0x2503B6C1u), gondolin).SequenceEqual(gondolinExpected),
            "Gondolin title suffix translates through its complete native record");
        Check(KnownUiFixes.ApplyTranslatedPayload(unchecked((int)0x2503B6C1u), gondolinExpected).SequenceEqual(gondolinExpected),
            "Gondolin title suffix correction is idempotent");
        Console.WriteLine("hidden_tooltip_tests_passed=8");
        return 8;
    }
    private static byte[] Fixture(bool translated)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)0xaa);
            Record(writer, 0x0640A225, new[] { "", translated ? "m Menzil" : "m Range" }, 0x005A6195);
            Record(writer, 0x0ADE6325, new[] { "", translated ? "m Menzil" : "m Range" }, 0x005A6195);
            Record(writer, 0x028B5915, new[] { "", " ", translated ? " Hasar" : " Damage" }, 0x04624A34, 0x05BE1855);
            Record(writer, 0x0F616255, new[] { "", translated ? " Hasar" : " Damage" }, 0x0E0F7997);
            Record(writer, 0x01894215, new[] { "", " - ", " ", translated ? " Hasar" : " Damage" }, 0x02864455, 0x048615B5, 0x05BE1855);
            writer.Write((byte)0xbb); writer.Flush(); return stream.ToArray();
        }
    }
    private static void Record(BinaryWriter writer, uint token, string[] variants, params uint[] parameters)
    {
        writer.Write(token); writer.Write(0u); writer.Write(variants.Length);
        foreach (string text in variants) { writer.Write((byte)text.Length); writer.Write(Encoding.Unicode.GetBytes(text)); }
        writer.Write(parameters.Length);
        foreach (uint parameter in parameters) writer.Write(parameter);
        writer.Write((byte)0);
    }
    private static byte[] GondolinFixture(bool translated)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)0xcc);
            Record(writer, 0x0A4B0FF5,
                new[] { "#1:", "#1:{ [E]}#2:", " #3:", translated ? " (Gondolinli)" : " of Gondolin" },
                0x0005662B, 0x00052615, 0x08A72645);
            writer.Write((byte)0xdd); writer.Flush(); return stream.ToArray();
        }
    }
    private static void Reject(byte[] input, string label)
    {
        try { KnownUiFixes.ApplyHiddenTooltipFixes(input); }
        catch (InvalidDataException) { Check(true, label); return; }
        throw new Exception(label);
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }
}
