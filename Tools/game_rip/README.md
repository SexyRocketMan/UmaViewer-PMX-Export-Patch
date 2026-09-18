# Reading the game's own shaders and materials

How the ground truth was obtained, so it can be done again. Nothing here is guessed: every step is a key, an
offset or a query copied from the game's data or from UmaViewer's loader, which reads the same files.

Everything writes into a scratch directory, never into this repository: the extracted bundles and the game's
asset data are not ours to redistribute. Findings belong in a document; the bytes stay outside.

## 1. The database that names everything

`umamusume_Data/Persistent/meta` is a SQLite3MC database (encrypted SQLite, multiple-ciphers build). The viewer
reads it in `Assets/Scripts/UmaDatabase/UmaDatabaseController.cs`; the pieces needed are:

- open with `sqlite3_open_v2`
- `sqlite3mc_config(db, "cipher", 3)`
- `sqlite3_key(db, key, len)` where the key is `Config.DBKey` with the first 13 bytes of `Config.DBBaseKey`
  XORed over it: `key[i] ^= DBBaseKey[i % 13]`
- query `SELECT m,n,h,c,d,e FROM a`

Columns: `m` category, `n` asset path, `h` hash, `c` checksum, `d` prerequisites, `e` the bundle key.

```powershell
cd Tools/game_rip
dotnet run -c Release -- "C:\Users\user\Umamusume\umamusume_Data\Persistent\meta" "%chr1001_00_face%" 20
```

`meta_dump.csproj` needs `sqlite3mc_x64.dll` beside it - copy it from the viewer's `Assets/Plugins/`.

Two things to know before trusting a result:

- the key derivation is `i % 13`, not `i % 16`, even though `DBBaseKey` is 16 bytes long;
- a wrong key does not fail loudly at `sqlite3_key` - it fails later with *file is not a database*. That error
  means the key or the cipher is wrong, not that the file is corrupt.

## 2. The bundles

`Persistent/dat/<first two chars of hash>/<hash>` are plain UnityFS bundles whose **first 256 bytes are in the
clear** - which is why the magic reads `UnityFS`. Everything from byte 256 on is XORed with an 88-byte keystream:

```python
keys[i * 8 + j] = ABKey[i] ^ little_endian_int64(entry_key)[j]      # i in 0..10, j in 0..7
data[p] ^= keys[p % 88]                                            # p >= 256
```

`ABKey` is `Config.ABKey`. The entry key comes from column `e`. This is `UmaAssetBundleStream` and
`UmaDatabaseEntry` from the viewer, in ten lines.

```powershell
python rip_shader.py MEC5L6UQQ4MIEEDH2IW5VVZRUB5HSOHF 5460498415719320078 shader_bundle
python rip_shader.py 3GQR2HQJSCOKRZXBD7UY5OJARUQRDIL4 6614454798242685316 face_material_bundle
```

The `shader` entry (`SELECT ... WHERE n='shader'`) is the bundle holding all 499 shaders.

## 3. Reading what comes out

With the bundle decrypted, `UnityPy` loads it as an ordinary Unity bundle. Two gotchas:

- a shader's **name is not in the top-level `m_Name`** (empty in these builds) but in
  `m_ParsedForm.m_Name`; the properties are at `m_ParsedForm.m_PropInfo.m_Props[].m_Name`;
- there is **no ShaderLab source**. These builds ship `m_ShaderIsBaked` with compiled blobs
  (`progVertex`/`progFragment` → `m_SubPrograms`, `compressedBlob`). So the property lists and the material
  values are readable, but the instructions need bytecode disassembly.

Material values live in `m_SavedProperties` as `m_Floats`, `m_Colors`, `m_TexEnvs`, and UnityPy hands those
entries back as `[key, value]` pairs, not dicts.

```powershell
python find_toon_shaders.py          # every Gallop/3D/Chara shader with its property list
python face_shader_and_material.py   # the face shaders, and the real mtl_chr1001_00_face values
```

