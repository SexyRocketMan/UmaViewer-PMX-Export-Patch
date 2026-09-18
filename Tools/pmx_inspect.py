# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Inspect / diff PMX files without Blender or Unity.

  uv run pmx_inspect.py summary <file.pmx> [more.pmx ...]
  uv run pmx_inspect.py bones   <file.pmx>
  uv run pmx_inspect.py weights <file.pmx> [--material M_Eye]
  uv run pmx_inspect.py morphs  <file.pmx> [--grep Eye_2]
  uv run pmx_inspect.py diff    <a.pmx> <b.pmx>
  uv run pmx_inspect.py check   <file.pmx>        # uma_addon morph-name contract + eye skinning

`check` verifies the invariants the Blender uma_addon relies on:
  * the four eye-range morphs are named "<name>(<tag>)[M_Face]" (used by "Refine Structure"
    to build the Eye_L(L)/Eye_L(R)/... controls),
  * the eyeball geometry is skinned to the Eye_L / Eye_R bones.
"""

from __future__ import annotations

import argparse
import math
import re
import sys

# these tools print japanese asset names, and a windows console is not utf-8 by default
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except (AttributeError, ValueError):
    pass

from collections import Counter
from dataclasses import dataclass, field

# The morph names the Blender uma_addon's "Refine Structure" operator looks up by exact name.
# See uma_addon/.../operators/AddonOperators.py -> RefineBoneStructure.fix_eye_shapekeys.
ADDON_EYE_RANGE_MORPHS = [
    "Eye_20_R(XRange)[M_Face]",
    "Eye_20_L(XRange)[M_Face]",
    "Eye_21_R(YRange)[M_Face]",
    "Eye_21_L(YRange)[M_Face]",
]


class Reader:
    def __init__(self, data: bytes) -> None:
        self.d = data
        self.p = 0

    def bytes(self, n: int) -> bytes:
        b = self.d[self.p:self.p + n]
        if len(b) != n:
            raise EOFError(f"wanted {n} bytes at {self.p}, got {len(b)}")
        self.p += n
        return b

    def u8(self) -> int:
        v = self.d[self.p]
        self.p += 1
        return v

    def i8(self) -> int:
        v = self.d[self.p]
        self.p += 1
        return v - 256 if v > 127 else v

    def i32(self) -> int:
        v = int.from_bytes(self.bytes(4), "little", signed=True)
        return v

    def u32(self) -> int:
        return int.from_bytes(self.bytes(4), "little", signed=False)

    def u16(self) -> int:
        return int.from_bytes(self.bytes(2), "little", signed=False)

    def f32(self) -> float:
        import struct
        return struct.unpack("<f", self.bytes(4))[0]

    def vec(self, n: int) -> list[float]:
        return [self.f32() for _ in range(n)]

    def index(self, size: int, signed: bool = True) -> int:
        if size == 1:
            return self.i8() if signed else self.u8()
        if size == 2:
            return int.from_bytes(self.bytes(2), "little", signed=signed)
        if size == 4:
            return self.i32() if signed else self.u32()
        raise ValueError(f"bad index size {size}")


@dataclass
class Bone:
    index: int
    name: str
    name_en: str
    pos: list[float]
    parent: int
    flags: int
    tail_bone: int | None = None
    tail_pos: list[float] | None = None
    inherit_parent: int | None = None
    inherit_weight: float | None = None
    ik: dict | None = None


@dataclass
class Material:
    index: int
    name: str
    name_en: str
    face_count: int
    texture: int
    sphere_texture: int | None = None
    sphere_mode: int = 0
    toon_texture: int | None = None
    shared_toon: int | None = None
    comment: str = ""


@dataclass
class Vertex:
    pos: list[float]
    weight_type: int
    bones: list[int]
    weights: list[float]


@dataclass
class Model:
    version: float
    encoding: str
    extra_uv: int
    sizes: dict
    name: str
    name_en: str
    vertices: list[Vertex] = field(default_factory=list)
    surfaces: list[int] = field(default_factory=list)
    textures: list[str] = field(default_factory=list)
    materials: list[Material] = field(default_factory=list)
    bones: list[Bone] = field(default_factory=list)
    morph_names: list[str] = field(default_factory=list)
    morph_cats: list[int] = field(default_factory=list)


def read_model(path: str) -> Model:
    with open(path, "rb") as fh:
        data = fh.read()
    r = Reader(data)
    if r.bytes(4) != b"PMX ":
        raise ValueError(f"not a PMX file: {path}")
    version = r.f32()
    gcount = r.u8()
    g = [r.u8() for _ in range(gcount)]
    encoding = "utf-16-le" if g[0] == 0 else "utf-8"
    sizes = {"vertex": g[2], "texture": g[3], "material": g[4],
             "bone": g[5], "morph": g[6], "rigidbody": g[7]}

    def text() -> str:
        return r.bytes(r.i32()).decode(encoding, errors="replace")

    model = Model(version=version, encoding=encoding, extra_uv=g[1], sizes=sizes,
                  name=text(), name_en=text())
    text(); text()  # comments

    for _ in range(r.i32()):
        pos = r.vec(3)
        r.vec(3); r.vec(2)
        if g[1]:
            r.vec(4 * g[1])
        wt = r.u8()
        bones: list[int] = []
        weights: list[float] = []
        if wt == 0:
            bones = [r.index(sizes["bone"])]; weights = [1.0]
        elif wt == 1:
            b0 = r.index(sizes["bone"]); b1 = r.index(sizes["bone"]); w0 = r.f32()
            bones = [b0, b1]; weights = [w0, 1.0 - w0]
        elif wt == 2:
            bones = [r.index(sizes["bone"]) for _ in range(4)]
            weights = [r.f32() for _ in range(4)]
        elif wt == 3:
            b0 = r.index(sizes["bone"]); b1 = r.index(sizes["bone"]); w0 = r.f32()
            bones = [b0, b1]; weights = [w0, 1.0 - w0]
            r.vec(3); r.vec(3); r.vec(3)
        elif wt == 4:
            bones = [r.index(sizes["bone"]) for _ in range(4)]
            weights = [r.f32() for _ in range(4)]
        else:
            raise ValueError(f"unknown weight type {wt}")
        r.f32()
        model.vertices.append(Vertex(pos, wt, bones, weights))

    for _ in range(r.i32()):
        model.surfaces.append(r.index(sizes["vertex"], signed=False))

    for _ in range(r.i32()):
        model.textures.append(text())

    for i in range(r.i32()):
        name = text(); name_en = text()
        r.vec(4); r.vec(3); r.f32(); r.vec(3)
        r.u8(); r.vec(4); r.f32()
        tex = r.index(sizes["texture"])
        sphere = r.index(sizes["texture"]); sphere_mode = r.u8()
        # the toon field is a flag byte: 1 means a shared toon (a plain byte index), 0 means a texture index
        shared_toon = r.u8()
        if shared_toon == 1:
            shared_toon_index = r.u8()
            toon = None
        else:
            shared_toon_index = None
            toon = r.index(sizes["texture"])
        comment = text()
        model.materials.append(Material(i, name, name_en, r.i32(), tex,
                                        sphere_texture=sphere, sphere_mode=sphere_mode,
                                        toon_texture=toon, shared_toon=shared_toon_index,
                                        comment=comment))

    for i in range(r.i32()):
        b = Bone(i, text(), text(), r.vec(3), r.index(sizes["bone"]), 0)
        r.i32()  # transform level
        b.flags = r.u16()
        if b.flags & 0x0001:
            b.tail_bone = r.index(sizes["bone"])
        else:
            b.tail_pos = r.vec(3)
        if b.flags & (0x0100 | 0x0200):
            b.inherit_parent = r.index(sizes["bone"]); b.inherit_weight = r.f32()
        if b.flags & 0x0400:
            r.vec(3)
        if b.flags & 0x0800:
            r.vec(3); r.vec(3)
        if b.flags & 0x2000:
            r.i32()
        if b.flags & 0x0020:
            ik = {"target": r.index(sizes["bone"]), "loop": r.i32(), "limit_angle": r.f32(), "links": []}
            for _ in range(r.i32()):
                link = {"bone": r.index(sizes["bone"])}
                if r.u8() == 1:
                    r.vec(3); r.vec(3)
                ik["links"].append(link)
            b.ik = ik
        model.bones.append(b)

    for _ in range(r.i32()):
        model.morph_names.append(text())
        text()
        model.morph_cats.append(r.u8())
        mtype = r.u8()
        off = r.i32()
        if mtype == 0:
            for _ in range(off):
                r.index(sizes["morph"]); r.f32()
        elif mtype == 1:
            for _ in range(off):
                r.index(sizes["vertex"]); r.vec(3)
        elif mtype == 2:
            for _ in range(off):
                r.index(sizes["bone"]); r.vec(3); r.vec(4)
        elif mtype == 3:
            for _ in range(off):
                r.index(sizes["vertex"]); r.vec(4)
        elif mtype in (4, 5, 6, 7):
            for _ in range(off):
                r.index(sizes["material"]); r.u8(); r.vec(4)
        elif mtype == 8:
            for _ in range(off):
                r.index(sizes["bone"]); r.i32(); r.f32()
        elif mtype == 9:
            for _ in range(off):
                r.index(sizes["material"]); r.u8(); r.vec(4)
        elif mtype == 10:
            for _ in range(off):
                r.index(sizes["morph"]); r.f32()
        else:
            raise ValueError(f"unknown morph type {mtype}")
    return model


def material_vertices(model: Model, needle: str) -> list[int] | None:
    cursor = 0
    for m in model.materials:
        idx = model.surfaces[cursor:cursor + m.face_count]
        cursor += m.face_count
        if needle in m.name:
            return sorted(set(idx))
    return None


def cmd_summary(paths: list[str], args) -> int:
    for path in paths:
        m = read_model(path)
        print(f"== {path}")
        print(f"   PMX {m.version} encoding={m.encoding} extraUV={m.extra_uv} sizes={m.sizes}")
        print(f"   name={m.name!r}/{m.name_en!r}")
        print(f"   verts={len(m.vertices)} indices={len(m.surfaces)} textures={len(m.textures)} "
              f"materials={len(m.materials)} bones={len(m.bones)} morphs={len(m.morph_names)}")
        print(f"   morph categories: {dict(Counter(m.morph_cats))}")

    if getattr(args, "materials", False):
        for path in paths:
            m = read_model(path)
            print(f"== {path}")
            for material in m.materials:
                texture = m.textures[material.texture] if 0 <= material.texture < len(m.textures) else None
                sphere = (m.textures[material.sphere_texture]
                          if material.sphere_texture is not None and 0 <= material.sphere_texture < len(m.textures)
                          else None)
                toon = (m.textures[material.toon_texture]
                        if material.toon_texture is not None and 0 <= material.toon_texture < len(m.textures)
                        else None)
                print(f"   [{material.index:2}] {material.name!r}")
                print(f"        texture={texture} sphere={sphere} (mode {material.sphere_mode}) toon={toon}")
                if material.comment:
                    print(f"        comment={material.comment}")
    return 0


def cmd_bones(paths: list[str], args) -> int:
    m = read_model(paths[0])
    for b in m.bones:
        parent = m.bones[b.parent].name if 0 <= b.parent < len(m.bones) else str(b.parent)
        tail = (m.bones[b.tail_bone].name if (b.flags & 1 and b.tail_bone is not None
                                              and 0 <= b.tail_bone < len(m.bones)) else b.tail_bone)
        print(f"[{b.index:3}] {b.name!r:26} en={b.name_en!r:26} parent={parent!r:20} "
              f"pos=({b.pos[0]:.4f},{b.pos[1]:.4f},{b.pos[2]:.4f}) flags={b.flags:#06x} tail={tail}")
    return 0


def cmd_weights(paths: list[str], args) -> int:
    m = read_model(paths[0])
    cursor = 0
    for mat in m.materials:
        if args.material and args.material not in mat.name:
            cursor += mat.face_count
            continue
        vids = sorted(set(m.surfaces[cursor:cursor + mat.face_count]))
        cursor += mat.face_count
        totals: dict[str, float] = {}
        wtypes = Counter()
        for vi in vids:
            v = m.vertices[vi]
            wtypes[v.weight_type] += 1
            for bi, w in zip(v.bones, v.weights):
                if w <= 1e-4:
                    continue
                name = m.bones[bi].name if 0 <= bi < len(m.bones) else f"<{bi} OOR>"
                totals[name] = totals.get(name, 0.0) + w
        print(f"\n== material[{mat.index}] {mat.name!r} verts={len(vids)} tris={mat.face_count // 3} "
              f"weight_types={dict(wtypes)}")
        for name, w in sorted(totals.items(), key=lambda kv: -kv[1]):
            print(f"   {name!r:34} weight={w:10.1f}")
    return 0


def cmd_morphs(paths: list[str], args) -> int:
    for path in paths:
        m = read_model(path)
        names = [n for n in m.morph_names if not args.grep or args.grep.lower() in n.lower()]
        print(f"== {path}: {len(m.morph_names)} morphs, {len(names)} matching")
        for n in names:
            print(f"   {n}")
    return 0


def cmd_diff(paths: list[str], args) -> int:
    a, b = read_model(paths[0]), read_model(paths[1])
    print(f"A = {paths[0]}\nB = {paths[1]}")
    map_a = {x.name: x for x in a.bones}
    map_b = {x.name: x for x in b.bones}
    print(f"bones         : A={len(a.bones)} B={len(b.bones)} onlyA={sorted(set(map_a) - set(map_b))[:10]} "
          f"onlyB={sorted(set(map_b) - set(map_a))[:10]}")
    pos_diff, par_diff = [], []
    for name in sorted(set(map_a) & set(map_b)):
        x, y = map_a[name], map_b[name]
        if math.dist(x.pos, y.pos) > 1e-4:
            pos_diff.append(name)
        px = a.bones[x.parent].name if 0 <= x.parent < len(a.bones) else str(x.parent)
        py = b.bones[y.parent].name if 0 <= y.parent < len(b.bones) else str(y.parent)
        if px != py:
            par_diff.append((name, px, py))
    print(f"bone pos diff : {len(pos_diff)} {pos_diff[:10]}")
    print(f"bone parent   : {len(par_diff)} {par_diff[:10]}")
    print(f"vertices      : A={len(a.vertices)} B={len(b.vertices)}")
    if len(a.vertices) == len(b.vertices):
        moved = sum(1 for va, vb in zip(a.vertices, b.vertices) if math.dist(va.pos, vb.pos) > 1e-4)
        print(f"vertex pos diff > 1e-4: {moved}")
        different_weights = 0
        for va, vb in zip(a.vertices, b.vertices):
            wa = {a.bones[i].name: round(w, 4) for i, w in zip(va.bones, va.weights) if w > 1e-4}
            wb = {b.bones[i].name: round(w, 4) for i, w in zip(vb.bones, vb.weights) if w > 1e-4}
            if wa != wb:
                different_weights += 1
        print(f"weight map diff       : {different_weights}")
    print(f"materials     : A={len(a.materials)} B={len(b.materials)}")
    print(f"morphs        : A={len(a.morph_names)} B={len(b.morph_names)}")
    only_a = [n for n in a.morph_names if n not in set(b.morph_names)]
    only_b = [n for n in b.morph_names if n not in set(a.morph_names)]
    print(f"  morph names only in A ({len(only_a)}): {only_a[:6]}")
    print(f"  morph names only in B ({len(only_b)}): {only_b[:6]}")
    return 0


def cmd_check(paths: list[str], args) -> int:
    ok = True
    for path in paths:
        m = read_model(path)
        print(f"== {path}")
        names = set(m.morph_names)
        missing = [n for n in ADDON_EYE_RANGE_MORPHS if n not in names]
        short = [n for n in ("Eye_20_R", "Eye_20_L", "Eye_21_R", "Eye_21_L") if n in names]
        print(f"   uma_addon tagged eye-range morphs present: "
              f"{len(ADDON_EYE_RANGE_MORPHS) - len(missing)}/{len(ADDON_EYE_RANGE_MORPHS)}")
        if missing:
            print(f"      MISSING: {missing}")
            if short:
                print(f"      found short names instead: {short}  <- uma_addon will silently skip the eye controls")
            ok = False

        vids = material_vertices(m, "_eye")
        if vids is None:
            print("   !! no eye material found")
            ok = False
            continue
        totals: dict[str, float] = {}
        for vi in vids:
            for bi, w in zip(m.vertices[vi].bones, m.vertices[vi].weights):
                if w > 1e-4:
                    totals[m.bones[bi].name] = totals.get(m.bones[bi].name, 0.0) + w
        eye_bound = {k: v for k, v in totals.items() if k in ("Eye_L", "Eye_R")}
        print(f"   eye material ({len(vids)} verts) skinned to: {dict(sorted(totals.items(), key=lambda kv: -kv[1])[:5])}")
        if len(eye_bound) != 2:
            print("   !! eyeball is not bound to both Eye_L and Eye_R")
            ok = False
        else:
            print(f"   eyeball -> Eye_L/Eye_R ok ({eye_bound})")
    return 0 if ok else 1


def cmd_names(paths: list[str], args) -> int:
    """Validate morph names: they must be unique and fit a vmd's 15 byte shift-jis name field."""
    ok = True
    required = [n for n in (args.require or "").split(",") if n]
    for path in paths:
        m = read_model(path)
        names = m.morph_names
        lengths = {n: len(n.encode("shift_jis", errors="replace")) for n in names}
        over = {n: length for n, length in lengths.items() if length > 15}
        dupes = [n for n, c in Counter(names).items() if c > 1]

        tagged = sum(1 for n in names if "(" in n and "[" in n)
        if tagged == len(names) and names:
            scheme = "tagged (BlenderCompatible)"
        elif all(re.match(r"^(Eye|Brow|Ear|Mouth)_", n) for n in names) and names:
            scheme = "unified"
        else:
            scheme = "short/other"

        print(f"== {path}")
        print(f"   morphs={len(names)} distinct={len(set(names))} "
              f"maxBytes={max(lengths.values()) if lengths else 0} scheme={scheme}")
        if over:
            ok = False
            print(f"   !! {len(over)} morph name(s) do not fit a vmd name field (>15 bytes shift-jis):")
            for n, length in sorted(over.items(), key=lambda kv: -kv[1])[:10]:
                print(f"        {length:3}B  {n}")
        if dupes:
            ok = False
            print(f"   !! duplicate morph names (Blender would rename them .001 and break vmd import): {dupes[:10]}")
        if required:
            missing = [n for n in required if n not in set(names)]
            if missing:
                ok = False
                print(f"   !! required morph name(s) missing: {missing}")
            else:
                print(f"   required names present: {required}")
        if not over and not dupes:
            print("   all names are unique and within the vmd limit")
    return 0 if ok else 1


