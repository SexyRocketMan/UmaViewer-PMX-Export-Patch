"""Does choosing `none` for a pinned family really leave its bones plain - and does leaving it alone keep the pin?

    blender -b --factory-startup -P Tools/verify_rig_plain_bones.py -- <file.pmx> <out.txt>

The pinned family (the ears, `"fixed_families": {"Ear": "spines.basic_tail"}`) used to be drawn as a locked row,
so `none` - the answer a damped track needs - could not be chosen for it. Three claims are checked here, and each
is about *what the run decides*, not about the dialog's pixels:

1. the dialog's starting value for a pinned family is the pin (`TAIL`), so an untouched row changes nothing;
2. with `none` chosen, the first rule that decides an ear bone has no rig type - the resolved answer sits ahead of
   the rules table (`final = leading_skips + resolved + rules[cut:]`, first match wins), so the ears really are
   left plain;
3. with the pin's own mode chosen, nothing changes: the first deciding rule is still `spines.basic_tail`.

Exit 1 on a failure.
"""
import re
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"
argv = sys.argv[sys.argv.index("--") + 1:]
pmx_path, out_path = argv[0], argv[1]
lines, failures = [], []


def say(text=""):
    lines.append(text)
    print(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.mmd_tools.import_model(filepath=pmx_path, scale=0.08, types={'MESH', 'ARMATURE', 'MORPHS'})

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    RigifyChains as R)

arm = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
rules, options, source = R.load_rules(None)
fixed = dict(options.get("fixed_families") or {})
present = R.physics_families(arm)
family = next(iter(fixed), None)
say("=" * 100)
say(f"pinned families: {fixed or 'none'}   (rules from {source})")
if family is None:
    say("this install pins no family, so there is nothing to check")
    sys.exit(0)
bones = sorted(present.get(family) or [])
say(f"{family} bones: {bones}")


class Item:
    def __init__(self, name, bone_names):
        self.family = name
        self.bones = " ".join(sorted(bone_names))
        self.mode = None
        self.default_mode = None
        self.fixed = ""


items = [Item(name, names) for name, names in sorted(present.items()) if name]
modes, table_modes, label = R.family_defaults(rules, {}, R.PRESET_DEFAULT, items,
                                             (options.get("unmatched_families") or {}))
pin_type = fixed[family]
pin_mode = R.TYPE_MODES.get(pin_type)

say("")
say("1. an untouched row keeps the pin")
check(f"the dialog starts {family} at the pinned type's mode",
      modes.get(family) == pin_mode, f"{modes.get(family)!r} against pin {pin_type!r} -> {pin_mode!r}")


class SilentTrace:
    def info(self, *_args, **_kwargs):
        return None

    def check(self, label="", ok=True, detail=""):
        return ok

    def warn(self, *_args, **_kwargs):
        return None


def first_deciding_rule(chosen):
    """The rule the run would use for the family's first bone - first match wins over the final list."""
    final, report = R.resolve_rule_modes(arm, rules, chosen, table_modes, fixed,
                                         options.get("never_rig") or (), SilentTrace(),
                                         options.get("unmatched_families") or {})
    bone = bones[0] if bones else None
    for rule in final:
        if not rule.get("pattern") or bone is None:
            continue
        try:
            if re.match(str(rule["pattern"]), bone):
                return rule, final, report
        except re.error:
            continue
    return None, final, report


say("")
say("2. choosing none leaves the bones plain")
rule, final, report = first_deciding_rule({family: R.MODE_OFF})
say(f"  first rule deciding {bones[0] if bones else '?'}: {rule}")
check("the run's answer for the family is 'no rig type'",
      any(family == entry[1] and not entry[2] for entry in report),
      str([entry for entry in report if entry[1] == family]))
check("the rule that would decide the ear bone has no chain type",
      rule is not None and not rule.get("type"), str(rule.get("type") if rule else None))
check("and it came from the setup rather than the table",
      rule is not None and (rule.get("skip") or "setup" in str(rule.get("source", "")) or
                            not rule.get("type")), str(rule))

say("")
say("3. choosing the pin changes nothing")
rule2, _final2, report2 = first_deciding_rule({family: pin_mode})
say(f"  first rule deciding {bones[0] if bones else '?'}: {rule2}")
check("the pin still types the ear bones", rule2 is not None and rule2.get("type") == pin_type,
      str(rule2.get("type") if rule2 else None))
check("and the run reports it as the pin, not an override",
      any(family == entry[1] and entry[3] == "fixed" for entry in report2),
      str([entry for entry in report2 if entry[1] == family]))

say("")
say("=" * 100)
say("FAILURES: " + (", ".join(failures) if failures else "none"))
with open(out_path, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
sys.exit(1 if failures else 0)
