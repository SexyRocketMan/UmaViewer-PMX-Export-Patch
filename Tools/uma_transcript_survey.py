"""Survey what audio the game ships and which of it carries text.

Read-only. Walks every `sound/**/*.awb` asset in the game's asset index, resolves each clip to its cue
name through the sibling `.acb`, and asks the master data whether that cue has a line of text.

The cue walk mirrors `Assets/Scripts/Audio/CriWareFormats/AcbNameLoader.cs`: a cue references a
waveform, a synth, a sequence or a block sequence, and only a waveform (reached through any of those)
carries the AWB wave id that the awb file's subfile entries use.

Text sources, in the order the lookup tries them:
  * `character_system_text` - `lip_sync_data` is the cue name, `text` is the line (character voice)
  * `race_jikkyo_message` / `race_jikkyo_comment` - race commentary, `voice` lists the ids a cue name
    ends with
"""
import os
import sqlite3
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cri_utf import Utf  # noqa: E402

GAME = r"C:\Users\user\Umamusume\umamusume_Data\Persistent"
MDB = os.path.join(GAME, "master", "master.mdb")
META = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "meta_plain.sqlite")


def connect(path):
    return sqlite3.connect(f"file:{path}?mode=ro", uri=True)


def asset_path(con, name):
    row = con.execute("select h from a where n = ?", (name,)).fetchone()
    if not row:
        return None
    return os.path.join(GAME, "dat", row[0][:2], row[0])


class Acb:
    """cue index -> cue name, and cue index -> awb wave ids."""

    def __init__(self, buf):
        h = Utf(buf, 0)
        self.buf = buf
        self.names = {}
        t = h.subtable("CueNameTable")
        if t is not None:
            for r in t.rows_named():
                self.names[r["CueIndex"]] = r["CueName"]
        self.wave_by_cue = {}
        cue = h.subtable("CueTable")
        self.synth = h.subtable("SynthTable")
        self.waveform = h.subtable("WaveformTable")
        self.sequence = h.subtable("SequenceTable")
        self.track = h.subtable("TrackTable")
        self.track_event = h.subtable("TrackEventTable") or h.subtable("CommandTable")
        self.block_sequence = h.subtable("BlockSequenceTable")
        self.block = h.subtable("BlockTable")
        self.wave_ids = {}
        if self.waveform is not None:
            for i, row in enumerate(self.waveform.rows_named()):
                wid = row.get("Id", 0xFFFF)
                if wid == 0xFFFF:
                    wid = row.get("MemoryAwbId", 0xFFFF)
                if wid == 0xFFFF:
                    wid = row.get("StreamAwbId", 0xFFFF)
                self.wave_ids[i] = wid
        if cue is not None:
            for i, row in enumerate(cue.rows_named()):
                out = []
                self._follow(row["ReferenceType"], row["ReferenceIndex"], out)
                self.wave_by_cue[i] = out

    def _vl(self, off, size):
        return [(int.from_bytes(self.buf[off + k:off + k + 2], "big"),
                 int.from_bytes(self.buf[off + k + 2:off + k + 4], "big"))
                for k in range(0, size, 4)]

    def _follow(self, kind, index, out, depth=0):
        if depth > 3:
            return
        if kind == 1:
            if index in self.wave_ids:
                out.append(self.wave_ids[index])
        elif kind == 2:
            if self.synth is None or index >= self.synth.rows:
                return
            off, size = self.synth.row_dict(index)["ReferenceItems"]
            for item_type, item_index in self._vl(off, size):
                if item_type == 1:
                    if item_index in self.wave_ids:
                        out.append(self.wave_ids[item_index])
                elif item_type in (2, 3):
                    self._follow(item_type, item_index, out, depth + 1)
        elif kind == 3:
            if self.sequence is None or index >= self.sequence.rows:
                return
            off, size = self.sequence.row_dict(index)["TrackIndex"]
            for k in range(0, size, 2):
                self._track(int.from_bytes(self.buf[off + k:off + k + 2], "big", signed=True), out)
        elif kind == 8:
            if self.block_sequence is None or index >= self.block_sequence.rows:
                return
            row = self.block_sequence.row_dict(index)
            off, size = row["TrackIndex"]
            for k in range(0, size, 2):
                self._track(int.from_bytes(self.buf[off + k:off + k + 2], "big", signed=True), out)
            if "BlockIndex" in row and self.block is not None:
                boff, bsize = row["BlockIndex"]
                for k in range(0, bsize, 2):
                    bi = int.from_bytes(self.buf[boff + k:boff + k + 2], "big", signed=True)
                    if 0 <= bi < self.block.rows:
                        toff, tsize = self.block.row_dict(bi)["TrackIndex"]
                        for j in range(0, tsize, 2):
                            self._track(int.from_bytes(self.buf[toff + j:toff + j + 2],
                                                       "big", signed=True), out)

    def _track(self, index, out, depth=0):
        if self.track is None or not (0 <= index < self.track.rows):
            return
        ev = self.track.row_dict(index)["EventIndex"]
        if ev == 0xFFFF or self.track_event is None or ev >= self.track_event.rows:
            return
        off, size = self.track_event.row_dict(ev)["Command"]
        pos = 0
        while pos < size:
            code = int.from_bytes(self.buf[off + pos:off + pos + 2], "big")
            tlv = self.buf[off + pos + 2]
            pos += 3
            if code in (2000, 2003) and tlv >= 4:
                ttype = int.from_bytes(self.buf[off + pos:off + pos + 2], "big")
                tindex = int.from_bytes(self.buf[off + pos + 2:off + pos + 4], "big")
                if ttype in (2, 3):
                    self._follow(ttype, tindex, out, depth + 1)
            pos += tlv


