"""Minimal CRI @UTF table reader, for poking at an ACB's cue/waveform tables from Python.

The Unity code in `Assets/Scripts/Audio/CriWareFormats/UtfTable.cs` is the reference this mirrors.
"""
import struct

COLUMN_TYPES = {
    0x00: ("b", 1),
    0x01: ("b", 1),
    0x02: ("H", 2),
    0x03: ("h", 2),
    0x04: ("I", 4),
    0x05: ("i", 4),
    0x06: ("Q", 8),
    0x07: ("q", 8),
    0x08: ("f", 4),
    0x09: ("d", 8),
    0x0A: ("str", 4),
    0x0B: ("vldata", 8),
    0x0C: ("u128", 16),
}


class Utf:
    def __init__(self, buf, offset=0):
        self.buf = buf
        self.base = offset
        assert buf[offset:offset + 4] == b"@UTF", buf[offset:offset + 4]
        self.table_size = struct.unpack_from(">I", buf, offset + 4)[0] + 8
        self.version = struct.unpack_from(">H", buf, offset + 8)[0]
        self.rows_offset = struct.unpack_from(">H", buf, offset + 0x0A)[0] + 8
        self.strings_offset = struct.unpack_from(">I", buf, offset + 0x0C)[0] + 8
        self.data_offset = struct.unpack_from(">I", buf, offset + 0x10)[0] + 8
        name_offset = struct.unpack_from(">I", buf, offset + 0x14)[0]
        self.columns = struct.unpack_from(">H", buf, offset + 0x18)[0]
        self.row_width = struct.unpack_from(">H", buf, offset + 0x1A)[0]
        self.rows = struct.unpack_from(">I", buf, offset + 0x1C)[0]

        self.strings_size = self.data_offset - self.strings_offset
        self.strings = buf[offset + self.strings_offset:offset + self.strings_offset + self.strings_size]
        self.name = self._string(name_offset)

        self.schema = []  # (name, type, flag, offset)
        pos = 0x20
        row_col_offset = 0
        for _ in range(self.columns):
            info = buf[offset + pos]
            col_name_off = struct.unpack_from(">I", buf, offset + pos + 1)[0]
            pos += 5
            flag = info & 0xF0
            ctype = info & 0x0F
            size = COLUMN_TYPES[ctype][1]
            name = self._string(col_name_off) if flag & 0x10 else ""
            const_off = None
            if flag & 0x20:
                const_off = pos
                pos += size
            row_off = None
            if flag & 0x40:
                row_off = row_col_offset
                row_col_offset += size
            self.schema.append((name, ctype, flag, const_off, row_off))

    def _string(self, off):
        end = self.strings.index(b"\x00", off)
        return self.strings[off:end].decode("utf-8", "replace")

    def column(self, name):
        for i, col in enumerate(self.schema):
            if col[0] == name:
                return i
        return -1

    def value(self, row, col_index):
        name, ctype, flag, const_off, row_off = self.schema[col_index]
        if const_off is not None:
            pos = self.base + const_off  # const_off already carries the 0x20 schema base
        else:
            pos = self.base + self.rows_offset + row * self.row_width + row_off
        fmt, size = COLUMN_TYPES[ctype]
        if fmt == "str":
            return self._string(struct.unpack_from(">I", self.buf, pos)[0])
        if fmt == "vldata":
            off, size_ = struct.unpack_from(">II", self.buf, pos)
            return (self.base + self.data_offset + off, size_)
        return struct.unpack_from(">" + fmt, self.buf, pos)[0]

    def row_dict(self, row):
        return {col[0]: self.value(row, i) for i, col in enumerate(self.schema)}

    def rows_named(self, count=None):
        out = []
        for r in range(self.rows if count is None else min(count, self.rows)):
            out.append(self.row_dict(r))
        return out

    def subtable(self, name):
        col = self.column(name)
        if col < 0:
            return None
        off, size = self.value(0, col)
        if size == 0:  # CRI collapses every empty table onto one offset, which need not be a table
            return None
        return Utf(self.buf, off)
