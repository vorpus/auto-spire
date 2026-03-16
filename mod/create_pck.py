"""
Create a minimal Godot 4 PCK file containing mod_manifest.json.

Godot 4 PCK format:
  - Magic: GDPC (4 bytes)
  - Pack version: int32 (2 for Godot 4)
  - Engine major: int32 (4)
  - Engine minor: int32 (5)
  - Engine patch: int32 (1)
  - Flags: int32 (0)
  - File offset: int64 (offset to file data)
  - Reserved: 16 * int32 (zeros)
  - File count: int32
  - For each file:
    - Path length: int32
    - Path: UTF-8 string (padded to 4-byte alignment)
    - File offset: int64
    - File size: int64
    - MD5: 16 bytes
  - File data (aligned to next boundary)
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

    file_path = "res://mod_manifest.json".encode("utf-8")
    file_path_padded_len = len(file_path) + pad4(len(file_path))

    # Calculate header size
    # Magic(4) + version(4) + major(4) + minor(4) + patch(4) + flags(4) + offset(8) + reserved(64)
    header_size = 4 + 4 + 4 + 4 + 4 + 4 + 8 + 64
    # File count(4) + file entry: path_len(4) + path(padded) + offset(8) + size(8) + md5(16)
    file_table_size = 4 + (4 + file_path_padded_len + 8 + 8 + 16)
    data_offset = header_size + file_table_size

    # Align data offset to 64 bytes (Godot convention)
    data_align = (64 - (data_offset % 64)) % 64
    data_offset += data_align

    md5 = hashlib.md5(manifest).digest()

    with open(output_path, "wb") as f:
        # Header
        f.write(b"GDPC")
        f.write(struct.pack("<i", 2))       # Pack version
        f.write(struct.pack("<i", 4))       # Engine major
        f.write(struct.pack("<i", 5))       # Engine minor
        f.write(struct.pack("<i", 1))       # Engine patch
        f.write(struct.pack("<i", 0))       # Flags
        f.write(struct.pack("<q", 0))       # File offset (0 = files follow header)
        f.write(b"\x00" * 64)              # Reserved

        # File table
        f.write(struct.pack("<i", 1))       # File count

        # File entry
        f.write(struct.pack("<i", len(file_path)))
        f.write(file_path)
        f.write(b"\x00" * pad4(len(file_path)))
        f.write(struct.pack("<q", data_offset))  # Offset to file data
        f.write(struct.pack("<q", len(manifest))) # File size
        f.write(md5)

        # Alignment padding
        current = f.tell()
        f.write(b"\x00" * (data_offset - current))

        # File data
        f.write(manifest)

    print(f"Created PCK: {output_path} ({data_offset + len(manifest)} bytes)")


if __name__ == "__main__":
    create_pck(sys.argv[1])
