#!/usr/bin/env python3
"""Print the transcript an exported mp3 carries in its ID3 tag.

The viewer writes the line a clip voices into the tag when it exports from the Sound page: the line as the
title, the speaker as the artist, the story as the album, the asset and clip as a comment, and every line of
the clip as the lyrics. This reads that back, which is also the check that the export did what it promised.

Standard library only - no `mutagen`, no `uv run`, nothing to install.

    python Tools/mp3_transcript.py out.mp3
    python Tools/mp3_transcript.py "D:/rips/*_transcribed.mp3"
    python Tools/mp3_transcript.py out.mp3 --json
    python Tools/mp3_transcript.py "D:/rips/*.mp3" --quiet      # just the pass/fail summary

Exit code is 0 when every file carries a transcript, 1 when one does not, 2 when a file cannot be read.
"""

from __future__ import annotations

import argparse
import glob
import json
import sys

TEXT_FRAMES = {
    "TIT2": "title",
    "TPE1": "artist",
    "TALB": "album",
    "TPE2": "album artist",
    "TCOM": "composer",
    "TDRC": "year",
    "TYER": "year",
}
DESCRIBED_FRAMES = {"USLT": "lyrics", "COMM": "comment"}


def _text(encoding: int, data: bytes) -> str:
    if encoding == 0:
        return data.decode("latin-1", "replace")
    if encoding == 1:
        return data.decode("utf-16", "replace")   # BOM picks the byte order
    if encoding == 2:
        return data.decode("utf-16-be", "replace")
    return data.decode("utf-8", "replace")


def _terminator(encoding: int) -> int:
    """Length of the encoding's string terminator."""
    return 2 if encoding in (1, 2) else 1


def _skip_string(encoding: int, data: bytes, at: int) -> int:
    """Index just past a nul-terminated string of this encoding."""
    step = _terminator(encoding)
    while at + step <= len(data):
        if data[at:at + step] == b"\x00" * step:
            return at + step
        at += step
    return len(data)


def _syncsafe(data: bytes) -> int:
    return ((data[0] & 0x7F) << 21) | ((data[1] & 0x7F) << 14) | ((data[2] & 0x7F) << 7) | (data[3] & 0x7F)


def read_tags(path: str) -> dict[str, str]:
    """The tag frames this tool understands, by their friendly name. Empty when the file has no tag."""
    with open(path, "rb") as handle:
        head = handle.read(10)
        if len(head) < 10 or head[:3] != b"ID3":
            return {}

        version = head[3]
        flags = head[5]
        size = _syncsafe(head[6:10])
        body = handle.read(size)

    if flags & 0x80:
        # unsynchronisation inserts a 0x00 after every 0xFF; the viewer never writes it, so be explicit
        body = body.replace(b"\xff\x00", b"\xff")

    if flags & 0x40:
        # an extended header sits in front of the frames and this tool has no use for its contents
        print(f"{path}: extended ID3 header, frames not read", file=sys.stderr)
        return {}

    at = 0
    if flags & 0x40:
        # extended header: its own size comes first, and this tool does not need anything in it
        return tags_unavailable("extended header not supported")

    tags: dict[str, str] = {}
    while at + 10 <= len(body):
        frame_id = body[at:at + 4]
        if not frame_id.strip(b"\x00") or not frame_id.isalnum():
            break
        raw_size = body[at + 4:at + 8]
        frame_size = _syncsafe(raw_size) if version >= 4 else int.from_bytes(raw_size, "big")
        if frame_size <= 0 or at + 10 + frame_size > len(body):
            break

        payload = body[at + 10:at + 10 + frame_size]
        at += 10 + frame_size
        name = frame_id.decode("ascii", "replace")

        if name in DESCRIBED_FRAMES and len(payload) > 4:
            encoding = payload[0]
            # encoding byte, language code, then a nul-terminated description of the frame
            text_at = _skip_string(encoding, payload, 1 + 3)
            tags[DESCRIBED_FRAMES[name]] = _text(encoding, payload[text_at:]).strip("\x00")
        elif name in TEXT_FRAMES and payload:
            tags[TEXT_FRAMES[name]] = _text(payload[0], payload[1:]).strip("\x00")

    return tags


def expand(patterns: list[str]) -> list[str]:
    paths: list[str] = []
    for pattern in patterns:
        hits = sorted(glob.glob(pattern)) or ([pattern] if "*" not in pattern else [])
        paths.extend(hits)
    return paths


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Print the transcript inside an exported mp3's tag.")
    parser.add_argument("files", nargs="+", help="mp3 file(s) or glob(s)")
    parser.add_argument("--json", action="store_true", help="one JSON object per file")
    parser.add_argument("--quiet", action="store_true", help="only the summary line")
    args = parser.parse_args(argv)

    paths = expand(args.files)
    if not paths:
        print("no files matched", file=sys.stderr)
        return 2

    carried = 0
    for path in paths:
        try:
            tags = read_tags(path)
        except Exception as error:                    # noqa: BLE001 - a tool: report and keep going
            print(f"{path}: cannot read ({error})", file=sys.stderr)
            return 2

        has = bool(tags.get("title") or tags.get("lyrics"))
        carried += 1 if has else 0

        if args.json:
            print(json.dumps({"file": path, "transcript": tags}, ensure_ascii=False))
            continue
        if args.quiet:
            continue

        print(path)
        if not has:
            print("  (no transcript in this file)")
            continue
        for key in ("title", "artist", "album", "comment", "lyrics"):
            if tags.get(key):
                value = tags[key].replace("\r\n", " / ").replace("\n", " / ")
                print(f"  {key + ':':8} {value}")

    total = len(paths)
    summary = f"{carried}/{total} file(s) carry a transcript"
    if args.json:
        print(summary, file=sys.stderr)
    else:
        print(summary)
    return 0 if carried == total else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