def awb_wave_ids(path):
    with open(path, "rb") as f:
        head = f.read(0x10)
        id_align, total = struct.unpack_from("<Hi", head, 6)
        ids = []
        for s in range(total):
            f.seek(0x10 + s * id_align)
            ids.append(struct.unpack("<H", f.read(2))[0])
    return ids


def load_texts():
    """cue name -> line, and the race commentary pools keyed by the ids their `voice` column lists."""
    texts = {}
    con = connect(MDB)
    for cue_name, text in con.execute("select lip_sync_data, text from character_system_text"):
        texts[cue_name] = text

    jikkyo = {}
    for pool in ("race_jikkyo_message", "race_jikkyo_comment"):
        for rid, message, voice in con.execute(f"select id, message, voice from {pool}"):
            jikkyo.setdefault(rid, message)
            for part in voice.split("|"):
                if part.isdigit():
                    jikkyo.setdefault(int(part), message)
    return texts, jikkyo


def resolve(cue_name, texts, jikkyo):
    if cue_name in texts:
        return "master"
    for prefix in ("snd_voi_race_jky_", "snd_voi_race_cmt_"):
        if cue_name.startswith(prefix):
            tail = cue_name[len(prefix):]
            if tail.isdigit() and int(tail) in jikkyo:
                return "jikkyo"
    return None


def main():
    texts, jikkyo = load_texts()
    con = connect(META)
    names = [r[0] for r in con.execute(
        "select n from a where n like 'sound/%' and n like '%.awb' order by n")]

    stats = {}
    keys = ("awbs", "clips", "named", "master", "jikkyo", "no_acb")
    total = dict.fromkeys(keys, 0)
    for name in names:
        base = os.path.basename(name)
        parts = base.split("_")
        fam = "_".join(parts[:3]) if len(parts) >= 3 else base
        st = stats.setdefault(fam, dict.fromkeys(keys, 0))
        st["awbs"] += 1
        total["awbs"] += 1

        awb_path = asset_path(con, name)
        if not awb_path or not os.path.exists(awb_path):
            continue
        ids = awb_wave_ids(awb_path)
        st["clips"] += len(ids)
        total["clips"] += len(ids)

        acb_path = asset_path(con, name[:-4] + ".acb")
        if not acb_path or not os.path.exists(acb_path):
            st["no_acb"] += 1
            total["no_acb"] += 1
            continue
        acb = Acb(open(acb_path, "rb").read())
        by_wave = {}
        for cue_index, waves in acb.wave_by_cue.items():
            for wid in waves:
                by_wave.setdefault(wid, acb.names.get(cue_index, ""))
        for wid in ids:
            cue_name = by_wave.get(wid, "")
            if not cue_name:
                continue
            st["named"] += 1
            total["named"] += 1
            source = resolve(cue_name, texts, jikkyo)
            if source:
                st[source] += 1
                total[source] += 1

    print(f"{'family':30} {'awbs':>5} {'clips':>7} {'named':>7} {'master':>7} {'jikkyo':>7} {'text %':>7}")
    for fam in sorted(stats, key=lambda k: -stats[k]["clips"]):
        st = stats[fam]
        pct = 100.0 * (st["master"] + st["jikkyo"]) / st["clips"] if st["clips"] else 0
        print(f"{fam:30} {st['awbs']:5} {st['clips']:7} {st['named']:7} {st['master']:7} "
              f"{st['jikkyo']:7} {pct:6.1f}%")
    print()
    pct = 100.0 * (total["master"] + total["jikkyo"]) / total["clips"] if total["clips"] else 0
    print(f"total: {total['awbs']} awbs, {total['clips']} clips, {total['named']} named, "
          f"{total['master']} from character_system_text, {total['jikkyo']} from race commentary, "
          f"{total['no_acb']} awbs without an acb, {pct:.1f}% of all clips carry text")


if __name__ == "__main__":
    main()
