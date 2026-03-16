"""
Create a minimal Godot 4 PCK file containing mod_manifest.json.

Godot 4 PCK format v2 (from core/io/file_access_pack.cpp).
"""

import hashlib
import json
import struct
import sys


def pad4(n):
    return (4 - (n % 4)) % 4


def create_pck(output_path):
    manifest = json.dumps({
        "pck_name": "AutoSpire",
        "name": "AutoSpire",
        "author": "auto-spire",
        "description": "Automation bridge for Slay the Spire 2",
        "version": "0.1.0"
    }, indent=2).encode("utf-8")

    file_path = b"res://mod_manifest.json"
    path_padded_len = len(file_path) + pad4(len(file_path))
    md5 = hashlib.md5(manifest).digest()

    # Build the file table entry first so we know exactly where data starts
    # Header: 4 + 4 + 4 + 4 + 4 + 4 + 8 + 64 = 96 bytes
    header_size = 96
    # File table: count(4) + path_len(4) + path_padded + offset(8) + size(8) + md5(16) + flags(4)
    file_entry_size = 4 + path_padded_len + 8 + 8 + 16 + 4
    table_size = 4 + file_entry_size

    # Data starts immediately after the table (no alignment - keep it simple)
    data_offset = header_size + table_size

    with open(output_path, "wb") as f:
        # --- Header ---
        f.write(b"GDPC")
        f.write(struct.pack("<i", 2))       # Pack version
        f.write(struct.pack("<i", 4))       # Engine major
        f.write(struct.pack("<i", 5))       # Engine minor
        f.write(struct.pack("<i", 1))       # Engine patch
        f.write(struct.pack("<i", 0))       # Flags (0 = no encryption, absolute offsets)
        f.write(struct.pack("<q", 0))       # Files base offset
        f.write(b"\x00" * 64)              # Reserved

        # --- File table ---
        f.write(struct.pack("<i", 1))       # 1 file

        f.write(struct.pack("<i", len(file_path)))
        f.write(file_path)
        f.write(b"\x00" * pad4(len(file_path)))
        f.write(struct.pack("<q", data_offset))
        f.write(struct.pack("<q", len(manifest)))
        f.write(md5)
        f.write(struct.pack("<i", 0))       # Per-file flags

        assert f.tell() == data_offset, f"Expected {data_offset}, got {f.tell()}"

        # --- File data ---
        f.write(manifest)

    total = data_offset + len(manifest)
    print(f"Created PCK v2: {output_path} ({total} bytes, data@{data_offset})")


if __name__ == "__main__":
    create_pck(sys.argv[1])
