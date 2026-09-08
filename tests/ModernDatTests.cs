using System;
using System.IO;
using System.Linq;
using System.Text;
using LotroTrGemini;

internal static class ModernDatTests
{
    private const int Did = 0x250047CF;
    internal static int Run()
    {
        int passed = 0;
        byte[] payload = BitConverter.GetBytes(Did).Concat(new byte[] { 1, 0, 0, 0, 11, 12, 13, 14, 0x45, 0x26, 0xa7, 8 }).ToArray();
        foreach (bool extra in new[] { false, true })
        {
            string path = Path.Combine(Path.GetTempPath(), "lotro-modern-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                File.WriteAllBytes(path, Fixture(payload, extra));
                using (var dat = new TurbineDat())
                {
                    dat.Open(path, true); dat.BuildEntryIndex();
                    dat.TryGetEntry(Did, out DatEntry entry);
                    dat.ValidateLocalizationChains();
                    foreach (Action forbidden in new Action[] {
                        () => dat.WriteChain(entry.Offset, payload),
                        () => dat.ExpandChain(entry.Offset, payload.Length),
                        () => dat.MeasureCapacity(entry.Offset) })
                    {
                        try { forbidden(); throw new Exception("legacy modern-storage operation accepted"); }
                        catch (NotSupportedException) { passed++; }
                    }
                    Check(dat.UsesModernStorage && dat.ReadRaw(entry).SequenceEqual(payload),
                        "native eight-byte framing preserves DID and all final bytes; extra=" + extra); passed++;
                    byte[] changed = payload.Concat(Enumerable.Repeat((byte)0x79, 33)).ToArray();
                    Check(dat.WriteOrRelocateContiguous(Did, changed), "native relocation succeeds"); passed++;
                    dat.TryGetEntry(Did, out DatEntry moved);
                    Check(moved.Offset != entry.Offset && moved.Size == changed.Length
                        && moved.Size2 == ((changed.Length + 3) & ~3) + 8,
                        "relocation size excludes the eight-byte native header"); passed++;
                    dat.ValidateLocalizationChains();
                    Check(dat.ReadRaw(moved).SequenceEqual(changed), "relocated native payload is complete"); passed++;
                    // Read physical bytes independently, without the DAT reader.
                    using (var physical = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new BinaryReader(physical))
                    {
                        physical.Position = moved.Offset;
                        Check(reader.ReadUInt32() == 0 && reader.ReadUInt32() == 0
                            && reader.ReadBytes(changed.Length).SequenceEqual(changed),
                            "independent physical reader sees a valid native header and exact payload"); passed++;
                    }
                    Check(dat.WriteOrRelocateContiguous(Did, payload), "native in-place write succeeds"); passed++;
                    dat.TryGetEntry(Did, out DatEntry resized);
                    Check(resized.Offset == moved.Offset && resized.Size2 == moved.Size2 && dat.ReadRaw(resized).SequenceEqual(payload),
                        "native in-place write keeps allocation and final bytes"); passed++;
                }
            }
            finally { File.Delete(path); }
        }
        string corrupt = Path.Combine(Path.GetTempPath(), "lotro-modern-corrupt-" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            byte[] bytes = Fixture(payload, true);
            Buffer.BlockCopy(BitConverter.GetBytes(2048u), 0, bytes, 2068, 4);
            File.WriteAllBytes(corrupt, bytes);
            using (var dat = new TurbineDat())
            {
                dat.Open(corrupt, false);
                try { dat.ValidateLocalizationChains(); throw new Exception("overlapping block accepted"); }
                catch (InvalidDataException) { Check(true, "native overlapping extra block rejected"); passed++; }
            }
        }
        finally { File.Delete(corrupt); }
        Console.WriteLine("modern_dat_tests_passed=" + passed);
        return passed;
    }

    private static byte[] Fixture(byte[] payload, bool extra)
    {
        byte[] result = new byte[8192];
        using (var stream = new MemoryStream(result))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            stream.Position = 0x101; writer.Write((ushort)0x4c50);
            stream.Position = 320; writer.Write(TurbineDat.MagicBt); writer.Write(1024u); writer.Write((uint)result.Length);
            for (int i = 0; i < 5; i++) writer.Write(0u);
            writer.Write(1024u);
            stream.Position = 1024 + 504; writer.Write(1u);
            writer.Write(0x70002u); writer.Write(Did); writer.Write(2048u); writer.Write((uint)payload.Length);
            writer.Write(0u); writer.Write(1); writer.Write(24u); writer.Write(0u);
            stream.Position = 2048; writer.Write(extra ? 1u : 0u); writer.Write(0u);
            writer.Write(payload, 0, extra ? 8 : payload.Length);
            if (extra)
            {
                writer.Write((uint)(payload.Length - 8)); writer.Write(4096u);
                stream.Position = 4096; writer.Write(payload, 8, payload.Length - 8);
            }
        }
        return result;
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
