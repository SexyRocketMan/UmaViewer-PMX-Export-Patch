"""Draw a spring chain in 2D so the motion can be judged without Blender or a render.

    python Tools/spring_plot.py <spec.json> [--motion spec|impulse|wag] [--swing DEG] [--out chain.html]
                                [--no-colliders] [--vanes]

Writes one self-contained HTML file: the chain seen from the side (and from above), frame by frame, with the
*grey ghost* being where a rigid chain would be at that moment. The gap between the two is the lag, and it is
the whole question - a tail that keeps its links on the ghost is a rod, whatever the numbers say.

Space plays and pauses, the arrow keys step a frame at a time, `o` toggles drawing every frame on top of the
others (the contact sheet), and `v` turns on the little frames of reference drawn on each link.

The chain is the solver's own output (`cyspring_port.simulate_spec`), not a Blender bake, so what is drawn is
the mechanism and nothing else - which is the point of drawing it at all.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cyspring_port as port  # noqa: E402


def drive(spec: dict, mode: str, swing: float, axis: str) -> list[dict]:
    """The same three test drives `cyspring_metrics.py` uses, so a plot and its numbers agree."""
    if mode == "spec" and spec.get("motion"):
        return spec["motion"]
    if mode == "spec":
        return [{"frame": f, "rotation": list(port.IDENTITY)} for f in range(spec["frames"] + 1)]
    direction = {"x": (1.0, 0.0, 0.0), "y": (0.0, 1.0, 0.0), "z": (0.0, 0.0, 1.0)}[axis]

    def keyed(angle: float) -> list[float]:
        half = math.radians(angle) / 2.0
        return [direction[0] * math.sin(half), direction[1] * math.sin(half),
                direction[2] * math.sin(half), math.cos(half)]

    if mode == "impulse":
        frames = max(spec["frames"], 72)
        angles = [0.0] * 5 + [swing * 0.5, swing] + [swing] * (frames - 6)
        return [{"frame": f, "rotation": keyed(angles[f])} for f in range(frames + 1)]

    rise, hold = 3, 10
    keys = [(0.0, 0.0), (rise, swing), (rise + hold, swing), (2 * rise + hold, -swing),
            (2 * rise + 2 * hold, -swing), (3 * rise + 2 * hold, 0.0)]
    motion = []
    for frame in range(spec["frames"] + 1):
        angle = 0.0
        for (start, begin), (end, finish) in zip(keys, keys[1:]):
            if start <= frame <= end:
                angle = begin + (finish - begin) * ((frame - start) / (end - start))
                break
        motion.append({"frame": frame, "rotation": keyed(angle)})
    return motion


def chain_points(spec: dict, frames: list) -> dict:
    """The chain's points per frame: the anchor, then every link's tail, plus the same for a rigid chain."""
    n = len(spec["bones"])
    pivot = tuple(spec.get("pivot") or spec["bones"][0]["position"])
    root_head = tuple(spec["bones"][0]["position"])
    axes = [tuple(bone["axis"]) for bone in spec["bones"]]

    points, rigid = [], []
    for frame_index, row in enumerate(frames):
        rotation = port._motion_at({"motion": spec["motion"]}, frame_index)
        offset = port._offset_at({"motion": spec["motion"]}, frame_index)
        anchor = port.v_add(port.v_add(pivot, port.q_rotate(rotation, port.v_sub(root_head, pivot))), offset)
        points.append([tuple(anchor)] + [tuple(row[i * 7:i * 7 + 3]) for i in range(n)])

        # The rigid ghost: every link is `R(t) * axis` hung off the animated anchor, so it is where the
        # chain would be if the solver were not there at all.
        path = [tuple(anchor)]
        for index, axis in enumerate(axes):
            direction = port.q_rotate(rotation, axis)
            path.append(port.v_add(path[-1], port.v_scale(direction, spec["bones"][index]["distance"])))
        rigid.append(path)
    return {"points": points, "rigid": rigid}


def build_html(spec: dict, data: dict, deviation: list[list[float]], mode: str, note: str) -> str:
    frames = data["points"]
    rigid = data["rigid"]
    flat = [point for frame in frames for point in frame]
    centre = [sum(p[axis] for p in flat) / len(flat) for axis in range(3)]
    radius = max(math.dist(point, centre) for point in flat) or 1.0
    scale = 150.0 / radius

    payload = {
        "chain": [[[round(v, 5) for v in point] for point in frame] for frame in frames],
        "rigid": [[[round(v, 5) for v in point] for point in frame] for frame in rigid],
        "names": [bone["name"] for bone in spec["bones"]],
        "centre": [round(v, 5) for v in centre],
        "scale": round(scale, 4),
        "title": f"{spec['name']} - {len(spec['bones'])} link(s), {mode} drive",
        "note": note,
        "deviation": deviation,
    }
    return _HTML.replace("__DATA__", json.dumps(payload, separators=(",", ":")))


_HTML = """<!doctype html>
<meta charset="utf-8">
<title>CySpring chain</title>
<style>
  body { background: #12141a; color: #d8dce6; font: 13px/1.45 ui-monospace, Consolas, monospace; margin: 0; }
  header { padding: 10px 14px 4px; }
  h1 { font-size: 14px; font-weight: 600; margin: 0 0 2px; color: #fff; }
  .hint { color: #7d8597; }
  .wrap { display: flex; flex-wrap: wrap; gap: 10px; padding: 6px 14px 14px; }
  canvas { background: #0b0d12; border: 1px solid #232733; border-radius: 8px; }
  .legend span { display: inline-block; margin-right: 14px; }
  .swatch { display: inline-block; width: 22px; height: 3px; vertical-align: middle; margin-right: 5px; }
</style>
<header>
  <h1 id="title"></h1>
  <div class="hint" id="note"></div>
  <div class="hint">space play/pause &middot; left/right step &middot; o overlay all frames &middot; v frames &middot;
    the grey chain is where a rigid chain would be</div>
  <div class="legend hint">
    <span><i class="swatch" style="background:#ff7a45"></i>solved chain</span>
    <span><i class="swatch" style="background:#4a5060"></i>rigid ghost</span>
    <span><i class="swatch" style="background:#39d0a0"></i>tip trail</span>
  </div>
</header>
<div class="wrap"><canvas id="view" width="430" height="470"></canvas>
<canvas id="strip" width="560" height="470"></canvas></div>
<script>
const DATA = __DATA__;
const view = document.getElementById('view'), ctx = view.getContext('2d');
const strip = document.getElementById('strip'), sctx = strip.getContext('2d');
document.getElementById('title').textContent = DATA.title;
document.getElementById('note').textContent = DATA.note;

let frame = 0, playing = true, overlay = false, vanes = false, last = 0;
const frames = DATA.chain, rigid = DATA.rigid, n = DATA.names.length;

function project(point, ctx) {
  // a 35 degree turn about the vertical and a shallow tilt, so the wag and the droop both read
  const [x, y, z] = point, [cx, cy, cz] = DATA.centre;
  const dx = x - cx, dy = y - cy, dz = z - cz;
  const sx = dx * 0.82 - dz * 0.57;
  const sy = -dy * 0.94 - (dx * 0.57 + dz * 0.82) * 0.34;
  const unit = DATA.scale * 1.25;
  return [ctx.canvas.width / 2 + sx * unit, ctx.canvas.height / 2 + sy * unit];
}

function drawChain(c, points, width, alpha, tip) {
  for (let i = 0; i < points.length - 1; i++) {
    const a = project(points[i], c), b = project(points[i + 1], c);
    c.strokeStyle = i === points.length - 2 && tip ? '#39d0a0' : `rgba(255,122,69,${alpha})`;
    c.lineWidth = width + (i * 0.7);
    c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); c.stroke();
    c.fillStyle = `rgba(216,220,230,${alpha})`;
    c.beginPath(); c.arc(b[0], b[1], 2.4, 0, 7); c.fill();
  }
  if (vanes) {
    for (let i = 0; i < points.length - 1; i++) {
      const a = project(points[i], c);
      c.fillStyle = `rgba(120,200,255,${alpha * 0.8})`;
      c.font = '10px monospace'; c.fillText(String(i), a[0] + 3, a[1] - 3);
    }
  }
}

function draw(frameno) {
  ctx.clearRect(0, 0, view.width, view.height);
  const points = frames[frameno], ghosts = rigid[frameno];
  if (overlay) {
    ctx.globalAlpha = 1;
    for (let f = 0; f < frames.length; f += 3) {
      drawChain(ctx, frames[f], 1, 0.13, false);
    }
  }
  // the rigid ghost first, then the solved chain on top: where they part is the lag
  ctx.setLineDash([4, 4]);
  drawChain(ctx, ghosts, 1.6, 0.55, false);
  ctx.setLineDash([]);
  const tipRigid = project(ghosts[ghosts.length - 1], ctx), tip = project(points[points.length - 1], ctx);
  ctx.strokeStyle = 'rgba(57,208,160,.75)';
  ctx.beginPath(); ctx.moveTo(tipRigid[0], tipRigid[1]); ctx.lineTo(tip[0], tip[1]); ctx.stroke();
  drawChain(ctx, points, 2.6, 0.95, true);

  ctx.fillStyle = '#7d8597'; ctx.font = '12px monospace';
  ctx.fillText(`frame ${frameno} / ${frames.length - 1}`, 10, 18);
  const gap = Math.hypot(tip[0] - tipRigid[0], tip[1] - tipRigid[1]) / (DATA.scale * 1.25);
  ctx.fillText(`tip is ${gap.toFixed(3)} units off its rigid path`, 10, 34);

  // the strip: how far each link sits from its rigid direction, through the clip
  sctx.clearRect(0, 0, strip.width, strip.height);
  const pad = 30, width = strip.width - pad - 10, height = strip.height - pad - 30;
  const series = DATA.deviation || [];
  sctx.strokeStyle = '#232733';
  sctx.beginPath(); sctx.moveTo(pad, pad); sctx.lineTo(pad, pad + height); sctx.lineTo(pad + width, pad + height); sctx.stroke();
  let maxDev = 1;
  for (const line of series) for (const v of line) maxDev = Math.max(maxDev, Math.abs(v));
  const colors = ['#ff7a45', '#ffc53d', '#39d0a0', '#4a9eff', '#b37feb'];
  series.forEach((line, index) => {
    sctx.strokeStyle = colors[index % colors.length];
    sctx.lineWidth = index === series.length - 1 ? 2.2 : 1.4;
    sctx.beginPath();
    line.forEach((value, i) => {
      const x = pad + (i / (line.length - 1)) * width;
      const y = pad + height / 2 - (value / maxDev) * (height / 2 - 8);
      i ? sctx.lineTo(x, y) : sctx.moveTo(x, y);
    });
    sctx.stroke();
  });
  const xNow = pad + (frameno / (frames.length - 1)) * width;
  sctx.strokeStyle = 'rgba(255,255,255,.25)';
  sctx.beginPath(); sctx.moveTo(xNow, pad); sctx.lineTo(xNow, pad + height); sctx.stroke();
  sctx.fillStyle = '#d8dce6'; sctx.font = '12px monospace';
  sctx.fillText(`gap to the rigid direction, deg (max ${maxDev.toFixed(1)})`, pad, pad - 12);
  sctx.fillStyle = '#7d8597';
  DATA.names.forEach((name, index) => {
    sctx.fillStyle = colors[index % colors.length];
    sctx.fillText(name, pad + 6, pad + 18 + index * 16);
  });
}

function tick(now) {
  if (playing) {
    last = last || now;
    if (now - last > 1000 / 24) { frame = (frame + 1) % frames.length; last = now; }
  }
  draw(frame);
  requestAnimationFrame(tick);
}

addEventListener('keydown', (event) => {
  if (event.code === 'Space') { playing = !playing; event.preventDefault(); }
  if (event.code === 'ArrowRight') frame = Math.min(frames.length - 1, frame + 1);
  if (event.code === 'ArrowLeft') frame = Math.max(0, frame - 1);
  if (event.key === 'o') overlay = !overlay;
  if (event.key === 'v') vanes = !vanes;
});
view.addEventListener('mousedown', (event) => {
  playing = false;
  frame = Math.round(((event.offsetX - 20) / (view.width - 40)) * (frames.length - 1));
  frame = Math.max(0, Math.min(frames.length - 1, frame));
});
requestAnimationFrame(tick);
</script>
"""


def write_gif(spec: dict, data: dict, out: str, scale_ratio: float = 1.0) -> None:
    """An animated side view, for when a browser is not in the way: the solved chain over the rigid ghost."""
    from PIL import Image, ImageDraw

    size = 460
    frames = data["points"]
    rigid = data["rigid"]
    flat = [point for frame in frames for point in frame]
    centre = [sum(p[axis] for p in flat) / len(flat) for axis in range(3)]
    radius = max(math.dist(point, centre) for point in flat) or 1.0
    unit = (size * 0.42 / radius)

    def project(point):
        x, y, z = point
        dx, dy, dz = x - centre[0], y - centre[1], z - centre[2]
        sx = dx * 0.82 - dz * 0.57
        sy = -dy * 0.94 - (dx * 0.57 + dz * 0.82) * 0.34
        return [size / 2 + sx * unit, size / 2 + sy * unit]

    images = []
    for index, frame in enumerate(frames):
        image = Image.new("RGB", (size, size), (11, 13, 18))
        draw = ImageDraw.Draw(image)
        ghost = [project(point) for point in rigid[index]]
        solved = [project(point) for point in frame]
        for a, b in zip(ghost, ghost[1:]):
            draw.line([tuple(a), tuple(b)], fill=(74, 80, 96), width=3)
        for a, b in zip(solved, solved[1:]):
            draw.line([tuple(a), tuple(b)], fill=(255, 122, 69), width=5)
        draw.line([tuple(ghost[-1]), tuple(solved[-1])], fill=(57, 208, 160), width=2)
        for point in ghost[1:]:
            draw.ellipse([point[0] - 3, point[1] - 3, point[0] + 3, point[1] + 3], fill=(74, 80, 96))
        for point in solved[1:]:
            draw.ellipse([point[0] - 4, point[1] - 4, point[0] + 4, point[1] + 4], fill=(255, 122, 69))
        draw.ellipse([solved[0][0] - 5, solved[0][1] - 5, solved[0][0] + 5, solved[0][1] + 5],
                     outline=(216, 220, 230), width=2)
        draw.text((12, 10), f"{spec['name']}  frame {index}/{len(frames) - 1}", fill=(216, 220, 230))
        draw.text((12, 26), "orange = solved   grey = rigid   green = the tip's lag", fill=(125, 133, 151))
        images.append(image)
    images[0].save(out, save_all=True, append_images=images[1:], duration=int(1000 / 24), loop=0)
    print(f"{out}: {len(images)} frames")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("spec")
    parser.add_argument("--motion", choices=("spec", "impulse", "wag"), default="spec")
    parser.add_argument("--swing", type=float, default=35.0)
    parser.add_argument("--axis", choices=("x", "y", "z"), default="y")
    parser.add_argument("--no-colliders", action="store_true")
    parser.add_argument("--out", default="", help="where to write the html (default next to the spec)")
    parser.add_argument("--gif", default="", help="also write an animated gif here")
    args = parser.parse_args(argv)

    spec = port.load_spec(args.spec)
    if args.no_colliders:
        spec = dict(spec)
        spec["colliders"] = []
        spec["bones"] = [dict(bone, colliders=[]) for bone in spec["bones"]]
    motion = drive(spec, args.motion, args.swing, args.axis)
    spec["frames"] = len(motion) - 1
    spec["motion"] = motion

    frames, _ = port.simulate_spec(spec, trace=True, warmup=24)
    data = chain_points(spec, frames)

    # the deviation series the strip draws: each link against where the drive alone would put it
    n = len(spec["bones"])
    pivot = tuple(spec.get("pivot") or spec["bones"][0]["position"])
    axes = [tuple(bone["axis"]) for bone in spec["bones"]]
    deviation = [[] for _ in range(n)]
    for index, row in enumerate(frames):
        rotation = port._motion_at({"motion": motion}, index)
        tails = [tuple(row[i * 7:i * 7 + 3]) for i in range(n)]
        if index == 0:
            heads = [tuple(pivot)] + [tails[i - 1] for i in range(1, n)]
        else:
            previous = [tuple(frames[index - 1][i * 7:i * 7 + 3]) for i in range(n)]
            heads = [port.v_add(port.v_add(pivot, port.q_rotate(rotation, port.v_sub(
                tuple(spec["bones"][0]["position"]), pivot))), port._offset_at({"motion": motion}, index))] \
                + [previous[i - 1] for i in range(1, n)]
        for i in range(n):
            link = port.v_normalized(port.v_sub(tails[i], heads[i]))
            driven = port.q_rotate(rotation, axes[i])
            deviation[i].append(round(math.degrees(math.acos(max(-1.0, min(1.0, port.v_dot(link, driven))))), 2))

    html = build_html(spec, data, deviation, args.motion, "solver output; no Blender, no bake")
    out = args.out or os.path.splitext(args.spec)[0] + ".html"
    with open(out, "w", encoding="utf-8") as handle:
        handle.write(html)
    print(f"{out}: {len(frames)} frames, {n} link(s), tip travel "
          f"{math.dist(data['points'][-1][-1], data['points'][0][-1]):.4f} game units end to end")
    if args.gif:
        write_gif(spec, data, args.gif)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