## 4. Finding which asset is which

The names in the database are the key to everything: `3d/chara/head/chr1001_00/textures/tex_chr1001_00_face_diff`
is a texture, `sourceresources/3d/chara/head/chr1001_00/materials/mtl_chr1001_00_face` is a material and its `d`
column lists its dependencies (`shader`, other textures). Search by path fragment rather than by guessing hashes.

Texture `m_PathID`/`m_FileID` references inside a material can be resolved against the bundle's own object list,
which is how to confirm which file a `_TripleMaskMap` or `_ToonMap` slot actually points at.

## Reproducing this from zero

Everything needed, in the order it has to be done. The asset identifiers are recorded so a later reader can tell
whether they are looking at the same data.

**What is needed:** the installed game, .NET, Python with `UnityPy` and `Pillow`, and `sqlite3mc_x64.dll` copied
from the viewer's `Assets/Plugins/` next to the built `meta_dump`.

**The face shader, character 1001:**

| asset | bundle hash | entry key |
| --- | --- | --- |
| the shader catalogue (`n = 'shader'`, 499 shaders) | `MEC5L6UQQ4MIEEDH2IW5VVZRUB5HSOHF` | `5460498415719320078` |
| the face material bundle (`mtl_chr1001_00_face`) | `3GQR2HQJSCOKRZXBD7UY5OJARUQRDIL4` | `6614454798242685316` |

```powershell
cd Tools/game_rip
dotnet run -c Release --project . -- <meta path> "shader" 4          # find the shader bundle
dotnet run -c Release --project . -- <meta path> "%chr1001_00_face%" 12   # the face material and its textures
python rip_shader.py MEC5L6UQQ4MIEEDH2IW5VVZRUB5HSOHF 5460498415719320078 shader_bundle
python extract_blob.py                                               # the blob layout of the face shader
python decompress_blob.py                                            # writes face_blob_region.bin
dotnet run --project dxbc_disasm -- <game_shaders>/face_blob_region.bin <game_shaders>/dxbc
```

The face shader is `Gallop/3D/Chara/DitherToonFace/TSER`. Names are empty in the top-level `m_Name`, so find it by
`m_ParsedForm.m_Name`. Its region holds 28 DXBC containers: four vertex programs and eleven full pixel variants
(8 and 9 textures), plus small programs for other passes.

**The textures:**

| asset | bundle hash | entry key |
| --- | --- | --- |
| `tex_chr0001_00_face000_0_area` | `6MQFGZXC62KGRJM4XDQVETKUJLTT6QFK` | `-614143197183374756` |
| `tex_chr0001_00_face000_0_area_wet` | `ZKVOWNLO2E2RUQFBFE45TE3OK3LEUUK5` | `2350644591724972638` |

```powershell
python extract_area.py     # writes the area texture, or use rip_shader.py for any other bundle
python dump_face_textures.py
python face_channel_sheet.py    # red, green and blue side by side - the only way to see a soft mask
```

**Expect:** `Player.log`-style console output is not needed; every step prints what it found. The most likely
failure is a wrong key, which surfaces as *file is not a database* rather than as an error at `sqlite3_key`.

## The saved sources

`shader_sources/` holds the disassembly the conclusions came from, so a reader can check them without the game
installed:

| file | what it is |
| --- | --- |
| `face_vertex.asm.txt` | the vertex program: skinning, `_NormalizeNormal`, and the cylinder blend |
| `face_pixel_use_mask_color.asm.txt` | the pixel variant with `cb1[40]`, the `USE_MASK_COLOR` path the face runs |
| `face_pixel_8tex.asm.txt` | the smaller pixel variant, for comparison |
| `pass_2tex.asm.txt`, `pass_no_textures.asm.txt` | two of the small passes, for the shape of the rest |

These are disassemblies of the game's own bytecode. The raw blobs and the extracted bundles are not kept: they are
the game's assets, and this repository is public. What is kept is the analysis.
