"""Rebuilds the generated sections of docs/rules/program-index.md.

- by-address: every routine row of the topic tables, sorted by address.
- coverage: how many functions of the game code band the index names.
- odf-keys (only with a dump): every ODF key a getter reads, grouped by the routine that reads it.

The topic tables are the source; run this after editing them.

Usage: python3 -I program_index.py <program-index.md> [<dump directory>]
"""

import collections
import os
import re
import sys

BAND_END = 0x5BB4C2

# ODF getters (see the "Object definitions" topic) and the kind of value each reads.
GETTERS = {
    "00553ba0": "int", "00553c30": "float", "00553d40": "string", "00553e20": "name", "00553f20": "lookup",
    "00554060": "float list", "00554320": "vector3", "00554430": "vector2", "00554530": "list",
    "005537a0": "text",
}


def replace(text, section, body):
    start, end = f"<!-- {section}:start -->", f"<!-- {section}:end -->"
    head, rest = text.split(start, 1)
    _, tail = rest.split(end, 1)
    return f"{head}{start}\n{body}{end}{tail}"


def topic_rows(text):
    rows, topic = {}, ""
    for line in text.split("\n"):
        if line.startswith("### "):
            topic = line[4:].strip()
        m = re.match(r"\| (0x[0-9a-f]{6}) \| (\w+) \| [^|]+\| (confirmed|inferred|unknown) \|", line)
        if m and topic:
            rows.setdefault(int(m.group(1), 16), (m.group(2), m.group(3), topic))
    return rows


def by_address(rows):
    lines = ["| Address | Name | Status | Topic |", "|---|---|---|---|"]
    for address in sorted(rows):
        name, status, topic = rows[address]
        lines.append(f"| 0x{address:06x} | {name} | {status} | {topic} |")
    return "\n".join(lines) + "\n"


def coverage(rows, dump):
    named = sum(1 for a in rows if a < BAND_END)
    counts = collections.Counter(r[1] for a, r in rows.items() if a < BAND_END)
    total = ""
    if dump:
        with open(os.path.join(dump, "functions.tsv")) as f:
            band = sum(1 for line in f.readlines()[1:] if int(line.split("\t")[0], 16) < BAND_END)
        total = f" of the {band:,} functions in the game code band"
    return (f"Coverage: the index names {named} routines{total}: {counts['confirmed']} confirmed, "
            f"{counts['inferred']} inferred, {counts['unknown']} unknown.\n")


def odf_keys(rows, dump):
    pattern = re.compile(r"FUN_(0055[34][0-9a-f]{3})\(s_([A-Za-z0-9_]+)_[0-9a-f]{8}")
    readers = collections.defaultdict(set)
    folder = os.path.join(dump, "functions")
    for name in os.listdir(folder):
        with open(os.path.join(folder, name), encoding="utf-8", errors="replace") as f:
            for getter, key in pattern.findall(f.read()):
                if getter in GETTERS:
                    readers[int(name[2:-2], 16)].add(key)

    classes = {}
    with open(os.path.join(dump, "vtables.tsv")) as f:
        for line in f.readlines()[1:]:
            _, cls, _, slots = line.rstrip("\n").split("\t")
            for slot in slots.split(","):
                if slot:
                    classes.setdefault(int(slot, 16), cls)
    callers = {}
    with open(os.path.join(dump, "functions.tsv")) as f:
        for line in f.readlines()[1:]:
            p = line.split("\t")
            callers[int(p[0], 16)] = [c for c in p[3].split(",") if c]

    def owner(address):
        if address in rows:
            return rows[address][0]
        if address in classes:
            return f"a method of `{classes[address]}`"
        for c in callers.get(address, []):
            if int(c, 16) in classes:
                return f"called by `{classes[int(c, 16)]}`"
        return "—"

    lines = ["| Reader | Owner | Keys |", "|---|---|---|"]
    for address in sorted(readers):
        keys = ", ".join(f"`{k}`" for k in sorted(readers[address], key=str.lower))
        lines.append(f"| 0x{address:06x} | {owner(address)} | {keys} |")
    return "\n".join(lines) + "\n"


def main(path, dump=None):
    with open(path) as f:
        text = f.read()
    rows = topic_rows(text)
    text = replace(text, "by-address", by_address(rows))
    text = replace(text, "coverage", coverage(rows, dump))
    if dump:
        text = replace(text, "odf-keys", odf_keys(rows, dump))
    with open(path, "w") as f:
        f.write(text)


if __name__ == "__main__":
    main(*sys.argv[1:])
