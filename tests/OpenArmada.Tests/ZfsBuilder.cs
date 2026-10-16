using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenArmada.Tests;

// Builds .zfs archives in code (INV-0001): the 0x1C-byte header, chained 0xE14-byte blocks of 100
// 36-byte entries, and file data XORed with the key over whole 4-byte words.
internal static class ZfsBuilder
{
    internal sealed record Member(string Name, byte[] Data, uint Flags = 0);

    internal static Member M(string name, string text, uint flags = 0) =>
        new(name, Encoding.Latin1.GetBytes(text), flags);

    internal static byte[] Build(uint key, params Member[] members)
    {
        var blocks = members.Length / 100 + 1;
        var dataStart = 28 + blocks * 0xE14;
        var file = new byte[dataStart];
        void Put(int at, uint v) => BitConverter.GetBytes(v).CopyTo(file, at);
        Encoding.ASCII.GetBytes("ZFSF").CopyTo(file, 0);
        Put(4, 1);
        Put(8, 16);
        Put(12, 100);
        Put(16, (uint)members.Length);
        Put(20, key);
        Put(24, 28);
        var data = new List<byte>();
        for (var i = 0; i < members.Length; i++)
        {
            var block = 28 + i / 100 * 0xE14;
            if (i % 100 == 0)
                Put(block, i / 100 + 1 < blocks ? (uint)(28 + (i / 100 + 1) * 0xE14) : 0);
            var e = block + 4 + i % 100 * 36;
            Encoding.Latin1.GetBytes(members[i].Name).CopyTo(file, e);
            Put(e + 16, (uint)(dataStart + data.Count));
            Put(e + 20, (uint)i);
            Put(e + 24, (uint)members[i].Data.Length);
            Put(e + 32, members[i].Flags);
            data.AddRange(Xor(members[i].Data, key));
        }
        return file.Concat(data).ToArray();
    }

    static byte[] Xor(byte[] data, uint key)
    {
        var o = (byte[])data.Clone();
        if (key == 0)
            return o;
        for (var i = 0; i + 4 <= o.Length; i += 4)
            BitConverter.GetBytes(BitConverter.ToUInt32(o, i) ^ key).CopyTo(o, i);
        return o;
    }
}
