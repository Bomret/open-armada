"""Writes vtables.tsv for an MSVC x86 executable from its RTTI data.

Ghidra labels no vtables in Armada.exe, so this reads them straight from the file. A type descriptor's
name is the string ".?AV<class>@@" 8 bytes into the descriptor. A complete object locator is five dwords:
0, the subobject offset, the constructor displacement offset, a pointer to the type descriptor, and a
pointer to the class hierarchy. A vtable starts 4 bytes after a pointer to a locator, and its slots run
while they point into .text.

Usage: python3 -I rtti_vtables.py <exe> <vtables.tsv>
"""

import re
import struct
import sys


def main(exe, out):
    d = open(exe, "rb").read()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    count = struct.unpack_from("<H", d, pe + 6)[0]
    base = struct.unpack_from("<I", d, pe + 24 + 28)[0]
    table = pe + 24 + struct.unpack_from("<H", d, pe + 20)[0]
    sections = [struct.unpack_from("<8sIIII", d, table + 40 * i) for i in range(count)]

    def offset(va):
        for _, _, rva, raw_size, raw in sections:
            if base + rva <= va < base + rva + raw_size:
                return raw + va - base - rva
        return None

    def address(off):
        for _, _, rva, raw_size, raw in sections:
            if raw <= off < raw + raw_size:
                return base + rva + off - raw
        return None

    text = next(s for s in sections if s[0].startswith(b".text"))
    text_lo, text_hi = base + text[2], base + text[2] + text[1]

    descriptors = {}
    for m in re.finditer(rb"\.\?A[VU]([^\x00]+?)@@\x00", d):
        descriptors[address(m.start() - 8)] = m.group(1).decode("latin1")

    locators = {}
    for off in range(0, len(d) - 20, 4):
        signature, sub, _, descriptor, hierarchy = struct.unpack_from("<5I", d, off)
        if signature == 0 and descriptor in descriptors and offset(hierarchy) is not None:
            locators[address(off)] = (descriptors[descriptor], sub)

    rows = []
    for off in range(0, len(d) - 4, 4):
        locator = struct.unpack_from("<I", d, off)[0]
        if locator not in locators:
            continue
        slots = []
        q = off + 4
        while q + 4 <= len(d):
            target = struct.unpack_from("<I", d, q)[0]
            if not text_lo <= target < text_hi:
                break
            slots.append(target)
            q += 4
            if struct.unpack_from("<I", d, q)[0] in locators:
                break
        name, sub = locators[locator]
        rows.append((address(off) + 4, name, sub, slots))

    with open(out, "w") as w:
        w.write("address\tclass\tsubobject_offset\tslots\n")
        for vt, name, sub, slots in sorted(rows):
            w.write(f"0x{vt:08x}\t{name}\t{sub}\t{','.join(f'0x{s:x}' for s in slots)}\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
