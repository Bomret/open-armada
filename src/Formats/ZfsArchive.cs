using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenArmada.Formats;

// A .zfs archive read from memory. Layout per the REA report of Armada.exe (FUN_004b7c70, FUN_004b87d0):
// a 0x1C-byte header ("ZFSF", version, name length, entries per block, file count, key, first block),
// then chained blocks of a next-block offset (0 = end) and 100 entries of 36 bytes (name[16], data
// offset, index, stored size, timestamp, flags). Member lookup ignores case.
public sealed class ZfsArchive
{
    const int HeaderSize = 0x1C,
        BlockSize = 0xE14,
        EntrySize = 36,
        EntriesPerBlock = 100;

    readonly byte[] _data;
    readonly uint _key;
    readonly Dictionary<string, (int Offset, int Size, uint Flags)> _entries = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _faults = new();

    ZfsArchive(byte[] data, uint key)
    {
        _data = data;
        _key = key;
    }

    // Names of the readable members (deleted and faulty entries are left out).
    public IEnumerable<string> Names => _entries.Keys;

    public bool Contains(string name) => _entries.ContainsKey(name);

    // One line per entry that was skipped, naming the entry and the reason.
    public IReadOnlyList<string> Faults => _faults;

    // Throws InvalidDataException when the archive as a whole is unreadable: bad header, a block chain
    // that is cut off, loops or does not match the file count. An entry that cannot be read is skipped
    // and listed in Faults, so the other members still load.
    public static ZfsArchive Open(byte[] data)
    {
        if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 4) != "ZFSF")
            throw new InvalidDataException("not a ZFS archive (missing ZFSF header)");
        uint U(int at) => BitConverter.ToUInt32(data, at);
        if (U(4) != 1 || U(8) != 16 || U(12) != EntriesPerBlock)
            throw new InvalidDataException(
                $"unsupported ZFS header (version {U(4)}, name length {U(8)}, entries per block {U(12)}; expected 1, 16, 100)"
            );
        var zfs = new ZfsArchive(data, U(20));
        var blocks = U(16) / EntriesPerBlock + 1L; // the game's own block count rule
        var seen = new HashSet<long>();
        long pos = U(24);
        for (var b = 0L; b < blocks; b++)
        {
            if (pos == 0)
                throw new InvalidDataException($"block chain ends after {b} blocks, the header counts {blocks}");
            if (pos < HeaderSize || pos + BlockSize > data.Length)
                throw new InvalidDataException($"block {b} at offset {pos} lies past the end of the archive (cut off)");
            if (!seen.Add(pos))
                throw new InvalidDataException($"block chain loops at offset {pos}");
            zfs.ReadBlock((int)pos);
            pos = U((int)pos);
        }

        if (pos != 0)
            throw new InvalidDataException($"block chain continues past the {blocks} blocks the header counts");

        return zfs;
    }

    void ReadBlock(int block)
    {
        for (var i = 0; i < EntriesPerBlock; i++)
        {
            var e = block + 4 + i * EntrySize;
            if (_data[e] == 0)
                return; // the game stops at the first unnamed entry
            var nameLength = Array.IndexOf(_data, (byte)0, e, 16) is var z and >= 0 ? z - e : 16;
            var name = Windows1252.Encoding.GetString(_data, e, nameLength);
            uint offset = BitConverter.ToUInt32(_data, e + 16),
                size = BitConverter.ToUInt32(_data, e + 24),
                flags = BitConverter.ToUInt32(_data, e + 32);
            if ((flags & 1) != 0)
                continue; // deleted
            var compressed = (flags & 6) != 0;
            if ((flags & 0xF8) != 0 || (!compressed && flags >> 8 != 0))
            {
                _faults.Add($"{name}: unknown flag bits 0x{flags:x}");
            }
            else if ((long)offset + size > _data.Length)
            {
                _faults.Add($"{name}: data at {offset} + {size} bytes lies past the end of the archive");
            }
            else if (!_entries.TryAdd(name, ((int)offset, (int)size, flags)))
            {
                _faults.Add($"{name}: listed twice, the first entry is kept");
            }
        }
    }

    // The member's bytes. Throws FileNotFoundException for an unknown name and
    // UnsupportedFormatException for a compressed entry (no decompressor).
    public byte[] Read(string name)
    {
        if (!_entries.TryGetValue(name, out var e))
            throw new FileNotFoundException($"'{name}' is not in the archive", name);
        if ((e.Flags & 6) != 0)
            throw new UnsupportedFormatException($"{name}: compressed archive entry");
        var bytes = _data.AsSpan(e.Offset, e.Size).ToArray();
        if (_key != 0)
        {
            foreach (ref var word in MemoryMarshal.Cast<byte, uint>(bytes)) // whole words only; the trailing remainder is plain
            {
                word ^= _key; // little-endian host, as the archive's words are
            }
        }

        return bytes;
    }
}
