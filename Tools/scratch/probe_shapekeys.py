import sys
import logging
import traceback

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")

import bpy

logging.basicConfig(level=logging.INFO, stream=sys.stdout, format="%(levelname)s %(message)s")
logging.getLogger("mmd_tools").setLevel(logging.INFO)

from mmd_tools.core.pmx import importer as pmx_importer

CLS = pmx_importer.PMXImporter
orig = CLS._PMXImporter__importVertexMorphs


def wrapped(self):
    try:
        model = self._PMXImporter__model
        logging.warning("PRE-IMPORT: %d morphs", len(model.morphs))
        from mmd_tools.core import pmx

        print("  vertex morphs:", sum(1 for m in model.morphs if isinstance(m, pmx.VertexMorph)))
        print("  mesh obj:", getattr(self, "_PMXImporter__meshObj", None))
        r = orig(self)
        verts = self._PMXImporter__meshObj
        print("POST vertex morphs: obj", verts, "shape_keys", verts.data.shape_keys if verts else None)
        print("  key blocks:", len(verts.data.shape_keys.key_blocks) if verts and verts.data.shape_keys else "none")
        return r
    except Exception:
        traceback.print_exc()
        raise


CLS._PMXImporter__importVertexMorphs = wrapped

path = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs.pmx"
bpy.ops.mmd_tools.import_model(filepath=path)
for ob in bpy.data.objects:
    if ob.type == "MESH":
        sk = ob.data.shape_keys
        print("MESH", ob.name, "verts", len(ob.data.vertices), "shape_keys", len(sk.key_blocks) if sk else 0)
for ob in bpy.data.objects:
    if ob.type == "EMPTY":
        r = ob.mmd_root if hasattr(ob, "mmd_root") else None
        if r:
            print("ROOT", ob.name, "vertex_morphs", len(r.vertex_morphs), "material_morphs", len(r.material_morphs), "bone_morphs", len(r.bone_morphs))
