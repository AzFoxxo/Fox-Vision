#!/usr/bin/env python3
"""Convert mislabeled FoxFS text files to the new byte-packed format.

Old FoxOS `edit` files are type 1 with one character per 32-bit word. Host-created
files from the early FUSE mount are also type 1, but already byte-packed. New
files are type 3 with four characters per word, so host editors show them
directly. This tool rewrites either case in place; compiled ROMs are left alone.

Unmount the FoxFS FUSE mount before running this on an image.

    foxfs_migrate_text.py [IMAGE]          # list candidates (dry run)
    foxfs_migrate_text.py [IMAGE] --apply  # convert them

IMAGE defaults to $FOXVISION_PORT6_HARDDISK_PATH or the standard port6 image.
"""

import argparse
import os
import struct
import sys

MAGIC = 0x464F5833
VERSION = 8
ENTRY_COUNT = 512
ENTRY_TABLE = 4
ENTRY_WORDS = 4
NAME_TABLE_BASE = 2052
NAMES_PER_SLOT = 16
DATA_BASE = 10244
FILE_CHUNK_WORDS = 0x1FF000

TYPE_FREE = 0
TYPE_PROGRAM = 1
TYPE_DIR = 2
TYPE_TEXT = 3

PRINTABLE = set(range(32, 127)) | {9, 10, 13}  # tab, LF, CR


class Image:
    def __init__(self, path):
        self.file = open(path, "r+b")

    def read_word(self, word):
        self.file.seek(word * 4)
        data = self.file.read(4)
        if len(data) != 4:
            raise ValueError("truncated image at word %d" % word)
        return struct.unpack("<I", data)[0]

    def write_word(self, word, value):
        self.file.seek(word * 4)
        self.file.write(struct.pack("<I", value))

    def write_words(self, word, values):
        self.file.seek(word * 4)
        self.file.write(b"".join(struct.pack("<I", value) for value in values))

    def entry(self, slot):
        base = ENTRY_TABLE + slot * ENTRY_WORDS
        return tuple(self.read_word(base + i) for i in range(4))

    def name(self, slot, length):
        base = NAME_TABLE_BASE + slot * NAMES_PER_SLOT
        return "".join(chr(self.read_word(base + i) & 0xFF) for i in range(length))


def looks_like_legacy_text(words):
    if not words:
        return False
    # One ASCII byte per word: every word is a printable character code.
    return all(word in PRINTABLE for word in words)


def looks_like_packed_text(data):
    if not data:
        return False
    # Four ASCII bytes per word: the raw byte stream is printable.
    return all(byte in PRINTABLE for byte in data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    default_image = os.environ.get(
        "FOXVISION_PORT6_HARDDISK_PATH",
        os.path.expanduser("~/vf16/ports/vf32harddisk/port6.img"),
    )
    parser.add_argument("image", nargs="?", default=default_image)
    parser.add_argument("--apply", action="store_true", help="rewrite candidates")
    args = parser.parse_args()

    image = Image(args.image)
    try:
        header = tuple(image.read_word(i) for i in range(4))
        if header != (MAGIC, VERSION, ENTRY_COUNT, DATA_BASE):
            raise ValueError("not a FoxFS v8 VF32HardDisk image")

        candidates = []
        for slot in range(ENTRY_COUNT):
            entry_type, parent, name_len, size = image.entry(slot)
            if entry_type != TYPE_PROGRAM or size == 0 or size > FILE_CHUNK_WORDS:
                continue
            words = [image.read_word(DATA_BASE + slot * FILE_CHUNK_WORDS + i)
                     for i in range(size)]
            raw = b"".join(struct.pack("<I", word) for word in words)

            if looks_like_legacy_text(words):
                text = bytes(words)
                mode = "word-packed"
            else:
                text = raw.rstrip(b"\0")
                if not looks_like_packed_text(text):
                    continue
                mode = "byte-packed"

            candidates.append((slot, image.name(slot, name_len), words,
                               text, mode))

        if not candidates:
            print("no text files to migrate")
            return 0

        for slot, name, words, text, mode in candidates:
            action = "converting" if args.apply else "would convert"
            print("%s slot %d '%s' (%s, %d words -> %d bytes)"
                  % (action, slot, name, mode, len(words), len(text)))
            if not args.apply:
                continue

            base = DATA_BASE + slot * FILE_CHUNK_WORDS
            padded = text + b"\0" * ((-len(text)) % 4)
            packed = [struct.unpack("<I", padded[i:i + 4])[0]
                      for i in range(0, len(padded), 4)]
            image.write_words(base, packed)
            tail = len(words) - len(packed)
            if tail > 0:
                image.write_words(base + len(packed), [0] * tail)
            image.write_word(ENTRY_TABLE + slot * ENTRY_WORDS, TYPE_TEXT)
            image.write_word(ENTRY_TABLE + slot * ENTRY_WORDS + 3, len(text))

        if args.apply:
            print("converted %d file(s)" % len(candidates))
        else:
            print("dry run only; pass --apply to write changes")
        return 0
    finally:
        image.file.close()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError) as error:
        print("foxfs_migrate_text: %s" % error, file=sys.stderr)
        sys.exit(1)
