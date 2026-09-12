"""Writes src/Rezrov.Glulx/Glk/GlkUnicodeData.cs from the Unicode
Character Database.

Usage, from the repository root:

    python tools/unicode_tables.py <directory>

where the directory holds UnicodeData.txt, SpecialCasing.txt, and
CompositionExclusions.txt as published at
https://www.unicode.org/Public/<version>/ucd/. The version is read from
SpecialCasing.txt and recorded in the file written.

The Glk case and normalization functions need four things the database
has: the full case mappings that differ from the one-to-one ones, the
canonical combining classes, the canonical decompositions, and the pairs
that compose. Everything is written as flat arrays of hex words, sorted
so that the library can binary search them.

The runtime supplies the one-to-one case mappings, and its invariant
mode leaves two letters alone that Unicode does not: the dotless i and
the long s, whose upper-case forms are the plain I and S. They are
written into the tables so that they map as Unicode says.
"""

import re
import sys
from pathlib import Path

OUTPUT = Path("src/Rezrov.Glulx/Glk/GlkUnicodeData.cs")
HANGUL_FIRST, HANGUL_LAST = 0xAC00, 0xD7A3
RUNTIME_GAPS = {0x0131, 0x017F}


class Character:
    def __init__(self, fields):
        self.code = int(fields[0], 16)
        self.category = fields[2]
        self.combining_class = int(fields[3])
        decomposition = fields[5]
        self.parts = None
        if decomposition and not decomposition.startswith("<"):
            self.parts = [int(part, 16) for part in decomposition.split()]
        self.upper = int(fields[12], 16) if fields[12] else self.code
        self.lower = int(fields[13], 16) if fields[13] else self.code
        # An empty title field means the title mapping is the upper one.
        self.title = int(fields[14], 16) if fields[14] else self.upper
        self.special = None


def read_database(directory):
    characters = {}
    for line in (directory / "UnicodeData.txt").read_text(encoding="utf-8").splitlines():
        fields = line.split(";")
        character = Character(fields)
        characters[character.code] = character

    # SpecialCasing.txt: code; lower; title; upper; [condition;] # name.
    # Only the unconditional mappings are Unicode's full case mappings;
    # the conditional ones depend on language or context.
    version = None
    for line in (directory / "SpecialCasing.txt").read_text(encoding="utf-8").splitlines():
        if version is None and (found := re.match(r"# SpecialCasing-(\d+\.\d+\.\d+)\.txt", line)):
            version = found.group(1)
        body = line.split("#", 1)[0].strip()
        if not body:
            continue
        fields = [field.strip() for field in body.split(";")]
        if len(fields) > 4 and fields[4]:
            continue
        character = characters[int(fields[0], 16)]
        character.special = tuple(
            [int(part, 16) for part in fields[i].split()] for i in (3, 1, 2)
        )

    excluded = set()
    for line in (directory / "CompositionExclusions.txt").read_text(encoding="utf-8").splitlines():
        body = line.split("#", 1)[0].strip()
        if body:
            first, _, last = body.partition("..")
            excluded.update(range(int(first, 16), int(last or first, 16) + 1))

    return characters, excluded, version


def full_decomposition(characters, code):
    character = characters.get(code)
    if character is None or character.parts is None:
        return [code]
    result = []
    for part in character.parts:
        result.extend(full_decomposition(characters, part))
    return result


def combining_ranges(characters):
    ranges = []
    for code in sorted(characters):
        cls = characters[code].combining_class
        if cls == 0:
            continue
        if ranges and ranges[-1][1] == code - 1 and ranges[-1][2] == cls:
            ranges[-1][1] = code
        else:
            ranges.append([code, code, cls])
    return ranges


def decompositions(characters):
    index, pool = [], [0]
    for code in sorted(characters):
        if HANGUL_FIRST <= code <= HANGUL_LAST or characters[code].parts is None:
            continue
        parts = full_decomposition(characters, code)
        index.append((code, len(pool)))
        pool.append(len(parts))
        pool.extend(parts)
    return index, pool