def cmd_tails(paths: list[str], args) -> int:
    """Where every bone points, for the bones whose direction a user notices.

    A PMX bone's tail is either a child index or an offset, and the direction from head to tail is what
    Blender draws. The uma rig has no tails, so the exporter derives them: a bone points at the child that
    continues its chain, and a bone without one (finger tips, hair ends, the roll helpers) continues the
    segment leading into it. This reports the result, the left/right mirroring, and the bones that do not
    follow the segment leading into them - a hair strand that turns is expected there, the head bone is not.
    """
    exit_code = 0
    for path in paths:
        m = read_model(path)
        bones = m.bones
        by_index = {b.index: b for b in bones}
        children: dict[int, list[Bone]] = {}
        for b in bones:
            children.setdefault(b.parent, []).append(b)
        print(f"== {path}")

        def head(bone: Bone) -> list[float]:
            return bone.pos

        def tail(bone: Bone) -> list[float] | None:
            if bone.tail_bone is not None and 0 <= bone.tail_bone < len(bones):
                return bones[bone.tail_bone].pos
            if bone.tail_pos is not None:
                return [bone.pos[i] + bone.tail_pos[i] for i in range(3)]
            return None

        def subtract(a, b):
            return [a[i] - b[i] for i in range(3)]

        def length(v):
            return math.sqrt(sum(c * c for c in v))

        def angle(a, b):
            la, lb = length(a), length(b)
            if la < 1e-9 or lb < 1e-9:
                return 0.0
            cosine = max(-1.0, min(1.0, sum(a[i] * b[i] for i in range(3)) / (la * lb)))
            return math.degrees(math.acos(cosine))

        if args.named:
            print("   bone                 head -> tail direction (length)")
            for name in args.named.split(","):
                bone = next((b for b in bones if b.name == name), None)
                if bone is None:
                    print(f"   {name:<20} (not in this model)")
                    continue
                end = tail(bone)
                if end is None:
                    print(f"   {name:<20} no tail")
                    continue
                delta = subtract(end, bone.pos)
                print(f"   {name:<20} ({delta[0]:+.4f}, {delta[1]:+.4f}, {delta[2]:+.4f})  len={length(delta):.4f}")

        deviations = []
        leaf_deviations = []
        for bone in bones:
            if bone.parent < 0 or bone.parent >= len(bones):
                continue
            end = tail(bone)
            if end is None:
                continue
            incoming = subtract(bone.pos, bones[bone.parent].pos)
            if length(incoming) < 1e-5:
                continue
            deviation = angle(subtract(end, bone.pos), incoming)
            deviations.append((deviation, bone.name, len(children.get(bone.index, []))))
            if not children.get(bone.index):
                leaf_deviations.append(deviation)
        off_chain = [row for row in deviations if row[0] > 5.0]
        deviations.sort(reverse=True)
        print(f"   bones: {len(bones)}, checked {len(deviations)}, off the segment leading into them "
              f"(>5 deg): {len(off_chain)}")
        for deviation, name, kids in deviations[:args.worst]:
            print(f"      {name:<28} {deviation:6.2f} deg   children={kids}")
        if leaf_deviations:
            print(f"   leaf bones: {len(leaf_deviations)}, worst deviation "
                  f"{max(leaf_deviations):.2f} deg, mean {sum(leaf_deviations) / len(leaf_deviations):.2f} deg")

        pairs = args.pairs.split(",") if args.pairs else []
        if pairs:
            print("   left/right mirror check (left against the right one mirrored across x):")
            for stem in pairs:
                left = next((b for b in bones if b.name == f"{stem}_L"), None)
                right = next((b for b in bones if b.name == f"{stem}_R"), None)
                if left is None or right is None or tail(left) is None or tail(right) is None:
                    continue
                dl = subtract(tail(left), left.pos)
                dr = subtract(tail(right), right.pos)
                deviation = angle(dl, [-dr[0], dr[1], dr[2]])
                flag = "" if deviation <= args.mirror_tolerance else "   <- not mirrored"
                if deviation > args.mirror_tolerance:
                    exit_code = 1
                print(f"      {stem + '_L/R':<20} {deviation:6.2f} deg apart{flag}")
    return exit_code


