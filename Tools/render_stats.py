# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow"]
# ///
"""Check rendered frames without looking at them.

Catches the failure modes that are easy to ship and hard to notice: a black frame, a frame where the
subject is missing or off screen, a flat white (untextured) model, and a motion that does not
actually move - or one that fails to loop.

  uv run Tools/render_stats.py frames <dir>                       # stats per frame + summary
  uv run Tools/render_stats.py still <file.png>                   # stats for one image
  uv run Tools/render_stats.py compare <a.png> <b.png> [--expect same|different]

For a motion the useful pair of checks is "some frames differ a lot" (the motion plays) and
"the last frame matches the first" (it loops). Exit code is non zero when a threshold is violated.
"""

from __future__ import annotations

import argparse
import colorsys
import os
import sys
from collections import Counter

from PIL import Image


def load(path: str) -> Image.Image:
    return Image.open(path).convert("RGB")


def analyse(image: Image.Image) -> dict:
    width, height = image.size
    pixels = list(image.getdata())
    corners = [image.getpixel(p) for p in ((0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1))]
    background = Counter(corners).most_common(1)[0][0]

    def is_background(pixel, tolerance=12):
        return all(abs(a - b) <= tolerance for a, b in zip(pixel, background))

    subject = [p for p in pixels if not is_background(p)]
    stats = {
        "path": getattr(image, "filename", ""),
        "size": (width, height),
        "background": background,
        "subject_fraction": len(subject) / len(pixels),
        "distinct_colours": len(set(subject)),
    }
    if subject:
        hsv = [colorsys.rgb_to_hsv(r / 255, g / 255, b / 255) for r, g, b in subject]
        stats["mean_rgb"] = tuple(round(sum(c[i] for c in subject) / len(subject)) for i in range(3))
        stats["mean_saturation"] = sum(h[1] for h in hsv) / len(hsv)
        stats["max_saturation"] = max(h[1] for h in hsv)
        stats["mean_value"] = sum(h[2] for h in hsv) / len(hsv)
        xs = [i % width for i, p in enumerate(pixels) if not is_background(p)]
        ys = [i // width for i, p in enumerate(pixels) if not is_background(p)]
        stats["bbox"] = (min(xs), min(ys), max(xs), max(ys))
        stats["bbox_fraction"] = ((max(xs) - min(xs)) / width, (max(ys) - min(ys)) / height)
    return stats


def mean_abs_difference(a: Image.Image, b: Image.Image) -> float:
    """Average per channel difference in 0..1, plus the fraction of pixels that changed."""
    if a.size != b.size:
        a = a.resize(b.size)
    pa, pb = list(a.getdata()), list(b.getdata())
    total = 0.0
    changed = 0
    for (r1, g1, b1), (r2, g2, b2) in zip(pa, pb):
        d = (abs(r1 - r2) + abs(g1 - g2) + abs(b1 - b2)) / (3 * 255)
        total += d
        if d > 0.02:
            changed += 1
    return total / len(pa), changed / len(pa)


def describe(stats: dict) -> str:
    line = (f"   subject={100 * stats['subject_fraction']:.1f}% "
            f"colours={stats['distinct_colours']}")
    if "mean_rgb" in stats:
        line += (f" mean_rgb={stats['mean_rgb']} sat={stats['mean_saturation']:.3f}"
                 f" (max {stats['max_saturation']:.3f})")
    if "bbox_fraction" in stats:
        line += f" bbox={100 * stats['bbox_fraction'][0]:.0f}x{100 * stats['bbox_fraction'][1]:.0f}%"
    return line


def check_thresholds(stats: dict, args) -> list[str]:
    failures = []
    if stats["subject_fraction"] < args.min_subject:
        failures.append(f"only {100 * stats['subject_fraction']:.1f}% of the frame is the subject "
                        f"(< {100 * args.min_subject:.0f}%): the model may be missing or off screen")
    if stats["subject_fraction"] > args.max_subject:
        failures.append(f"{100 * stats['subject_fraction']:.1f}% of the frame is the subject "
                        f"(> {100 * args.max_subject:.0f}%): the camera is probably inside the model")
    if stats.get("distinct_colours", 0) < args.min_colours:
        failures.append(f"only {stats['distinct_colours']} distinct colours: the render looks flat, "
                        f"which usually means missing textures or a solid colour")
    if stats.get("mean_saturation", 0) > args.max_saturation:
        failures.append(f"mean saturation {stats['mean_saturation']:.3f} is suspiciously uniform "
                        f"(> {args.max_saturation}): materials may be an untextured placeholder")
    if stats.get("mean_value", 1.0) < args.min_value:
        failures.append(f"mean brightness {stats['mean_value']:.3f} is very dark (< {args.min_value})")
    return failures


def cmd_frames(args) -> int:
    directory = args.target
    files = sorted(f for f in os.listdir(directory) if f.lower().endswith(".png"))
    if not files:
        print(f"!! no png frames in {directory}")
        return 1

    failures = []
    stats_list = []
    step = max(1, len(files) // args.report)
    for index, name in enumerate(files):
        path = os.path.join(directory, name)
        stats = analyse(load(path))
        stats_list.append(stats)
        stats["path"] = name
        if index % step == 0 or index == len(files) - 1:
            print(f"{name:>20} {describe(stats)}")
        failures += [f"{name}: {f}" for f in check_thresholds(stats, args)]

    first, last = stats_list[0], stats_list[-1]
    print(f"   frames={len(files)}")
    print(f"   first/last subject={100 * first['subject_fraction']:.1f}%/"
          f"{100 * last['subject_fraction']:.1f}%  colours={first['distinct_colours']}/"
          f"{last['distinct_colours']}")

    if len(files) >= 3:
        # A recording starts wherever the animation happens to be (the recorder does not seek, see
        # Tools/README.md), so how big the first step is depends on the phase the loop starts in: a run
        # cycle that starts in the middle of the stride legitimately moves a lot between frame 1 and 2.
        # What a pose pop looks like instead is a first step far above the steps around it, so compare it
        # against the median step of the same recording - the same relative check vmd_inspect motion does.
        steps = []
        for name_a, name_b in zip(files, files[1:]):
            steps.append(mean_abs_difference(load(os.path.join(directory, name_a)),
                                             load(os.path.join(directory, name_b))))
        typical = sorted(value for value, _ in steps)[len(steps) // 2]
        jump, jump_changed = steps[0]
        print(f"   start: first vs second frame differ by {jump:.4f} mean ({100 * jump_changed:.1f}% of pixels), "
              f"typical step {typical:.4f} ({jump / typical if typical else 0:.2f}x)")
        if args.max_first_frame_jump > 0 and jump > args.max_first_frame_jump:
            failures.append(f"the first frame differs from the second by {jump:.4f} "
                            f"(> {args.max_first_frame_jump}): that is a pose pop, typically a T-pose "
                            f"recorded before the animation started, or a rest pose mismatch")
        elif typical > 0 and jump > args.first_step_factor * typical:
            failures.append(f"the step from the first to the second frame is {jump / typical:.1f}x the "
                            f"typical step ({jump:.4f} vs {typical:.4f}, factor {args.first_step_factor}): "
                            f"the motion starts with a pose pop, typically a T-pose recorded before the "
                            f"animation started, or a rest pose mismatch")

        middle = load(os.path.join(directory, files[len(files) // 2]))
        difference, changed = mean_abs_difference(load(os.path.join(directory, files[0])), middle)
        print(f"   motion: first vs middle differ by {difference:.4f} mean ({100 * changed:.1f}% of pixels)")
        if difference < args.min_motion:
            failures.append(f"first and middle frame differ by only {difference:.4f}: the motion does "
                            f"not appear to be playing")
        loop_difference, loop_changed = mean_abs_difference(load(os.path.join(directory, files[0])),
                                                            load(os.path.join(directory, files[-1])))
        print(f"   loop: first vs last differ by {loop_difference:.4f} mean ({100 * loop_changed:.1f}% of pixels)")
        if loop_difference > args.max_loop_difference:
            failures.append(f"first and last frame differ by {loop_difference:.4f} "
                            f"(> {args.max_loop_difference}): the motion does not loop cleanly")

    for failure in failures[:10]:
        print(f"   !! {failure}")
    print("PASS" if not failures else "FAIL")
    return 0 if not failures else 1


def cmd_still(args) -> int:
    stats = analyse(load(args.target))
    stats["path"] = args.target
    print(f"== {args.target}")
    print(describe(stats))
    failures = check_thresholds(stats, args)
    for failure in failures:
        print(f"   !! {failure}")
    print("PASS" if not failures else "FAIL")
    return 0 if not failures else 1


def cmd_compare(args) -> int:
    a, b = load(args.a), load(args.b)
    difference, changed = mean_abs_difference(a, b)
    print(f"== {args.a}\n== {args.b}")
    print(f"   mean difference {difference:.4f}, {100 * changed:.1f}% of pixels changed")
    if args.expect == "same" and difference > args.tolerance:
        print(f"   !! expected the frames to match (tolerance {args.tolerance})")
        return 1
    if args.expect == "different" and difference < args.tolerance:
        print(f"   !! expected the frames to differ (tolerance {args.tolerance})")
        return 1
    print("PASS")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="cmd", required=True)

    def add_thresholds(p):
        p.add_argument("--min-subject", type=float, default=0.02)
        p.add_argument("--max-subject", type=float, default=0.95)
        p.add_argument("--min-colours", type=int, default=500)
        p.add_argument("--max-saturation", type=float, default=0.95)
        p.add_argument("--min-value", type=float, default=0.05)
        p.add_argument("--min-motion", type=float, default=0.0005)
        p.add_argument("--max-loop-difference", type=float, default=0.01)
        p.add_argument("--max-first-frame-jump", type=float, default=0,
                       help="optional absolute cap on how much the first frame may differ from the "
                            "second; 0 (default) only checks it against the typical step of the same "
                            "recording, because a loop that starts mid-stride legitimately steps far")
        p.add_argument("--first-step-factor", type=float, default=2.5,
                       help="the first step may be at most this many times the median step; the default "
                            "matches vmd_inspect motion")
        p.add_argument("--report", type=int, default=6)

    p = sub.add_parser("frames"); p.add_argument("target"); add_thresholds(p)
    p = sub.add_parser("still"); p.add_argument("target"); add_thresholds(p)
    p = sub.add_parser("compare")
    p.add_argument("a"); p.add_argument("b")
    p.add_argument("--expect", choices=["same", "different"], default="same")
    p.add_argument("--tolerance", type=float, default=0.01)
    args = parser.parse_args()
    return {"frames": cmd_frames, "still": cmd_still, "compare": cmd_compare}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