def compositions(characters, excluded):
    pairs = []
    for code, character in characters.items():
        parts = character.parts
        if parts is None or len(parts) != 2 or code in excluded:
            continue
        # Unicode chapter 3.11: a composite whose decomposition starts
        # with a mark, or that is itself a mark, is excluded too.
        first = characters.get(parts[0])
        if character.combining_class != 0 or (first and first.combining_class != 0):
            continue
        pairs.append((parts[0], parts[1], code))
    pairs.sort()
    return pairs


def full_mappings(character):
    if character.special:
        return character.special
    return ([character.upper], [character.lower], [character.title])


def special_cases(characters):
    index, pool = [], [0]

    def add(mapping):
        offset = len(pool)
        pool.append(len(mapping))
        pool.extend(mapping)
        return offset

    for code in sorted(characters):
        upper, lower, title = full_mappings(characters[code])
        upper_offset = add(upper) if len(upper) != 1 or code in RUNTIME_GAPS else 0
        lower_offset = add(lower) if len(lower) != 1 else 0
        title_offset = add(title) if title != upper else 0
        if upper_offset or lower_offset or title_offset:
            index.append((code, upper_offset, lower_offset, title_offset))
    return index, pool


def words(values, per_line=8):
    lines = []
    for start in range(0, len(values), per_line):
        chunk = values[start:start + per_line]
        lines.append("        " + " ".join(f"0x{v:X}," for v in chunk))
    return "\n".join(lines)


def flatten(rows):
    return [value for row in rows for value in row]


def main(argv):
    if len(argv) != 2:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    directory = Path(argv[1])
    characters, excluded, version = read_database(directory)
    ranges = combining_ranges(characters)
    decomp_index, decomp_pool = decompositions(characters)
    pairs = compositions(characters, excluded)
    special_index, special_pool = special_cases(characters)

    text = f'''namespace Rezrov.Glulx.Glk;

/// <summary>
/// The parts of the Unicode Character Database that the Glk case and
/// normalization functions read, as flat sorted arrays of words.
/// </summary>
/// <remarks>
/// This file is written by tools/unicode_tables.py from the database
/// files of Unicode {version}; change the script, not the file. Where
/// a table points into a pool, the pool holds a count and then that
/// many characters, and an offset of zero means no entry.
/// </remarks>
internal static class GlkUnicodeData
{{
    /// <summary>The Unicode version the tables were taken from.</summary>
    public const string Version = "{version}";

    /// <summary>
    /// Runs of characters with a nonzero canonical combining class:
    /// first character, last character, class. Sorted by character.
    /// </summary>
    public static ReadOnlySpan<uint> CombiningClasses =>
    [
{words(flatten(ranges))}
    ];

    /// <summary>
    /// Characters with a canonical decomposition, other than the
    /// Hangul syllables, which decompose by arithmetic: character,
    /// offset into <see cref="DecompositionParts"/>. Sorted by
    /// character.
    /// </summary>
    public static ReadOnlySpan<uint> Decompositions =>
    [
{words(flatten(decomp_index))}
    ];

    /// <summary>
    /// The decompositions, taken all the way down, so that no part
    /// decomposes further.
    /// </summary>
    public static ReadOnlySpan<uint> DecompositionParts =>
    [
{words(decomp_pool)}
    ];

    /// <summary>
    /// The pairs that compose: first character, second character, the
    /// composite. Sorted by the pair. Composition exclusions are left
    /// out, as are the Hangul syllables.
    /// </summary>
    public static ReadOnlySpan<uint> Compositions =>
    [
{words(flatten(pairs))}
    ];

    /// <summary>
    /// Characters whose full case mappings differ from the one-to-one
    /// ones the runtime has: character, then offsets into
    /// <see cref="SpecialCaseParts"/> for the upper, lower, and title
    /// mappings. Sorted by character. A mapping that is not there is
    /// the runtime's, and a title mapping that is not there is the
    /// upper one.
    /// </summary>
    public static ReadOnlySpan<uint> SpecialCases =>
    [
{words(flatten(special_index))}
    ];

    /// <summary>The full case mappings.</summary>
    public static ReadOnlySpan<uint> SpecialCaseParts =>
    [
{words(special_pool)}
    ];
}}
'''
    OUTPUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"{OUTPUT}: Unicode {version}, {len(ranges)} combining runs, "
          f"{len(decomp_index)} decompositions, {len(pairs)} compositions, "
          f"{len(special_index)} special cases")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