def main() -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("summary", "bones", "weights", "morphs", "check"):
        p = sub.add_parser(name)
        p.add_argument("paths", nargs="+")
        p.add_argument("--material")
        p.add_argument("--grep")
        if name == "summary":
            p.add_argument("--materials", action="store_true",
                           help="also list every material's texture, sphere map, toon map and comment")
    p = sub.add_parser("names")
    p.add_argument("paths", nargs="+")
    p.add_argument("--require", help="comma separated morph names that must exist")
    p = sub.add_parser("diff")
    p.add_argument("paths", nargs=2)
    p = sub.add_parser("tails")
    p.add_argument("paths", nargs="+")
    p.add_argument("--named", default="Neck,Head,Shoulder_L,Shoulder_R,ShoulderRoll_L,ShoulderRoll_R,"
                                     "Arm_L,Arm_R,ArmRoll_L,ArmRoll_R,Index_03_L,Index_03_R",
                   help="comma separated bones whose direction is printed")
    p.add_argument("--pairs", default="Shoulder,ShoulderRoll,Arm,ArmRoll,Elbow,Wrist,Thigh,Knee,Index_01,Index_02",
                   help="comma separated bone stems whose left and right direction must mirror each other")
    p.add_argument("--mirror-tolerance", type=float, default=5.0,
                   help="how far a left/right pair may differ, in degrees")
    p.add_argument("--worst", type=int, default=8, help="how many of the worst bone deviations to list")
    args = ap.parse_args()
    return {"summary": cmd_summary, "bones": cmd_bones, "weights": cmd_weights,
            "morphs": cmd_morphs, "diff": cmd_diff, "check": cmd_check,
            "names": cmd_names, "tails": cmd_tails}[args.cmd](args.paths, args)


if __name__ == "__main__":
    sys.exit(main())
