# Moving the docs into git

The eleven documents that used to sit beside this file now live in the tools repo, where they are tracked:

    C:\Users\user\Documents\dev\UmaViewer-DevTools\docs\

Moved 2026-10-02, when the version-control audit (docs section 9ah of the build log) found every one of them
untracked: the rigify-chains build log, the start-here summary, the process notes, the chain data model, the
animator's rig reference, the MMR controller notes, the addon steps and structure, the FBX export notes and the
viewer shortcuts.

Why there and not here: this repo (`UmaViewer-PMX-Export-Patch`) is in the middle of an unresolved merge
(`UU README.md`), so git refuses any partial commit in it - the rig docs could not be committed here without first
resolving that, and resolving someone else's conflict is not a documentation change. The tools repo holds the 94
probe scripts and the fleet reports the docs cite, so the written record and the measurements now sit together.

The moved files:

    BONE_STRUCTURE.md          COOLCHOU_NOTE.md        COOLCHOU_RIG.md
    FBX_EXPORT.md              MMR_CONTROLLER.md       PRESETS_AND_RULES.md
    RIGIFY_CHAINS_HANDOFF.md   RIG_HANDOFF.md          RIG_WORKFLOW.md
    UI_SHORTCUTS.md            UMA_ADDON_STEPS.md

Nothing was lost, and nothing here was modified: `README.md`'s documentation index still points at `docs/<name>.md`
paths, and it sits inside that same unresolved conflict block, so it will be rewritten when the merge is resolved -
point the rig entries at `../UmaViewer-DevTools/docs/` then, or copy this pointer's list.
