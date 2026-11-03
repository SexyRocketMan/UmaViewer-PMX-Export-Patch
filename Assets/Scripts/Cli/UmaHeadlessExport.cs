using Gallop;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UmaMusumeAudio;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Headless (batch mode) model export, so exports can be produced and regression tested without
/// clicking through the UI.
///
/// It opens the normal viewer scene, lets it boot in play mode, then drives the same code path the
/// UI's "Export Model" button uses (<see cref="UmaViewerBuilder.LoadUma"/> +
/// <see cref="ModelExporter.ExportModel"/>).
///
/// Usage (all arguments are optional except the ones producing an export):
///
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod UmaHeadlessExport.Run ^
///       -umaChar 1001 -umaCostume 00 -umaOut "D:/out/1001.pmx"
///
///   -umaChar &lt;id&gt;        character id to load (see -umaListChars)
///   -umaCostume &lt;id&gt;     costume id, e.g. "00". Defaults to the first costume of the character
///   -umaProp &lt;path&gt;      instead of a character, load and export a prop / scene asset path or name
///                        (see -umaListProps)
///   -umaMotion &lt;path&gt;    load a specific motion onto the character before exporting/recording
///                        (e.g. 3d/motion/racemain/body/type01/anm_rac_type01_run02_stride)
///   -umaOut &lt;path&gt;       .pmx or .fbx file to write (the extension picks the writer). Defaults to
///                          &lt;project&gt;/HeadlessExports/&lt;char&gt;_&lt;costume&gt;.pmx
///   -umaRefine &lt;bool&gt;    FBX only: the optional uma refinement step (eye bones aimed as the Blender
///                          addon aims them). Defaults to Config.json's FbxApplyUmaRefinement
///   -umaEmbedTextures &lt;bool&gt;  FBX only: carry the PNGs inside the file as well as next to it.
///                          Defaults to Config.json's FbxEmbedTextures
///   -umaScene &lt;path&gt;     scene to boot, default Assets/Scenes/Version2.unity
///   -umaListChars        print every character id together with its costume ids, then exit
///   -umaListCostumes     print the costume ids of -umaChar, then exit
///   -umaListProps &lt;f&gt;    print prop/scene assets whose name contains &lt;f&gt; (empty = all), then exit
///   -umaListTextures &lt;f&gt; print character texture assets whose name contains &lt;f&gt; (empty = all, use a
///                        character id), then exit. This is how a variant set is found rather than guessed
///                        at: the wet maps are the "..._wet" twins of the dry ones, so any other set the
///                        game ships would show up here as its own suffix.
///   -umaListFamilies &lt;f&gt; print the family under 3d/chara/ (body, head, tail, mini, common, prop, attach,
///                        richprop, sweat, toonprop) whose name contains &lt;f&gt; (empty = all), with how many
///                        entries it holds, what its prefabs are called, and how many of them the prop
///                        panel's "pfb_chr_prop" filter would show, then exit. Metadata only: this reads
///                        manifest names and opens no bundle, so it cannot download anything. When &lt;f&gt;
///                        selects a single family it also lists that family's folders (attach1048_00,
///                        bdy1001_00: the id and costume each asset belongs to) with entry counts.
///   -umaScanProps &lt;f&gt;    load every prop/scene matching &lt;f&gt; in turn and report its material health
///                        (textureless slots, missing shaders, resolved texture set), then exit
///   -umaScanCount &lt;n&gt;    how many props to scan, default 10
///   -umaScanExport       with -umaScanProps: export each matched prop instead of only reporting it, and
///                        continue past the ones that throw, then report how many of each. This is how a
///                        batch of scenes is checked for the ones that fail to export at all - one editor
///                        launch for the whole batch (see -umaScanOut)
///   -umaScanOut &lt;dir&gt;    where -umaScanExport writes; default &lt;project&gt;/HeadlessExports/scan
///   -umaExportAudio &lt;sheet&gt;  write a sound asset to an mp3 (with -umaOut) carrying its transcript in
///                          the tag: -umaExportAudio snd_voi_story_041047007 -umaExportClip 3 -umaOut x.mp3
///   -umaExportClip &lt;n&gt;     which clip of that asset, default 0; -1 mixes the whole asset
///   -umaTranscript &lt;sheet&gt;  print the lines a story voice sheet is used for (e.g. 041047007, or
///                          snd_voi_story_041047007), together with what the viewer would put on each
///                          clip of that sound asset, then exit
///   -umaDumpMaterials    log material diagnostics for the loaded model before exporting
///   -umaVariant &lt;code&gt;   pick an environment texture set variant (e.g. 212 or 214) before exporting
///   -umaMorphNameMode &lt;n&gt; override Config.PmxMorphNameMode (0 tagged, 1 short english, 2 unified)
///   -umaAttach &lt;f&gt;      wear/hold the 3d/chara/attach or 3d/chara/toonprop prefab(s) whose path contains
///                        &lt;f&gt; on the loaded character (repeatable: one -umaAttach per pattern). Applied after
///                        the settle frames and before any screenshot or export, so the run measures the
///                        character as worn. An attach prefab carries a copy of the character rig and is
///                        retargeted onto the character's bones by name; a toon prop whose own rig sits in its
///                        own space (measured per prefab) is hung off Hand_Attach_R/L instead.
///   -umaAPose            export models with the arms in the A-pose that recorded motions are relative to
///   -umaTextureVariants  also export the game's wet and dirt maps beside the dry ones and name them per
///                        material with wet=/dirt= tokens (off by default: about +80% texture payload)
///   -umaRecordVmd &lt;path&gt; record one loop of the playing animation to a .vmd, then exit (character only)
///   -umaRecordFps &lt;n&gt;    frame rate of that recording, default 30
///   -umaRecordMode &lt;m&gt;   deterministic (default, one pinned frame per sample) or realtime (the legacy
///                        FixedUpdate sampler, useful to compare behaviour)
///   -umaTimeout &lt;sec&gt;    abort after this many seconds, default 600
///   -umaExtraFrames &lt;n&gt;  frames to let the model settle before exporting, default 30
///   -umaPlaySeconds &lt;s&gt; let the loaded motion play for this much wall clock time before exporting or
///                        recording (default 0). Batch mode burns through editor frames in milliseconds,
///                        so without this a one shot animation never reaches its end - which is the state
///                        the viewer is in when a user records after the animation finished.
///
/// Exit code is 0 on success, non zero on failure (the log line prefixed with [UmaHeadlessExport]
/// explains why).
///
/// NOTE: entering play mode triggers a script domain reload, which clears static fields and drops
/// EditorApplication.update subscriptions. All progress state therefore lives in
/// <see cref="CliState"/> and the update loop is re-armed from an [InitializeOnLoadMethod].
/// </summary>
/// <summary>
/// The command line's editor-only probes, as hooks.
///
/// Reading an animation's curves and serialized properties is `UnityEditor` work, and a runtime assembly -
/// which is where the runner lives so a built player can use it - cannot see those types. The editor side
/// installs these when it loads (<see cref="CliEditorProbes"/>); in a player they stay null and the flag that
/// wanted one says so instead of failing.
/// </summary>
public static class CliEditorProbeHooks
{
    public static Action DumpTearBundles;
    public static Action<string, AnimationClip> DumpClipSerialized;
    public static Action<string, Animator, GameObject> DumpTearAnimator;
    public static Action<UmaContainer> DumpShaderFacts;
    public static Action<string, CharaShaderEffectData> DumpShaderEffectData;
    public static Action<GameObject, string> DumpFacePipeline;
}

public static class UmaHeadlessExport
{
    private const string Tag = "[UmaHeadlessExport]";
    private const string DefaultScene = "Assets/Scenes/Version2.unity";

    // SessionState keys (survive the play mode domain reload)
    private const string KeyOptions = "UmaHeadlessExport.Options";
    private const string KeyStage = "UmaHeadlessExport.Stage";
    private const string KeyDeadline = "UmaHeadlessExport.Deadline";
    private const string KeyOutPath = "UmaHeadlessExport.OutPath";
    private const string KeyIsProp = "UmaHeadlessExport.IsProp";
    private const string KeyPropName = "UmaHeadlessExport.PropName";
    private const string KeyVmdPath = "UmaHeadlessExport.VmdPath";
    private const string KeyVmdStarted = "UmaHeadlessExport.VmdStarted";
    private const string KeyVmdDone = "UmaHeadlessExport.VmdDone";
    private const string KeyVmdFrames = "UmaHeadlessExport.VmdFrames";
    private const string KeyMotionLoaded = "UmaHeadlessExport.MotionLoaded";

    /// <summary>
    /// The clip `-umaMotion` applied. Kept because the animator reports the state it is still transitioning *from*
    /// right after a loop motion is started, so reading "the current clip" records the wrong animation.
    /// </summary>
    private static AnimationClip _requestedClip;

    /// <summary>How many times the motion has been re-applied because the idle took the layer back.</summary>
    private static int _recordAttempts;
    private const string KeyScanIndex = "UmaHeadlessExport.ScanIndex";
    private const string KeyScanFailures = "UmaHeadlessExport.ScanFailures";
    private const string KeyBootReported = "UmaHeadlessExport.BootReported";
    private const string KeyRunStarted = "UmaHeadlessExport.RunStarted";
    /// <summary>The scene a run was asked for, so `--doctor` can report it before anything is loaded.</summary>
    private const string KeyScene = "UmaHeadlessExport.Scene";
    /// <summary>Where a `--all-costumes` / `--all-chars` batch has got to, and what it has written so far.</summary>
    private const string KeyBatchIndex = "UmaHeadlessExport.BatchIndex";
    private const string KeyBatchTotal = "UmaHeadlessExport.BatchTotal";
    private const string KeyBatchWritten = "UmaHeadlessExport.BatchWritten";
    private const string KeyBatchBytes = "UmaHeadlessExport.BatchBytes";
    private const string KeyBatchPlanned = "UmaHeadlessExport.BatchPlanned";

    /// <summary>
    /// When the boot wait started. It lives in the session, not in a field, because a player never goes through
    /// the editor's play-mode stage that used to set the clock - so in a player the age of the boot was measured
    /// from year one, every wait it guards was skipped, and a run could export before the english names had
    /// arrived.
    /// </summary>
    private const string KeyBootStarted = "UmaHeadlessExport.BootStarted";
    private const string KeyDumpIndex = "UmaHeadlessExport.DumpIndex";
    private const string KeyDumpTotal = "UmaHeadlessExport.DumpTotal";

    /// <summary>What a `-umaDumpProps` run knows about the models it has written. Seeded from an existing
    /// index.json when the run resumes, so a second run keeps what the first one recorded.</summary>
    private static readonly Dictionary<string, DumpEntry> _dumpEntries = new Dictionary<string, DumpEntry>(StringComparer.Ordinal);

    private static bool _dumpIndexLoaded;

    private enum Stage
    {
        Idle = 0,
        WaitPlayMode,
        WaitStartup,
        WaitModel,
        Settle,
        RecordVmd,
        Export,
        Done,
        Screenshot,
        ExportAudio
    }

        /// <summary>How a flag takes its value, which is what `--help` prints and what the normaliser knows.</summary>
    private enum FlagKind
    {
        /// <summary>`--flag`: the flag is the whole argument.</summary>
        Switch,

        /// <summary>`--flag value`: one value follows.</summary>
        Value,

        /// <summary>`--flag value`: repeatable, one value each time.</summary>
        Repeat,

        /// <summary>`--flag [value]`: takes a value unless the next argument is another flag.</summary>
        Optional,
    }

    /// <summary>One row of the CLI: the name a human types, the name the parser has always taken, what it means.</summary>
    private class Flag
    {
        public string Long;
        public string Internal;
        public FlagKind Kind;
        public string Group;
        public string Help;

        public bool TakesValue => Kind == FlagKind.Value || Kind == FlagKind.Repeat;
    }

    /// <summary>
    /// Every flag this build understands, grouped the way `--help` prints it.
    ///
    /// The single-dash names are the original interface and stay supported - `docs/CLI.md`, the PowerShell
    /// wrapper and every existing script are written in them - but they exist only to stay clear of Unity's own
    /// arguments, which is a problem the double-dash form does not have. So `--out` is the name to type now and
    /// `-umaOut` is the name that still works.
    /// </summary>
    private static readonly Flag[] Flags =
    {
        // model
        new Flag { Long = "--char", Internal = "-umaChar", Kind = FlagKind.Value, Group = "model",
                   Help = "character id, e.g. 1001 (see --list-chars)" },
        new Flag { Long = "--costume", Internal = "-umaCostume", Kind = FlagKind.Value, Group = "model",
                   Help = "costume id inside that character, e.g. 00 (see --list-costumes)" },
        new Flag { Long = "--out", Internal = "-umaOut", Kind = FlagKind.Value, Group = "model",
                   Help = "file to write: .pmx or .fbx" },
        new Flag { Long = "--prop", Internal = "-umaProp", Kind = FlagKind.Value, Group = "model",
                   Help = "prop or scene asset path, or a part of one (see --list-props)" },
        new Flag { Long = "--scene", Internal = "-umaScene", Kind = FlagKind.Value, Group = "model",
                   Help = "the viewer scene to boot; defaults to Assets/Scenes/Version2.unity" },
        new Flag { Long = "--variant", Internal = "-umaVariant", Kind = FlagKind.Value, Group = "model",
                   Help = "a prop's texture set to apply before export" },
        new Flag { Long = "--attach", Internal = "-umaAttach", Kind = FlagKind.Repeat, Group = "model",
                   Help = "wear an attach prefab on the character; repeatable" },
        new Flag { Long = "--refine", Internal = "-umaRefine", Kind = FlagKind.Value, Group = "model",
                   Help = "FBX only: true/false, the uma eye-bone refinement" },
        new Flag { Long = "--embed-textures", Internal = "-umaEmbedTextures", Kind = FlagKind.Value, Group = "model",
                   Help = "FBX only: true/false, embed the textures in the file" },

        // motion
        new Flag { Long = "--all-costumes", Internal = "-umaAllCostumes", Kind = FlagKind.Switch, Group = "motion",
                   Help = "with --char: export every costume that character has, into --out <folder>" },
        new Flag { Long = "--all-chars", Internal = "-umaAllChars", Kind = FlagKind.Switch, Group = "motion",
                   Help = "export every character (add --all-costumes for all of them), into --out <folder>" },
        new Flag { Long = "--motion", Internal = "-umaMotion", Kind = FlagKind.Value, Group = "motion",
                   Help = "motion asset to play, or part of one" },
        new Flag { Long = "--record-vmd", Internal = "-umaRecordVmd", Kind = FlagKind.Value, Group = "motion",
                   Help = "record one loop of the motion into this .vmd" },
        new Flag { Long = "--record-fps", Internal = "-umaRecordFps", Kind = FlagKind.Value, Group = "motion",
                   Help = "frames per second for that take; default 30" },
        new Flag { Long = "--record-reduction", Internal = "-umaRecordReduction", Kind = FlagKind.Value, Group = "motion",
                   Help = "key reduction level for that take" },
        new Flag { Long = "--record-mode", Internal = "-umaRecordMode", Kind = FlagKind.Value, Group = "motion",
                   Help = "how frames are sampled: deterministic or realtime" },
        new Flag { Long = "--play-seconds", Internal = "-umaPlaySeconds", Kind = FlagKind.Value, Group = "motion",
                   Help = "let the motion run this long before measuring" },
        new Flag { Long = "--extra-frames", Internal = "-umaExtraFrames", Kind = FlagKind.Value, Group = "motion",
                   Help = "frames to settle before measuring; default 30" },

        // listing
        new Flag { Long = "--list-chars", Internal = "-umaListChars", Kind = FlagKind.Switch, Group = "listing",
                   Help = "print every character" },
        new Flag { Long = "--list-costumes", Internal = "-umaListCostumes", Kind = FlagKind.Switch, Group = "listing",
                   Help = "print the costumes of --char" },
        new Flag { Long = "--list-props", Internal = "-umaListProps", Kind = FlagKind.Optional, Group = "listing",
                   Help = "print props/scenes, optionally filtered by a substring" },
        new Flag { Long = "--list-textures", Internal = "-umaListTextures", Kind = FlagKind.Optional, Group = "listing",
                   Help = "print textures, optionally filtered by a substring" },
        new Flag { Long = "--list-families", Internal = "-umaListFamilies", Kind = FlagKind.Optional, Group = "listing",
                   Help = "print the 3d/chara families with counts, optionally filtered" },

        // scan and dump
        new Flag { Long = "--scan-props", Internal = "-umaScanProps", Kind = FlagKind.Value, Group = "scanning",
                   Help = "load every prop/scene matching this and report it" },
        new Flag { Long = "--scan-count", Internal = "-umaScanCount", Kind = FlagKind.Value, Group = "scanning",
                   Help = "how many to scan or dump; default 10" },
        new Flag { Long = "--scan-export", Internal = "-umaScanExport", Kind = FlagKind.Switch, Group = "scanning",
                   Help = "export each scanned prop instead of only reporting it" },
        new Flag { Long = "--scan-out", Internal = "-umaScanOut", Kind = FlagKind.Value, Group = "scanning",
                   Help = "where a scan export writes; default HeadlessExports/scan" },
        new Flag { Long = "--dump-props", Internal = "-umaDumpProps", Kind = FlagKind.Value, Group = "scanning",
                   Help = "dump every prop into this folder with an index.json (docs/PROP_DUMP.md)" },
        new Flag { Long = "--dump-props-filter", Internal = "-umaDumpPropsFilter", Kind = FlagKind.Optional, Group = "scanning",
                   Help = "only dump props whose path contains this" },
        new Flag { Long = "--dump-props-count", Internal = "-umaDumpPropsCount", Kind = FlagKind.Value, Group = "scanning",
                   Help = "how many props to dump; 0 is all of them" },
        new Flag { Long = "--probe-morphs", Internal = "-umaProbeMorphs", Kind = FlagKind.Switch, Group = "scanning",
                   Help = "print what the facial morph system holds at runtime" },
        new Flag { Long = "--probe-tears", Internal = "-umaProbeTears", Kind = FlagKind.Switch, Group = "scanning",
                   Help = "print how the animated tears are built, while they play" },
        new Flag { Long = "--dump-shaders", Internal = "-umaDumpShaders", Kind = FlagKind.Switch, Group = "scanning",
                   Help = "print the shader facts of what is loaded" },
        new Flag { Long = "--dump-materials", Internal = "-umaDumpMaterials", Kind = FlagKind.Switch, Group = "scanning",
                   Help = "print every material and its shader properties" },
        new Flag { Long = "--dump-face", Internal = "-umaDumpFace", Kind = FlagKind.Switch, Group = "scanning",
                   Help = "print the face pipeline and write the vertices beside the model" },

        // screenshots
        new Flag { Long = "--screenshot", Internal = "-umaScreenshot", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "render the game's own view to this .png" },
        new Flag { Long = "--shot-view", Internal = "-umaShotView", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "full, upper, head or face" },
        new Flag { Long = "--shot-width", Internal = "-umaShotWidth", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "pixels; 0 follows the screen" },
        new Flag { Long = "--shot-height", Internal = "-umaShotHeight", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "pixels; 0 follows the screen" },
        new Flag { Long = "--shot-yaw", Internal = "-umaShotYaw", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "camera angle around the model, in degrees" },
        new Flag { Long = "--shot-yaws", Internal = "-umaShotYaws", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "several angles, comma separated: one png each" },
        new Flag { Long = "--shot-azimuths", Internal = "-umaShotAzimuths", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "face-light azimuths, comma separated" },
        new Flag { Long = "--shot-light-elevation", Internal = "-umaShotLightElevation", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "rebuild the key light at this height" },
        new Flag { Long = "--shot-transparent", Internal = "-umaShotTransparent", Kind = FlagKind.Switch, Group = "screenshot",
                   Help = "screenshot without the background" },
        new Flag { Long = "--shot-only", Internal = "-umaShotOnly", Kind = FlagKind.Switch, Group = "screenshot",
                   Help = "screenshot and write nothing else" },
        new Flag { Long = "--pin-pose", Internal = "-umaPinPose", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "freeze the model at this normalised time before shooting" },
        new Flag { Long = "--shot-material", Internal = "-umaShotMaterial", Kind = FlagKind.Value, Group = "screenshot",
                   Help = "limit a shot override to materials whose name contains this" },
        new Flag { Long = "--shot-texture", Internal = "-umaShotTexture", Kind = FlagKind.Repeat, Group = "screenshot",
                   Help = "redirect a shader texture term: Property=r,g,b or Property=null" },
        new Flag { Long = "--shot-global", Internal = "-umaShotGlobal", Kind = FlagKind.Repeat, Group = "screenshot",
                   Help = "override a shader global for the shots: Name=r,g,b" },
        new Flag { Long = "--shot-float", Internal = "-umaShotFloat", Kind = FlagKind.Repeat, Group = "screenshot",
                   Help = "override a material float for the shots: Property=value" },

        // what the exporters write
        new Flag { Long = "--morph-name-mode", Internal = "-umaMorphNameMode", Kind = FlagKind.Value, Group = "export",
                   Help = "0 tagged, 1 short english, 2 unified" },
        new Flag { Long = "--a-pose", Internal = "-umaAPose", Kind = FlagKind.Switch, Group = "export",
                   Help = "export the model in the A-pose rest pose" },
        new Flag { Long = "--plain-materials", Internal = "-umaPlainMaterials", Kind = FlagKind.Switch, Group = "export",
                   Help = "export plain MMD materials, without uma shader settings" },
        new Flag { Long = "--no-extra-morphs", Internal = "-umaNoExtraMorphs", Kind = FlagKind.Switch, Group = "export",
                   Help = "export without the deploy and tear morphs" },
        new Flag { Long = "--texture-variants", Internal = "-umaTextureVariants", Kind = FlagKind.Switch, Group = "export",
                   Help = "also write the game's wet and dirt maps" },

        // sound and text
        new Flag { Long = "--transcript", Internal = "-umaTranscript", Kind = FlagKind.Value, Group = "sound",
                   Help = "print the dialogue a story voice sheet carries" },
        new Flag { Long = "--export-audio", Internal = "-umaExportAudio", Kind = FlagKind.Value, Group = "sound",
                   Help = "write a sound asset to an mp3, transcript in its tag" },
        new Flag { Long = "--export-clip", Internal = "-umaExportClip", Kind = FlagKind.Value, Group = "sound",
                   Help = "which clip of that asset; -1 mixes the whole asset" },

        // the run itself
        new Flag { Long = "--timeout", Internal = "-umaTimeout", Kind = FlagKind.Value, Group = "run",
                   Help = "give up after this many seconds; default 600" },
        new Flag { Long = "--boot-timeout", Internal = "-umaBootTimeout", Kind = FlagKind.Value, Group = "run",
                   Help = "how long to wait for the viewer to boot; default 120" },
        new Flag { Long = "--json", Internal = "", Kind = FlagKind.Switch, Group = "run",
                   Help = "finish with one JSON line describing the result" },
        // not --version: Unity answers that one itself and exits before this code runs, with no log file and no
        // explanation. Anything the editor has its own flag for has to be spelled differently here.
        new Flag { Long = "--doctor", Internal = "", Kind = FlagKind.Switch, Group = "run",
                   Help = "report the paths, the game data and the manifest a run would use, then exit" },
        new Flag { Long = "--about", Internal = "", Kind = FlagKind.Switch, Group = "run",
                   Help = "print this build and exit (--version belongs to Unity)" },
        new Flag { Long = "--help", Internal = "", Kind = FlagKind.Switch, Group = "run",
                   Help = "print this and exit" },
    };

    /// <summary>True when <paramref name="name"/> is a flag this build knows (takes `--x` or `--x=value`).</summary>
    public static bool IsKnownFlagName(string name) => FlagsByLongName.ContainsKey(name);

    /// <summary>One command a person can type: a verb, an optional noun, and what it means in flags.</summary>
    private class Verb
    {
        /// <summary>`export`, `list`, `dump`, `scan`.</summary>
        public string Name;

        /// <summary>`model`, `motion`, `props`, ... - what the verb acts on, or "" when it acts on nothing.</summary>
        public string Noun;

        /// <summary>The flags this word implies, as the single-dash names the parser takes.</summary>
        public string[] Implies;

        /// <summary>The flags the command cannot work without.</summary>
        public string[] Requires;

        public string Help;
        public string Example;

        /// <summary>True when <paramref name="argv"/> starts with this command's words.</summary>
        public bool Matches(string[] argv, int at, out int consumed)
        {
            consumed = 0;
            if (at >= argv.Length || !Same(argv[at], Name)) return false;
            consumed = 1;
            if (string.IsNullOrEmpty(Noun)) return true;
            if (at + 1 < argv.Length && Same(argv[at + 1], Noun))
            {
                consumed = 2;
                return true;
            }
            return false;
        }

        private static bool Same(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public string FullName => string.IsNullOrEmpty(Noun) ? Name : Name + " " + Noun;

        /// <summary>True when the command needs this flag (by either spelling).</summary>
        public bool Needs(string flagInternal, string flagLong) => Requires.Any(r => r == flagInternal || r == flagLong);
    }

    /// <summary>
    /// The commands. A verb is sugar with a purpose: it says what a run *is* in the words a person would use, it
    /// fills in the flag that choice implies, and it refuses to start when the run could not produce what was
    /// asked for - `export model` without an `--out`, `list costumes` without a `--char`. The flags underneath are
    /// unchanged, so nothing that worked before stops working; `--char 1001 --out x.pmx` is still a valid
    /// command, it just has no verb in front of it.
    /// </summary>
    private static readonly Verb[] Verbs =
    {
        new Verb { Name = "export", Noun = "model",
                   Implies = new string[0],
                   Requires = new[] { "-umaOut" },
                   Help = "a character or prop as .pmx or .fbx",
                   Example = "export model --char 1001 --costume 00 --out D:/out/1001.pmx" },
        new Verb { Name = "export", Noun = "motion",
                   Requires = new[] { "-umaRecordVmd" },
                   Help = "one loop of a motion as .vmd, with the model if --out is given too",
                   Example = "export motion --char 1001 --motion anm_rac_type01_run02_stride --record-vmd D:/out/run.vmd" },
        new Verb { Name = "export", Noun = "screenshot",
                   Implies = new string[0],
                   Requires = new[] { "-umaScreenshot" },
                   Help = "the game's own view as a .png",
                   Example = "export screenshot --char 1001 --screenshot D:/out/shot.png" },
        new Verb { Name = "export", Noun = "audio",
                   Requires = new[] { "-umaExportAudio" },
                   Help = "one sound clip as an mp3, its line of text in the tag",
                   Example = "export audio --export-audio snd_voi_story_041047007 --export-clip 3 --out D:/out/line.mp3" },
        new Verb { Name = "list", Noun = "chars", Implies = new[] { "-umaListChars" }, Requires = new string[0],
                   Help = "every character and its id",
                   Example = "list chars" },
        new Verb { Name = "list", Noun = "costumes", Implies = new[] { "-umaListCostumes" }, Requires = new[] { "-umaChar" },
                   Help = "the costumes of one character",
                   Example = "list costumes --char 1001" },
        new Verb { Name = "list", Noun = "props", Implies = new[] { "-umaListProps" }, Requires = new string[0],
                   Help = "props and scenes, optionally filtered",
                   Example = "list props book" },
        new Verb { Name = "list", Noun = "textures", Implies = new[] { "-umaListTextures" }, Requires = new string[0],
                   Help = "textures, optionally filtered",
                   Example = "list textures prop" },
        new Verb { Name = "list", Noun = "families", Implies = new[] { "-umaListFamilies" }, Requires = new string[0],
                   Help = "the 3d/chara families and their counts",
                   Example = "list families" },
        new Verb { Name = "dump", Noun = "props", Requires = new[] { "-umaDumpProps" },
                   Help = "every prop into its own folder with index.json",
                   Example = "dump props D:/props --dump-props-filter toon_prop" },
        new Verb { Name = "scan", Noun = "props", Requires = new[] { "-umaScanProps" },
                   Help = "load a family and report how it is rigged",
                   Example = "scan props pfb_chr_prop --scan-count 60" },
        new Verb { Name = "doctor", Noun = "", Implies = new[] { "--doctor" }, Requires = new string[0],
                   Help = "what this build sees: paths, game data, manifest",
                   Example = "doctor" },
    };

    /// <summary>
    /// Rewrites a leading verb into the flags it implies, and checks that the run can do what was asked.
    ///
    /// `dump props D:/props` is also accepted: a command that needs a folder takes the next word as `--dump-props`
    /// when it is not a flag, because typing the flag twice is the sort of thing a person forgets.
    /// </summary>
    /// <summary>True for the program's own path, which is argument zero of every command line.</summary>
    private static bool LooksLikeExecutable(string argument) =>
        argument.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        || argument.IndexOf('/') >= 0
        || argument.IndexOf('\\') >= 0;

    private static string[] ApplyVerb(string[] argv, out string error)
    {
        error = null;
        var expanded = new List<string>(argv);
        if (expanded.Count == 0) return argv;
        // the program's own path is not an argument to anything, and a path sitting next to an optional-value flag
        // gets taken for its value: drop it here rather than let it become a family filter
        for (int drop = 0; drop < expanded.Count && LooksLikeExecutable(expanded[drop]); drop++)
        {
            expanded.RemoveAt(drop);
            break;
        }
        argv = expanded.ToArray();

        // The command can be anywhere on the line, not just first: an editor run is `Unity.exe -batchmode
        // -projectPath . -executeMethod UmaHeadlessExport.Run <command> ...`, so looking at argument zero (or one)
        // finds Unity's own flags and nothing else. Measured - the first version of this did exactly that and
        // every verb was silently ignored.
        int verbAt = -1;
        int consumed = 0;
        Verb verb = null;
        for (int at = 0; at < argv.Length && verb == null; at++)
        {
            if (string.IsNullOrEmpty(argv[at]) || argv[at].StartsWith("-", StringComparison.Ordinal)) continue;

            foreach (var candidate in Verbs)
            {
                if (!candidate.Matches(argv, at, out int words)) continue;
                verb = candidate;
                verbAt = at;
                consumed = words;
                break;
            }
            if (verb != null) break;

            // a verb name with nothing after it: say what can follow rather than "unknown command"
            if (Verbs.Any(candidate => string.Equals(candidate.Name, argv[at], StringComparison.OrdinalIgnoreCase)
                                       && !string.IsNullOrEmpty(candidate.Noun)))
            {
                error = $"'{argv[at]}' needs something to act on. Try one of: "
                        + string.Join(", ", Verbs.Where(candidate => candidate.Name.Equals(argv[at], StringComparison.OrdinalIgnoreCase))
                                                 .Select(candidate => candidate.FullName))
                        + ". See --help.";
                return argv;
            }
        }
        if (verb == null) return argv;      // no command: the plain flags, as they always were

        expanded.RemoveRange(verbAt, consumed);

        // The flag this command is really about: the one it requires, or - for the commands whose flag takes an
        // optional value - the one it implies. A bare word after the command becomes that flag's value, because
        // `dump props D:/props` is what a person types and `--dump-props D:/props` is what the parser wants.
        string subject = null;
        if (verb.Requires.Contains("-umaDumpProps")) subject = "-umaDumpProps";
        else if (verb.Requires.Contains("-umaScanProps")) subject = "-umaScanProps";
        else if (verb.Requires.Contains("-umaExportAudio")) subject = "-umaExportAudio";
        else if (verb.Implies != null)
        {
            subject = verb.Implies.FirstOrDefault(flag =>
                flag == "-umaListProps" || flag == "-umaListTextures" || flag == "-umaListFamilies");
        }

        bool bareWord = expanded.Count > verbAt && !expanded[verbAt].StartsWith("-", StringComparison.Ordinal);
        if (bareWord && subject != null) expanded.Insert(verbAt + 1, expanded[verbAt]);

        // the implied flags go where the command was, so the word stays next to the flag that takes it
        int insertAt = verbAt;
        if (subject != null)
        {
            expanded.Insert(insertAt, subject);
            insertAt++;
        }
        foreach (string implied in verb.Implies ?? new string[0])
        {
            if (implied == subject) continue;
            expanded.Insert(insertAt, implied);
            insertAt++;
        }

        expanded.Insert(0, "--verb=" + verb.FullName);
        return expanded.ToArray();
    }

    /// <summary>
    /// Checks a run against the command it was given: `export model` has to have somewhere to write, `list
    /// costumes` has to know which character. Returns null when the run can go ahead.
    /// </summary>
    private static string VerbProblem(Options options, Verb verb)
    {
        if (verb == null) return null;
        if (verb.Needs("-umaOut", "--out") && string.IsNullOrEmpty(options.OutPath))
        {
            return $"{verb.FullName} needs --out <file> to write";
        }
        if (verb.Needs("-umaChar", "--char") && options.CharId < 0)
        {
            return $"{verb.FullName} needs --char <id> (see `list chars`)";
        }
        if (verb.Needs("-umaMotion", "--motion") && string.IsNullOrEmpty(options.Motion))
        {
            return $"{verb.FullName} needs --motion <asset> (see `list props`)";
        }
        if (verb.Needs("-umaRecordVmd", "--record-vmd") && string.IsNullOrEmpty(options.RecordVmd))
        {
            return $"{verb.FullName} needs --record-vmd <file>";
        }
        if (verb.Needs("-umaScreenshot", "--screenshot") && string.IsNullOrEmpty(options.Screenshot))
        {
            return $"{verb.FullName} needs --screenshot <file>";
        }
        if (verb.Needs("-umaExportAudio", "--export-audio") && string.IsNullOrEmpty(options.ExportAudio))
        {
            return $"{verb.FullName} needs --export-audio <sheet> (see `list` in docs/CLI.md)";
        }
        return null;
    }

    /// <summary>The command a run was given, or null for the plain-flag form.</summary>
    private static Verb VerbOf(Options options)
    {
        if (string.IsNullOrEmpty(options.Verb)) return null;
        return Verbs.FirstOrDefault(v => v.FullName == options.Verb);
    }


    private static readonly Dictionary<string, Flag> FlagsByLongName =
        Flags.Where(flag => flag.Long.StartsWith("--", StringComparison.Ordinal))
             .ToDictionary(flag => flag.Long, flag => flag, StringComparer.Ordinal);

    /// <summary>
    /// Rewrites the double-dash form into the single-dash one the parser has always taken, and expands
    /// `--flag=value`. Anything else - Unity's own arguments, a path, a filter - is passed through untouched.
    ///
    /// Arguments after a bare `--` are passed through even if they look like flags, so a value that starts with
    /// a dash can still be given.
    /// </summary>
    private static string[] Normalise(string[] argv)
    {
        var expanded = new List<string>(argv.Length);
        bool passthrough = false;

        foreach (string argument in argv)
        {
            if (passthrough || string.IsNullOrEmpty(argument))
            {
                expanded.Add(argument);
                continue;
            }
            if (argument == "--")
            {
                passthrough = true;
                continue;
            }
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                expanded.Add(argument);
                continue;
            }

            string name = argument;
            string inlineValue = null;
            int equals = argument.IndexOf('=');
            if (equals > 2)
            {
                name = argument.Substring(0, equals);
                inlineValue = argument.Substring(equals + 1);
            }

            if (!FlagsByLongName.TryGetValue(name, out Flag flag))
            {
                string suggestion = SuggestFlag(name);
                throw new Exception($"unknown option {name}"
                                    + (suggestion != null ? $"; did you mean {suggestion}?" : "; try --help"));
            }

            if (string.IsNullOrEmpty(flag.Internal))
            {
                // a flag the harness itself handles (--help, --json, ...)
                expanded.Add(name);
            }
            else
            {
                expanded.Add(flag.Internal);
            }
            if (inlineValue != null)
            {
                if (!flag.TakesValue && flag.Kind != FlagKind.Optional)
                {
                    throw new Exception($"{name} does not take a value");
                }
                expanded.Add(inlineValue);
            }
        }
        return expanded.ToArray();
    }

    /// <summary>
    /// The flag a mistyped one was probably meant to be: the closest name by edit distance, when it is close
    /// enough to be a typo rather than a different word. A "did you mean" is worth ten lines of help text.
    /// </summary>
    private static string SuggestFlag(string typed)
    {
        string best = null;
        int bestDistance = int.MaxValue;
        foreach (string candidate in FlagsByLongName.Keys)
        {
            int distance = EditDistance(typed, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        int allowed = Mathf.Max(2, typed.Length / 3);
        return bestDistance <= allowed ? best : null;
    }

    /// <summary>Levenshtein distance, for <see cref="SuggestFlag"/>.</summary>
    private static int EditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Mathf.Min(Mathf.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            var swapBuffer = previous;
            previous = current;
            current = swapBuffer;
        }
        return previous[b.Length];
    }

    /// <summary>The `--help` text: one line per flag, grouped the way the commands are used.</summary>
    private static void PrintHelp(string why = null)
    {
        if (!string.IsNullOrEmpty(why)) Debug.LogError($"{Tag} {why}");
        Debug.Log($"{Tag} UmaViewer headless CLI");
        Debug.Log($"{Tag}   exports, records and inspects the game's assets without clicking.");
        Debug.Log($"{Tag}");
        Debug.Log($"{Tag}   Through the wrapper:  ./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001.pmx");
        Debug.Log($"{Tag}   Or straight from Unity, with the double-dash flags:");
        Debug.Log($"{Tag}     Unity.exe -batchmode -projectPath . -executeMethod UmaHeadlessExport.Run --char 1001 --out x.pmx");
        Debug.Log($"{Tag}");
        Debug.Log($"{Tag}   Both spellings work here: --out is the name to type, -umaOut the one that always has.");
        Debug.Log($"{Tag}   Values with spaces need quotes. Repeatable flags take one value each time.");

        Debug.Log($"{Tag}");
        Debug.Log($"{Tag} commands (or pass the flags alone, without a command)");
        foreach (var verb in Verbs)
        {
            Debug.Log($"{Tag}   {verb.FullName,-18} {verb.Help}");
            Debug.Log($"{Tag}   {new string(' ', 18)} {verb.Example}");
        }

        foreach (var group in Flags.GroupBy(flag => flag.Group))
        {
            Debug.Log($"{Tag}");
            Debug.Log($"{Tag} {group.Key}");
            foreach (var flag in group)
            {
                string value = flag.Kind switch
                {
                    FlagKind.Switch => "",
                    FlagKind.Optional => " [value]",
                    _ => " <value>",
                };
                string alias = string.IsNullOrEmpty(flag.Internal) ? "" : $"  (also {flag.Internal})";
                Debug.Log($"{Tag}   {flag.Long + value,-26} {flag.Help}{alias}");
            }
        }
    }

    /// <summary>
    /// Prints the environment a run would use: where the game data is, what the manifest holds, whether the scene
    /// and the shaders are there, and which of the usual traps are set.
    ///
    /// It exists because every way this tool fails for a person is an environment question - the wrong data
    /// folder, a missing `master.mdb`, a manifest that never loaded, a shader list that came back empty because
    /// the editor is offline - and the alternative is a one line exception that says none of it.
    /// </summary>
    /// <param name="stage">What to call this report, or null for the full one taken after the viewer booted.</param>
    private static void PrintDoctor(string stage)
    {
        var config = Config.Instance;
        var main = UmaViewerMain.Instance;
        var builder = UmaViewerBuilder.Instance;
        if (config == null)
        {
            Debug.Log($"{Tag} --- environment ---");
            Debug.Log($"{Tag}   the viewer's configuration is not loaded yet; nothing to report");
            return;
        }

        Debug.Log($"{Tag} --- environment ---");
        Debug.Log($"{Tag}   build        {Application.productName} {Application.version}"
                  + (CliHost.IsEditor ? " (editor)" : " (player)"));
        if (!CliHost.IsEditor)
        {
            Debug.Log($"{Tag}   unity        {Application.unityVersion}");
            Debug.Log($"{Tag}   executable   {Application.dataPath}");
        }
        Debug.Log($"{Tag}   config file  {ConfigPath()}");
        Debug.Log($"{Tag}   scene        {CliState.GetString(KeyScene, DefaultScene)}");
        Debug.Log($"{Tag}   data path    {config.MainPath}");
        Debug.Log($"{Tag}   work mode    {config.WorkMode}");
        Debug.Log($"{Tag}   language     {config.Language} (uma names come out "
                  + (config.Language == Language.En ? "english if the translation download worked" : "japanese")
                  + ")");
        Debug.Log($"{Tag}   downloads    {config.DownloadMissingResources}");

        Debug.Log($"{Tag} --- game data ---");
        foreach (string entry in GameDataReport(config))
        {
            Debug.Log($"{Tag}   {entry}");
        }

        if (main == null || builder == null)
        {
            Debug.Log($"{Tag} --- viewer ---");
            Debug.Log($"{Tag}   not up yet, so the manifest and the shaders are not reported"
                      + (stage != null ? $" ({stage})" : ""));
            return;
        }

        Debug.Log($"{Tag} --- manifest ---");
        Debug.Log($"{Tag}   assets       {main.AbList?.Count ?? 0}");
        Debug.Log($"{Tag}   3d/chara     {main.AbChara?.Count ?? 0}");
        Debug.Log($"{Tag}   scenes       {main.AbList?.Values.Count(e => e.Name.StartsWith("3d/env", StringComparison.OrdinalIgnoreCase)) ?? 0}");
        Debug.Log($"{Tag}   motions      {main.AbMotions?.Count ?? 0}");
        Debug.Log($"{Tag}   characters   {main.Characters?.Count ?? 0}");
        Debug.Log($"{Tag}   shaders      {builder.ShaderList?.Count ?? 0}");
        int translated = main.Translations != null && main.Translations.ContainsKey(TranslationTables.UmaNames)
            ? main.Translations[TranslationTables.UmaNames].Count
            : 0;
        Debug.Log($"{Tag}   translated   {translated} uma name(s)");
        Debug.Log($"{Tag}   props        {DumpCandidates("", 0).Count} (docs/PROP_FAMILIES.md)");
    }

    /// <summary>Where the config actually is, in the host this is running in.</summary>
    private static string ConfigPath()
    {
        string folder = Path.GetDirectoryName(Application.dataPath);
        string next = Path.Combine(folder, "Config.json");
        return File.Exists(next) ? next : "(not next to the executable; defaults are in use)";
    }

    /// <summary>The files a run needs to find, and whether each is there.</summary>
    private static IEnumerable<string> GameDataReport(Config config)
    {
        bool standalone = config.WorkMode == WorkMode.Standalone;
        yield return Check("meta", Path.Combine(config.MainPath, standalone ? "meta_umaviewer" : "meta"));
        yield return Check("master.mdb", Path.Combine(config.MainPath, "master",
                                                     standalone ? "master_umaviewer.mdb" : "master.mdb"));
        yield return Check("dat folder", Path.Combine(config.MainPath, "dat"));
        yield return Check("scene asset", CliState.GetString(KeyScene, DefaultScene));
    }

    /// <summary>One `name path PRESENT/MISSING` line.</summary>
    private static string Check(string label, string path)
    {
        bool file = File.Exists(path);
        bool directory = Directory.Exists(path);
        string state = file || directory ? "ok     " : "MISSING";
        string extra = directory ? " (folder)" : "";
        return $"{state} {label,-11} {path}{extra}";
    }

    /// <summary>
    /// The doctor's scene line reads the option that was asked for, so a run pointed at another scene reports
    /// that one rather than the default.
    /// </summary>
    private static void RememberScene(Options options) => CliState.SetString(KeyScene, options.ScenePath);

    /// <summary>What this build is: the one line a bug report needs.</summary>
    private static void PrintVersion()
    {
        Debug.Log($"{Tag} UmaViewer headless CLI, {Application.unityVersion}, "
                  + $"{Flags.Length} flag(s); docs/CLI.md");
    }
[Serializable]
    private class Options
    {
        public string ScenePath = DefaultScene;
        public int CharId = -1;
        public string CostumeId = "";
        public string OutPath = "";
        /// <summary>
        /// FBX only: -1 keeps Config.json's FbxApplyUmaRefinement, 0 turns the optional uma refinement off
        /// and 1 turns it on. An int rather than a bool?, because Options travels through SessionState as
        /// JsonUtility JSON and JsonUtility drops nullable fields.
        /// </summary>
        public int Refine = -1;
        /// <summary>FBX only: -1 keeps Config.json's FbxEmbedTextures, 0 off, 1 on.</summary>
        public int EmbedTextures = -1;
        public string Prop = "";
        public string Motion = "";
        public bool ListChars;
        public bool ListCostumes;
        public bool ListProps;
        public string ListPropsFilter = "";
        public bool ListTextures;
        public string ListTexturesFilter = "";
        public bool ListFamilies;
        public string ListFamiliesFilter = "";
        /// <summary>
        /// Attach prefabs to wear on the loaded character before measuring (-umaAttach, repeatable). Each value is
        /// a substring of an asset path under 3d/chara/attach/; every match is worn, after the settle frames and
        /// before any screenshot or export, so what a run reports includes them.
        /// </summary>
        public List<string> Attach = new List<string>();
        public string ScanProps = "";
        public int ScanCount = 10;
        /// <summary>
        /// With -umaScanProps: *export* every matched prop instead of only reporting its material health, and
        /// keep going after one that throws. This is the difference between finding a broken scene by
        /// launching the editor once per scene and finding it in one run: three quarters of the export time is
        /// the viewer booting, and a sweep boots it once for the whole batch.
        /// </summary>
        public bool ScanExport;

        /// <summary>
        /// `-umaDumpProps <dir>`: export *every* prop into its own folder, plus an index.json describing the
        /// lot. This is the dump a web viewer or a picker reads: one model per folder with its own textures, so
        /// each one can be fetched on its own, and an index so a page never has to walk a folder tree.
        /// </summary>
        public string DumpProps = "";

        /// <summary>Only dump props whose asset path contains this (e.g. `rich_prop`). Empty dumps all of them.</summary>
        public string DumpPropsFilter = "";

        /// <summary>How many props to dump; 0 means every prop in the manifest.</summary>
        public int DumpPropsCount;
        /// <summary>Where the sweep writes: one `.pmx` per prop, plus the shared `Texture2D/` folder.</summary>
        public string ScanOutDir = "";
        public bool DumpMaterials;
        // a sound asset to write to an mp3, with the transcript in its tag
        public string ExportAudio = "";
        public int ExportClip = 0;
        // a story voice sheet to print the transcript of, without loading or exporting anything
        public string Transcript = "";
        public bool DumpFace;
        // what the facial morph system actually holds at runtime: the extra morphs, their locators and
        // their binds, and whether the cheek/texture plumbing behind them got filled in
        public bool ProbeMorphs;
        // how the animated tears are built: they are skinned meshes posed by an Animator, so the mesh the exporter
        // reads is a bind pose and not necessarily the shape the game draws
        public bool ProbeTears;
        // how long -ProbeTears watches the tears with the animator actually running, in wall clock seconds
        public double TearSampleSeconds = 2.0;
        public bool DumpShaders;
        public string Variant = "";
        public int MorphNameMode = -1;
        public bool APoseRestPose;
        public bool PlainMaterials;
        // the other opt-out: no deploy/tear morphs and none of the tokens that explain them
        public bool NoExtraMorphs;
        /// <summary>Also export the game's wet and dirt variants of the maps (Config.PmxTextureVariants).</summary>
        public bool TextureVariants;
        public string RecordVmd = "";
        public int RecordFps = 30;
        public int RecordReduction = 0;
        public string RecordMode = "deterministic";
        /// <summary>`--help`: print the flags and exit without running anything.</summary>
        public bool Help;

        /// <summary>`--version`: print what this build is and exit.</summary>
        public bool Version;

        /// <summary>`--json`: finish with one machine readable line describing the result.</summary>
        public bool Json;

        /// <summary>`--doctor`: report the environment a run would use and exit.</summary>
        public bool Doctor;

        /// <summary>`--all-costumes`: every costume of `--char`, into `--out` as a folder.</summary>
        public bool AllCostumes;

        /// <summary>`--all-chars`: every character, into `--out` as a folder.</summary>
        public bool AllChars;

        /// <summary>
        /// The command this run was given ("export model"), or "" for the plain-flag form. Set by
        /// <see cref="ApplyVerb"/> and used for validation and for the summary line, not for behaviour.
        /// </summary>
        public string Verb = "";

        public double TimeoutSeconds = 600;
        public double BootTimeoutSeconds = 120;
        public int ExtraFrames = 30;
        public double PlaySeconds = 0;
        public string Screenshot = "";
        public string ShotView = "full";
        public int ShotWidth = 0;
        public int ShotHeight = 0;
        public double ShotYaw = 0;
        // several yaws in one run: "-umaShotYaw 0,45,-45" writes one png per angle, which is what shading work
        // is judged on - the same view under different light directions
        public string ShotYaws = "";
        // azimuths for the face light, in degrees: the Nars face shader shades with _ViewDirX/_ViewDirY
        public string ShotAzimuths = "";

        // for probing which texture a term of the game's compiled shader reads: "Property=r,g,b" or
        // "Property=null", applied before the shots and undone after them
        public List<string> ShotTextures = new List<string>();

        // for probing what a shader *global* does to the face - the viewer's own global buffer carries
        // the fog and lightmap colours, so the absolute brightness of its render is scene state
        public List<string> ShotGlobals = new List<string>();

        // for probing a material float: _faceShadowAlpha is driven by the Shade_Ctrl morph rather than
        // being a material constant, so its rest value is 0 and the cheek and nose regions never show
        public List<string> ShotFloats = new List<string>();

        // freezes the model at this normalised time before the shots, because an A/B pair is only a
        // comparison if the pose is the same in both. Negative leaves the animation alone.
        public double PinPose = -1;

        // limits -umaShotTexture / -umaShotFloat / -umaShotGlobal's face work to materials whose name
        // contains this. Without it a property that exists on both the face and the body changes both, and
        // the result is not comparable with a port that only touches the face.
        public string ShotMaterial = "";
        // -1 keeps the scene's own elevation; anything else rebuilds the light direction at that height
        public double ShotLightElevation = -1.0;
        public bool ShotTransparent;
        public bool ShotOnly;

        public static Options Parse(string[] argv)
        {
            var options = new Options();
            // a verb, if there is one, becomes the flags it stands for before anything else looks at the arguments
            argv = ApplyVerb(argv, out string verbError);
            if (verbError != null) throw new Exception(verbError);
            foreach (string argument in argv)
            {
                if (argument.StartsWith("--verb=", StringComparison.Ordinal))
                {
                    options.Verb = argument.Substring("--verb=".Length);
                }
            }
            // the marker is not a flag: drop it so the switch below never sees it
            argv = argv.Where(argument => !argument.StartsWith("--verb=", StringComparison.Ordinal)).ToArray();
            // the double-dash names are rewritten into the single-dash ones the switch below knows, so there is one
            // parser and one set of cases however a caller spells a flag
            string[] normalised = Normalise(argv);
            for (int i = 0; i < normalised.Length; i++)
            {
                string key = normalised[i];
                string Next() => i + 1 < normalised.Length ? normalised[++i] : throw new Exception($"missing value for {key}");

                switch (key)
                {
                    case "-umaChar": options.CharId = int.Parse(Next()); break;
                    case "-umaCostume": options.CostumeId = Next(); break;
                    case "-umaOut": options.OutPath = Next(); break;
                    case "-umaRefine": options.Refine = bool.Parse(Next()) ? 1 : 0; break;
                    case "-umaEmbedTextures": options.EmbedTextures = bool.Parse(Next()) ? 1 : 0; break;
                    case "-umaScene": options.ScenePath = Next(); break;
                    case "-umaProp": options.Prop = Next(); break;
                    case "-umaMotion": options.Motion = Next(); break;
                case "-umaAllCostumes": options.AllCostumes = true; break;
                case "-umaAllChars": options.AllChars = true; break;
                    case "-umaListChars": options.ListChars = true; break;
                    case "-umaListCostumes": options.ListCostumes = true; break;
                    case "-umaListProps":
                        options.ListProps = true;
                        // the filter is optional: only consume the next argument if it is a value,
                        // not another -umaX switch
                        if (i + 1 < normalised.Length && !normalised[i + 1].StartsWith("-")) options.ListPropsFilter = normalised[++i];
                        break;
                    case "-umaListTextures":
                        options.ListTextures = true;
                        if (i + 1 < normalised.Length && !normalised[i + 1].StartsWith("-")) options.ListTexturesFilter = normalised[++i];
                        break;
                    case "-umaListFamilies":
                        options.ListFamilies = true;
                        if (i + 1 < normalised.Length && !normalised[i + 1].StartsWith("-")) options.ListFamiliesFilter = normalised[++i];
                        break;
                    case "-umaAttach": options.Attach.Add(Next()); break;
                    case "-umaScanProps": options.ScanProps = Next(); break;
                    case "-umaScanCount": options.ScanCount = int.Parse(Next()); break;
                    case "-umaScanExport": options.ScanExport = true; break;
                case "-umaDumpProps": options.DumpProps = Next(); break;
                case "-umaDumpPropsFilter":
                    // optional value: the wrapper omits an empty filter, but a script that passes one
                    // anyway must not swallow the next switch as the filter
                    if (i + 1 < normalised.Length && !normalised[i + 1].StartsWith("-")) options.DumpPropsFilter = normalised[++i];
                    break;
                case "-umaDumpPropsCount": options.DumpPropsCount = int.Parse(Next()); break;
                    case "-umaScanOut": options.ScanOutDir = Next(); break;
                    case "-umaTranscript": options.Transcript = Next(); break;
                    case "-umaExportAudio": options.ExportAudio = Next(); break;
                    case "-umaExportClip": options.ExportClip = int.Parse(Next()); break;
                    case "-umaDumpMaterials": options.DumpMaterials = true; break;
                    case "-umaDumpFace": options.DumpFace = true; break;
            case "-umaProbeMorphs": options.ProbeMorphs = true; break;
            case "-umaProbeTears": options.ProbeTears = true; break;
            case "-umaDumpShaders": options.DumpShaders = true; break;
                    case "-umaShotTexture": options.ShotTextures.Add(Next()); break;
                    case "-umaShotGlobal": options.ShotGlobals.Add(Next()); break;
                    case "-umaShotFloat": options.ShotFloats.Add(Next()); break;
                    case "-umaPinPose": options.PinPose = double.Parse(Next()); break;
                    case "-umaShotMaterial": options.ShotMaterial = Next(); break;
                    case "-umaVariant": options.Variant = Next(); break;
                    case "-umaMorphNameMode": options.MorphNameMode = int.Parse(Next()); break;
                    case "-umaAPose": options.APoseRestPose = true; break;
                    case "-umaPlainMaterials": options.PlainMaterials = true; break;
                    case "-umaNoExtraMorphs": options.NoExtraMorphs = true; break;
                    case "-umaTextureVariants": options.TextureVariants = true; break;
                    case "-umaRecordVmd": options.RecordVmd = Next(); break;
                    case "-umaRecordFps": options.RecordFps = int.Parse(Next()); break;
                    case "-umaRecordReduction": options.RecordReduction = int.Parse(Next()); break;
                    case "-umaRecordMode": options.RecordMode = Next(); break;
                    case "-umaTimeout": options.TimeoutSeconds = double.Parse(Next()); break;
                    case "-umaBootTimeout": options.BootTimeoutSeconds = double.Parse(Next()); break;
                    case "-umaExtraFrames": options.ExtraFrames = int.Parse(Next()); break;
                    case "-umaPlaySeconds": options.PlaySeconds = double.Parse(Next()); break;
                    case "-umaScreenshot": options.Screenshot = Next(); break;
                    case "-umaShotView": options.ShotView = Next().ToLowerInvariant(); break;
                    case "-umaShotWidth": options.ShotWidth = int.Parse(Next()); break;
                    case "-umaShotHeight": options.ShotHeight = int.Parse(Next()); break;
                    case "-umaShotYaw": options.ShotYaw = double.Parse(Next()); break;
                    case "-umaShotYaws": options.ShotYaws = Next(); break;
                    case "-umaShotAzimuths": options.ShotAzimuths = Next(); break;
                    case "-umaShotLightElevation": options.ShotLightElevation = double.Parse(Next()); break;
                    case "-umaShotTransparent": options.ShotTransparent = true; break;
                    case "-umaShotOnly": options.ShotOnly = true; break;
                    case "--help": options.Help = true; break;
                    case "--about": options.Version = true; break;
                    case "--doctor": options.Doctor = true; break;
                    case "--json": options.Json = true; break;
                    default:
                        // anything else is Unity's own argument or the shell's; but a -uma flag nobody handles
                        // means the flag table and this switch have drifted apart, which is worth saying out loud
                        if (key.StartsWith("-uma", StringComparison.Ordinal))
                        {
                            Debug.LogWarning($"{Tag} ignoring unknown flag {key} (is the flag table out of date?)");
                        }
                        break;
                }
            }
            return options;
        }
    }

    private static Options CurrentOptions =>
        JsonUtility.FromJson<Options>(CliState.GetString(KeyOptions, "{}"));

    private static Stage CurrentStage
    {
        get => (Stage)CliState.GetInt(KeyStage, (int)Stage.Idle);
        set => CliState.SetInt(KeyStage, (int)value);
    }

    private static int _settleFramesLeft;
    private static bool _attachmentsApplied;
    private static bool _translationWarned;

    /// <summary>How long a run waits for the english name translations before going ahead without them.</summary>
    private const double TranslationWaitSeconds = 15.0;
    private static double _playDeadline;
    private static DateTime _playModeEnteredUtc;
    /// <summary>
    /// When the run started. It lives in the session rather than in a field because entering play mode reloads
    /// the domain: a field reads back as year one after that, which made the play mode retry heuristic fire on
    /// every tick and the result line report zero seconds.
    /// </summary>
    private static DateTime RunStartedUtc
    {
        get
        {
            string ticks = CliState.GetString(KeyRunStarted, "");
            return long.TryParse(ticks, out long value) && value > 0
                ? new DateTime(value, DateTimeKind.Utc)
                : default;
        }
    }
    private static int _playModeRetries;

    /// <summary>
    /// Entry point for the editor's `-executeMethod`. Everything after the argument parsing is shared with the
    /// player entry (<see cref="RunInPlayer"/>), so both hosts run the same stages; only the way the scene is
    /// reached differs.
    /// </summary>
    public static void Run() => Begin(Environment.GetCommandLineArgs());

    /// <summary>
    /// True when this process was started with command line flags this build understands. The player bootstrap
    /// asks before doing anything, so a normal launch of the viewer is untouched by any of this.
    /// </summary>
    public static bool LooksLikeCommandLine(string[] argv)
    {
        foreach (string argument in argv)
        {
            if (string.IsNullOrEmpty(argument)) continue;
            if (argument.StartsWith("-uma", StringComparison.Ordinal)) return true;
            if (!argument.StartsWith("--", StringComparison.Ordinal)) continue;
            if (IsKnownFlagName(argument.Split('=')[0])) return true;
        }

        // a command line can be nothing but a verb: `UmaViewer.exe list families` has no flags at all. Asking the
        // verb table is the only way to tell that apart from a normal launch, and forgetting it meant those
        // commands started the viewer and did nothing at all.
        return ApplyVerb(argv, out _).Any(argument => argument.StartsWith("--verb=", StringComparison.Ordinal));
    }

    /// <summary>
    /// The shared beginning of a run: parse, answer --help/--about, validate, then set the state machine going.
    /// In the editor the scene is opened and play mode is entered; in a player the scene is already the one
    /// running, so the stages start at <see cref="Stage.WaitStartup"/> instead.
    /// </summary>
    public static void Begin(string[] argv)
    {
        Options options;
        try
        {
            options = Options.Parse(argv);
        }
        catch (Exception ex)
        {
            Debug.LogError($"{Tag} invalid arguments: {ex.Message}");
            CliHost.Exit(2);
            return;
        }

        if (!string.IsNullOrEmpty(options.Verb))
        {
            string problem = VerbProblem(options, VerbOf(options));
            if (problem != null)
            {
                Debug.LogError($"{Tag} {problem}");
                Debug.LogError($"{Tag} run it as: {VerbOf(options)?.Example}");
                CliHost.Exit(2);
                return;
            }
        }

        if (options.Help)
        {
            PrintHelp();
            // every mode ends with this marker, including the ones that print and exit: a wrapper waits for it
            // and a mode that only logs its own summary looks like a hang
            Debug.Log($"{Tag} OK");
            CliHost.Exit(0);
            return;
        }

        if (options.Version)
        {
            PrintVersion();
            Debug.Log($"{Tag} OK");
            CliHost.Exit(0);
            return;
        }

        if (!options.ListChars && !options.ListCostumes && !options.ListProps && !options.ListTextures
            && !options.ListFamilies
            && string.IsNullOrEmpty(options.ScanProps) && string.IsNullOrEmpty(options.Transcript)
            && string.IsNullOrEmpty(options.ExportAudio) && string.IsNullOrEmpty(options.DumpProps)
            && !options.Doctor
            && !options.AllCostumes && !options.AllChars
            && options.CharId < 0 && string.IsNullOrEmpty(options.Prop))
        {
            Debug.LogError($"{Tag} nothing to do: pass --char <id> or --prop <path> (optionally --out), "
                           + "or --list-chars / --list-costumes / --list-props / --list-textures "
                           + "/ --list-families / --scan-props / --dump-props / --transcript. See --help.");
            CliHost.Exit(2);
            return;
        }

        if (!File.Exists(options.ScenePath))
        {
            Debug.LogError($"{Tag} scene not found: {options.ScenePath}");
            CliHost.Exit(2);
            return;
        }

        RememberScene(options);
        CliState.SetString(KeyOptions, JsonUtility.ToJson(options));
        CliState.SetString(KeyDeadline, DateTime.UtcNow.AddSeconds(options.TimeoutSeconds).Ticks.ToString());
        CliState.SetString(KeyOutPath, "");
        CliState.SetInt(KeyScanFailures, 0);
        CliState.SetInt(KeyBootReported, 0);
        CliState.SetString(KeyBootStarted, "");
        CliState.SetString(KeyRunStarted, DateTime.UtcNow.Ticks.ToString());
        _playModeRetries = 0;
        FaceDumped = false;
        _attachmentsApplied = false;
        _translationWarned = false;

        // a player is already past "start the scene and enter play mode": the scene it was built with is running
        // and the viewer is booting in it, so the run starts at the stage that waits for that boot
        CurrentStage = CliHost.IsEditor ? Stage.WaitPlayMode : Stage.WaitStartup;
        Debug.Log($"{Tag} starting a run in {(CliHost.IsEditor ? "the editor" : "a player")}, "
                  + $"scene {options.ScenePath}");

        CliHost.OpenScene(options.ScenePath);
        Arm();
        if (CliHost.IsEditor) CliHost.EnterPlayMode();
    }

    /// <summary>
    /// Runs after every domain reload (including the one caused by entering play mode), which is
    /// where the update subscription made by <see cref="Run"/> gets lost.
    /// </summary>
#if UNITY_EDITOR
    /// <summary>
    /// Runs after every domain reload (including the one caused by entering play mode), which is
    /// where the update subscription made by <see cref="Run"/> gets lost.
    /// </summary>
    [UnityEditor.InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        if (CurrentStage != Stage.Idle) Arm();
    }
#endif


    private static void Arm() => CliHost.Arm(Tick);


    private static void Tick()
    {
        var stage = CurrentStage;
        if (stage == Stage.Idle || stage == Stage.Done) return;

        var options = CurrentOptions;
        if (DateTime.UtcNow.Ticks > long.Parse(CliState.GetString(KeyDeadline, "0")))
        {
            Fail($"timed out after {options.TimeoutSeconds}s in stage {stage}");
            return;
        }

        try
        {
            switch (stage)
            {
                case Stage.WaitPlayMode:
                    if (CliHost.IsPlaying)
                    {
                        _playModeEnteredUtc = DateTime.UtcNow;
                        Debug.Log($"{Tag} play mode entered");
                        CurrentStage = Stage.WaitStartup;
                        break;
                    }

                    // Entering play mode is cancelled by a script recompile while the editor is starting,
                    // which otherwise leaves the run sitting here until the timeout.
                    if ((DateTime.UtcNow - RunStartedUtc).TotalSeconds > 20)
                    {
                        _playModeRetries++;
                        if (_playModeRetries > 6)
                        {
                            Fail("play mode never started; a script change while the editor was starting cancels it");
                            return;
                        }
                        Debug.Log($"{Tag} play mode has not started yet, asking again ({_playModeRetries})");
                        CliHost.EnterPlayMode();
                    }
                    return;

                case Stage.ExportAudio:
                    if (!_audioExported) return;
                    ReportTag(CliState.GetString(KeyOutPath, ""));
                    Succeed();
                    return;

                case Stage.WaitStartup:
                {
                    if (CliState.GetString(KeyBootStarted, "") == "")
                    {
                        CliState.SetString(KeyBootStarted, DateTime.UtcNow.Ticks.ToString());
                    }

                    var main = UmaViewerMain.Instance;
                    var builder = UmaViewerBuilder.Instance;
                    if (main == null || builder == null) return;

                    // UmaViewerMain.Start() ends by loading the shader list, but it also downloads the
                    // english translations from GitHub first - on a slow or blocked network that never
                    // completes, so fall through after a while instead of hanging forever.
                    bool fullyBooted = builder.ShaderList != null && builder.ShaderList.Count > 0;
                    bool usable = main.AbList != null && main.AbList.Count > 0;
                    long bootTicks = long.TryParse(CliState.GetString(KeyBootStarted, ""), out long started) ? started : 0;
                    double bootSeconds = bootTicks > 0
                        ? (DateTime.UtcNow - new DateTime(bootTicks, DateTimeKind.Utc)).TotalSeconds
                        : 0;

                    // The english names arrive from a download that runs beside the rest of the boot, and a run
                    // that starts without them writes the japanese name into the model's english name field. Give
                    // them a few seconds of their own - not the whole boot timeout, which would stall an offline
                    // machine for two minutes over a nicety.
                    bool wantNames = Config.Instance != null && Config.Instance.Language == Language.En;
                    int nameCount = main.Translations != null && main.Translations.ContainsKey(TranslationTables.UmaNames)
                        ? main.Translations[TranslationTables.UmaNames].Count
                        : 0;
                    bool namesSettled = !wantNames || nameCount > 0 || bootSeconds >= TranslationWaitSeconds;

                    if (!usable || !namesSettled || (!fullyBooted && bootSeconds < options.BootTimeoutSeconds)) return;

                    // once per run: a -umaScanExport sweep comes back through this stage for every prop, and
                    // the boot summary is about the run, not about the prop
                    if (CliState.GetInt(KeyBootReported, 0) == 0)
                    {
                        CliState.SetInt(KeyBootReported, 1);
                        Debug.Log($"{Tag} viewer booted: {main.Characters.Count} characters, {main.AbChara.Count} chara assets, "
                                  + $"{builder.ShaderList.Count} shaders"
                                  + (fullyBooted ? "" : $" (after {bootSeconds:F0}s without the startup download finishing)"));
                    }

                    if (Config.Instance.Language == Language.En && !_translationWarned)
                    {
                        int names = main.Translations != null && main.Translations.ContainsKey(TranslationTables.UmaNames)
                            ? main.Translations[TranslationTables.UmaNames].Count
                            : 0;
                        if (names == 0)
                        {
                            _translationWarned = true;
                            Debug.LogWarning($"{Tag} no english name translations arrived (they are downloaded from "
                                             + "GitHub at startup); a model exported now carries the japanese name "
                                             + "in its english name field. --doctor reports what did arrive.");
                        }
                    }

                    if (options.Doctor)
                    {
                        PrintDoctor(null);
                        Succeed();
                        return;
                    }

                    if (options.MorphNameMode >= 0)
                    {
                        // the enum no longer has a slot 3 (it used to be "both"), and an unknown value would
                        // otherwise be cast straight through and only show up as the wrong spelling in the file
                        if (options.MorphNameMode > (int)PmxMorphNameMode.Unified)
                        {
                            Fail($"-umaMorphNameMode takes 0 (tagged), 1 (short english) or 2 (unified), not {options.MorphNameMode}");
                            return;
                        }

                        Config.Instance.PmxMorphNameMode = (PmxMorphNameMode)options.MorphNameMode;
                        Debug.Log($"{Tag} morph naming mode overridden to {Config.Instance.PmxMorphNameMode} "
                                  + $"({(int)Config.Instance.PmxMorphNameMode})");
                    }

                    if (options.APoseRestPose)
                    {
                        Config.Instance.PmxAPoseRestPose = true;
                        Debug.Log($"{Tag} exporting models in the A-pose rest pose "
                                  + $"(upper arms rotated {UmaAPose.Degrees} degrees down)");
                    }

                    if (options.PlainMaterials)
                    {
                        Config.Instance.PmxUmaMaterialFields = false;
                        Debug.Log($"{Tag} exporting plain MMD materials: no uma shader settings in the material "
                                  + "comment, and the plain diffuse/specular/outline values");
                    }

                    if (options.NoExtraMorphs)
                    {
                        Config.Instance.PmxExtraMorphs = false;
                        Debug.Log($"{Tag} exporting without the extra morphs: no deploy or tear morphs, and none "
                                  + "of the morph tokens that describe them");
                    }

                    if (options.TextureVariants)
                    {
                        Config.Instance.PmxTextureVariants = true;
                        Debug.Log($"{Tag} exporting the game's wet and dirt maps beside the dry ones, named per "
                                  + "material by wet=/dirt= tokens");
                    }

                    if (options.Refine >= 0)
                    {
                        Config.Instance.FbxApplyUmaRefinement = options.Refine == 1;
                        Debug.Log($"{Tag} FBX uma refinement "
                                  + (Config.Instance.FbxApplyUmaRefinement ? "on" : "off")
                                  + (Config.Instance.FbxApplyUmaRefinement
                                      ? " (eye bones aimed at the gaze as the Blender addon aims them)"
                                      : " (the rig is exported exactly as the viewer holds it)"));
                    }

                    if (options.EmbedTextures >= 0)
                    {
                        Config.Instance.FbxEmbedTextures = options.EmbedTextures == 1;
                        Debug.Log($"{Tag} FBX textures "
                                  + (Config.Instance.FbxEmbedTextures
                                      ? "embedded (the file stays textured when moved on its own)"
                                      : "referenced only (keep the Texture2D folder next to the file)"));
                    }

                    if (!string.IsNullOrEmpty(options.ExportAudio))
                    {
                        if (!StartAudioExport(options)) return;
                        CurrentStage = Stage.ExportAudio;
                        return;
                    }

                    if (!string.IsNullOrEmpty(options.Transcript))
                    {
                        PrintTranscript(options.Transcript);
                        Succeed();
                        return;
                    }

                    if (options.ListChars)
                    {
                        PrintCharacters();
                        Succeed();
                        return;
                    }

                    if (options.ListProps)
                    {
                        PrintProps(options.ListPropsFilter);
                        Succeed();
                        return;
                    }

                    if (options.ListTextures)
                    {
                        PrintTextures(options.ListTexturesFilter);
                        Succeed();
                        return;
                    }

                    if (options.ListFamilies)
                    {
                        PrintFamilies(options.ListFamiliesFilter);
                        Succeed();
                        return;
                    }

                    if (!string.IsNullOrEmpty(options.DumpProps))
                    {
                        // The dump a picker or a web viewer reads: every prop, one folder each (its own textures
                        // inside), and an index.json the page can load once instead of walking the tree.
                        string root = Path.GetFullPath(options.DumpProps);
                        Directory.CreateDirectory(root);
                        LoadDumpIndex(root);

                        var dumpList = DumpCandidates(options.DumpPropsFilter, options.DumpPropsCount);
                        if (dumpList.Count == 0)
                        {
                            Fail("no props to dump"
                                 + (string.IsNullOrEmpty(options.DumpPropsFilter)
                                     ? "; is the manifest loaded?"
                                     : $" matching '{options.DumpPropsFilter}'"));
                            return;
                        }
                        CliState.SetInt(KeyDumpTotal, dumpList.Count);

                        int dumpIndex = CliState.GetInt(KeyDumpIndex, 0);
                        if (dumpIndex >= dumpList.Count)
                        {
                            WriteDumpIndex(root, dumpList.Count);
                            int skipped = File.Exists(Path.Combine(root, DumpIndexName)) ? _dumpEntries.Count : 0;
                            Debug.Log($"{Tag} prop dump: {dumpList.Count} model(s) in {root} "
                                      + $"({_dumpEntries.Count} recorded in index.json)");
                            Succeed();
                            return;
                        }

                        var dumpEntry = dumpList[dumpIndex];
                        string dumpPath = DumpPath(root, dumpEntry, out string dumpFamily);
                        if (File.Exists(dumpPath) && new FileInfo(dumpPath).Length > 0)
                        {
                            // a resume: the file is already there, so only the index needs to know about it
                            RecordDump(root, dumpEntry, dumpFamily, dumpPath, null);
                            CliState.SetInt(KeyDumpIndex, dumpIndex + 1);
                            Debug.Log($"{Tag} [{dumpIndex + 1}/{dumpList.Count}] {Path.GetFileName(dumpEntry.Name)}: "
                                      + "already dumped, skipping");
                            return;
                        }

                        CliState.SetString(KeyOutPath, dumpPath);
                        CliState.SetInt(KeyIsProp, 1);
                        CliState.SetString(KeyPropName, dumpEntry.Name);
                        Debug.Log($"{Tag} [{dumpIndex + 1}/{dumpList.Count}] dumping {dumpEntry.Name}");
                        builder.UnloadProp();
                        builder.LoadProp(dumpEntry);
                        CurrentStage = Stage.WaitModel;
                        return;
                    }


                    if (!string.IsNullOrEmpty(options.ScanProps))
                    {
                        int index = CliState.GetInt(KeyScanIndex, 0);
                        var candidates = ScanCandidates(options.ScanProps, options.ScanCount);
                        if (candidates.Count == 0)
                        {
                            Fail($"no props match '{options.ScanProps}'");
                            return;
                        }
                        if (index >= candidates.Count)
                        {
                            int failures = CliState.GetInt(KeyScanFailures, 0);
                            if (!options.ScanExport)
                            {
                                Debug.Log($"{Tag} scanned {candidates.Count} prop(s)");
                                Succeed();
                                return;
                            }
                            Debug.Log($"{Tag} sweep: {candidates.Count - failures} of {candidates.Count} prop(s) "
                                      + $"exported, {failures} failed"
                                      + (failures > 0 ? " - the failures are named above" : ""));
                            if (failures > 0)
                            {
                                Fail($"sweep: {failures} of {candidates.Count} prop(s) did not export");
                                return;
                            }
                            Succeed();
                            return;
                        }

                        var entry = candidates[index];
                        CliState.SetInt(KeyScanIndex, index + 1);
                        builder.UnloadProp();
                        builder.LoadProp(entry);

                        if (options.ScanExport)
                        {
                            // straight on to the export stage with everything the export step reads set up,
                            // exactly as the single -umaProp run does it
                            string sweepPath = Path.Combine(SweepFolder(options),
                                                            Path.GetFileName(entry.Name) + ".pmx");
                            sweepPath = Path.GetFullPath(sweepPath);
                            Directory.CreateDirectory(Path.GetDirectoryName(sweepPath));
                            // A sweep writes every prop into ONE folder, so a second run finds the first
                            // run's files. The success check below is File.Exists, which would then report
                            // a scene that now fails to export as "exported" - at the old file's size - and
                            // the run would exit 0. A sweep exists to find exactly those scenes, so the
                            // target is cleared first and the check means what it says.
                            if (File.Exists(sweepPath)) File.Delete(sweepPath);
                            CliState.SetString(KeyOutPath, sweepPath);
                            CliState.SetInt(KeyIsProp, 1);
                            CliState.SetString(KeyPropName, entry.Name);
                            Debug.Log($"{Tag} [{index + 1}/{candidates.Count}] exporting {entry.Name}");
                            CurrentStage = Stage.WaitModel;
                            return;
                        }

                        var container = builder.CurrentOtherContainer;
                        Debug.Log($"{Tag} [{index + 1}/{candidates.Count}] {entry.Name}  {SummarizeMaterials(container)}"
                                  + $"  {SummarizeRig(container)}");
                        return;
                    }

                    if (!string.IsNullOrEmpty(options.Prop))
                    {
                        var propEntry = FindPropEntry(options.Prop);
                        if (propEntry == null)
                        {
                            Fail($"prop '{options.Prop}' not found; use -umaListProps to search");
                            return;
                        }

                        string propExportPath = string.IsNullOrEmpty(options.OutPath)
                            ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "HeadlessExports",
                                           Path.GetFileName(propEntry.Name) + ".pmx")
                            : options.OutPath;
                        propExportPath = Path.GetFullPath(propExportPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(propExportPath));
                        CliState.SetString(KeyOutPath, propExportPath);
                        CliState.SetInt(KeyIsProp, 1);
                        CliState.SetString(KeyPropName, propEntry.Name);

                        Debug.Log($"{Tag} loading prop {propEntry.Name}");
                        builder.UnloadProp();
                        builder.LoadProp(propEntry);
                        CurrentStage = Stage.WaitModel;
                        break;
                    }

                    if (options.AllCostumes || options.AllChars)
                    {
                        BatchStep(options);
                        return;
                    }

                    var chara = FindCharacter(options.CharId);
                    if (chara == null)
                    {
                        Fail($"character {options.CharId} not found");
                        return;
                    }

                    if (options.ListCostumes)
                    {
                        PrintCostumes(chara);
                        Succeed();
                        return;
                    }

                    if (!TryResolveCostume(chara, options.CostumeId, out string costumeId, out string failure))
                    {
                        Fail(failure);
                        return;
                    }

                    string exportPath = string.IsNullOrEmpty(options.OutPath)
                        ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "HeadlessExports", $"{chara.Id}_{costumeId}.pmx")
                        : options.OutPath;
                    exportPath = Path.GetFullPath(exportPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
                    CliState.SetString(KeyOutPath, exportPath);
                    CliState.SetInt(KeyIsProp, 0);

                    Debug.Log($"{Tag} loading character {chara.Id} ({chara.Name}) costume {costumeId}");
                    StartLoad(chara, costumeId);
                    CurrentStage = Stage.WaitModel;
                    break;
                }

                case Stage.WaitModel:
                {
                    var container = CurrentContainer();
                    if (container == null) return;
                    _settleFramesLeft = options.ExtraFrames;
                    CurrentStage = Stage.Settle;
                    break;
                }

                case Stage.Settle:
                    if (_settleFramesLeft-- > 0) return;

                    // attachments go on once the character is settled and before anything is measured, so a
                    // screenshot or an export of the run includes them (they are not a setting of their own)
                    if (options.Attach.Count > 0 && !_attachmentsApplied)
                    {
                        _attachmentsApplied = true;
                        if (!ApplyAttachments(options.Attach)) return;
                    }

                    if (!string.IsNullOrEmpty(options.Motion) && CliState.GetInt(KeyMotionLoaded, 0) == 0)
                    {
                        var target = CurrentContainer() as UmaContainerCharacter;
                        if (target == null)
                        {
                            Fail("-umaMotion needs a character");
                            return;
                        }
                        // `LoadAnimation` does nothing at all while the character's animator is still being built -
                        // it takes an `if (UmaAnimator)` and has no else - so applying the motion any earlier is a
                        // silent no-op and the character then plays its own idle. Wait for the animator instead;
                        // measured: without this, `-umaMotion <a dance>` logged "loading motion" and then reported
                        // the character's `idle01_loop` as current, and the take recorded the idle.
                        if (target.UmaAnimator == null)
                        {
                            return;
                        }
                        var main = UmaViewerMain.Instance;
                        if (!main.AbList.TryGetValue(options.Motion, out var motionEntry))
                        {
                            motionEntry = main.AbMotions.FirstOrDefault(e =>
                                e.Name.IndexOf(options.Motion, StringComparison.OrdinalIgnoreCase) >= 0);
                        }
                        if (motionEntry == null)
                        {
                            Fail($"motion '{options.Motion}' not found; try a substring of the asset path");
                            return;
                        }
                        Debug.Log($"{Tag} loading motion {motionEntry.Name}");
                        target.LoadAnimation(motionEntry);
                        // `LoadAnimation` puts a loop motion into the override controller's `clip_2` slot and
                        // starts the `motion_1` state, so what the animator reports as current at this instant is
                        // still the clip it was playing - the previous one. Remember the clip that was asked for
                        // and record that: measuring through `CurrentClip` recorded the character's own idle
                        // against a 76-frame dance, 30 frames of `clip_t`.
                        _requestedClip = motionEntry.Get<AnimationClip>();
                        if (_requestedClip != null) _requestedClip.name = motionEntry.Name;
                        Debug.Log($"{Tag} now playing '{CurrentClip(target.UmaAnimator)?.name}' "
                                  + $"(requested '{_requestedClip?.name}')");
                        CliState.SetInt(KeyMotionLoaded, 1);
                        _settleFramesLeft = options.ExtraFrames; // let the new clip settle before sampling
                        // batch mode runs editor updates back to back, so frames alone are no time at all:
                        // PlaySeconds is what lets a one shot animation actually reach its end
                        _playDeadline = CliHost.Now + options.PlaySeconds;
                        return;
                    }

                    if (options.PlaySeconds > 0 && CliHost.Now < _playDeadline)
                    {
                        return;
                    }

                    // the tears last: they have to be watched with the animator running, which needs wall clock
                    // time rather than frames (see the note by _playDeadline)
                    if (options.ProbeTears)
                    {
                        if (!_tearSamplingStarted)
                        {
                            _tearSamplingStarted = true;
                            _tearSampleSeconds = options.TearSampleSeconds;
                            BeginTearSampling();
                        }
                        if (CliHost.Now < _tearSampleDeadline)
                        {
                            SampleTearFrame();
                            return;
                        }
                    }

                    CurrentStage = !string.IsNullOrEmpty(options.Screenshot)
                        ? Stage.Screenshot
                        : (string.IsNullOrEmpty(options.RecordVmd) ? Stage.Export : Stage.RecordVmd);
                    break;

                case Stage.Screenshot:
                {
                    // the game's own render of the loaded character, for comparing a Blender shading setup
                    // against what the game actually looks like. Several yaws in one run give the same view
                    // under different light directions, which is what shading work is judged on.
                    string shotPath = Path.GetFullPath(options.Screenshot);
                    Directory.CreateDirectory(Path.GetDirectoryName(shotPath));
                    var container = CurrentContainer();
                    if (options.PinPose >= 0.0 && container != null) PinContainerPose(container, options.PinPose);
                    List<ShotTextureOverride> overrides = container == null
                        ? new List<ShotTextureOverride>()
                        : ApplyShotTextureOverrides(container.gameObject, options.ShotTextures,
                                                    options.ShotMaterial);
                    List<GlobalOverride> globals = ApplyShotGlobalOverrides(options.ShotGlobals);
                    List<FloatOverride> floats = container == null
                        ? new List<FloatOverride>()
                        : ApplyShotFloatOverrides(container.gameObject, options.ShotFloats,
                                                  options.ShotMaterial);
                    double[] yaws = ParseShotYaws(options.ShotYaws, options.ShotYaw);
                    // the viewer's face shading reads its light direction from the materials' _ViewDirX/_ViewDirY,
                    // so a grid steps those instead of moving a scene light
                    double[] azimuths = ParseShotYaws(options.ShotAzimuths, 0.0);
                    string baseName = Path.GetFileNameWithoutExtension(shotPath);
                    string extension = Path.GetExtension(shotPath);
                    string folder = Path.GetDirectoryName(shotPath);
                    foreach (double azimuth in azimuths)
                    {
                        // the light is applied for every shot, not only when there are several azimuths. A
                        // single azimuth is a legitimate request - "render this view with the light at 90
                        // degrees" - and skipping it silently renders the scene's own light instead, which
                        // makes a one-value-per-run light sweep look as though the light does not matter.
                        // Found by sweeping the light through six azimuths and measuring a 0.98 overlap
                        // between two opposite ones.
                        ApplyLightAzimuth(azimuth, options.ShotLightElevation);
                        foreach (double yaw in yaws)
                        {
                            bool grid = yaws.Length * azimuths.Length > 1;
                            string name = grid
                                ? $"{baseName}_az{azimuth:+0;-0;0}_yaw{yaw:+0;-0;0}{extension}"
                                : (yaws.Length == 1 ? shotPath
                                                    : Path.Combine(folder, $"{baseName}_yaw{yaw:+0;-0;0}{extension}"));
                            string path = grid ? Path.Combine(folder, name) : name;
                            options.ShotYaw = yaw;
                            string failure = CaptureGameScreenshot(options, path);
                            if (failure != null)
                            {
                                Fail(failure);
                                return;
                            }
                            Debug.Log($"{Tag} wrote {path} ({options.ShotView} view, {yaw:+0;-0;0} degrees"
                                      + (azimuths.Length > 1 ? $", light {azimuth:+0;-0;0}" : "")
                                      + (options.ShotTransparent ? ", transparent" : "") + ")");
                        }
                    }
                    RestoreShotTextureOverrides(overrides);
                    RestoreShotGlobalOverrides(globals);
                    RestoreShotFloatOverrides(floats);
                    if (options.ShotOnly)
                    {
                        Succeed();
                        return;
                    }
                    CurrentStage = string.IsNullOrEmpty(options.RecordVmd) ? Stage.Export : Stage.RecordVmd;
                    break;
                }

                case Stage.RecordVmd:
                {
                    var container = CurrentContainer();
                    if (container == null)
                    {
                        Fail("model container disappeared before recording");
                        return;
                    }
                    if (!(container is UmaContainerCharacter character) || character.UmaAnimator == null)
                    {
                        Fail("-umaRecordVmd needs a character with an animator");
                        return;
                    }

                    // The motion has to be the thing actually playing, not merely the thing asked for. Applying it
                    // in the settle stage raced the character's own load sequence, which re-applies the idle
                    // afterwards: measured, layer 0 was still playing `anm_eve_chr1001_00_idle01_loop` while a
                    // 75-frame take labelled with the dance was recorded, so the take held the idle's animation
                    // for the dance's frame count - a static hip and a constant spine. Re-apply and wait again
                    // until the requested clip is the one on the animation layer.
                    if (_requestedClip != null && _recordAttempts < 6)
                    {
                        var playing = CurrentClip(character.UmaAnimator);
                        bool rightClip = playing != null && playing.name == _requestedClip.name;
                        if (!rightClip)
                        {
                            _recordAttempts++;
                            Debug.Log($"{Tag} the animation layer is playing '{playing?.name}' but "
                                      + $"'{_requestedClip.name}' was requested - re-applying (attempt {_recordAttempts})");
                            var main = UmaViewerMain.Instance;
                            if (main.AbList.TryGetValue(_requestedClip.name, out var entry))
                            {
                                character.LoadAnimation(entry);
                            }
                            _settleFramesLeft = Mathf.Max(8, options.ExtraFrames / 2);
                            return;
                        }
                        Debug.Log($"{Tag} the animation layer is playing the requested clip '{playing.name}'");
                    }

                    var positionBone = container.transform.Find("Position");
                    if (positionBone == null)
                    {
                        Fail("character has no 'Position' bone to attach the recorder to");
                        return;
                    }

                    var recorder = positionBone.GetComponent<UnityHumanoidVMDRecorder>();
                    if (recorder == null)
                    {
                        recorder = positionBone.gameObject.AddComponent<UnityHumanoidVMDRecorder>();
                        recorder.KeyReductionLevel = Config.Instance.VmdKeyReductionLevel;
                        recorder.Initialize();
                    }

                    string vmdPath = Path.GetFullPath(options.RecordVmd);
                    Directory.CreateDirectory(Path.GetDirectoryName(vmdPath));
                    CliState.SetString(KeyVmdPath, vmdPath);

                    if (CliState.GetInt(KeyVmdStarted, 0) == 0)
                    {
                        // the motion that was asked for wins over what the state machine reports as current: the
                        // state reports the clip it is still transitioning *from* (see the -umaMotion site)
                        var clip = _requestedClip != null ? _requestedClip : CurrentClip(character.UmaAnimator);
                        Debug.Log($"{Tag} recording clip '{clip?.name}' "
                                  + $"(animator currently reports '{CurrentClip(character.UmaAnimator)?.name}')");
                        if (clip == null)
                        {
                            Fail("no animation clip is playing on the character");
                            return;
                        }
                        CliState.SetInt(KeyVmdStarted, 1);
                        Debug.Log($"{Tag} recording one loop of '{clip.name}' ({clip.length:F3}s) at {options.RecordFps}fps "
                                  + $"in {options.RecordMode} mode; the save dialog would suggest "
                                  + $"'{ExportNaming.MotionFile(character, clip)}.vmd'");

                        if (options.RecordMode == "realtime")
                        {
                            // the legacy path: let the animator play normally and let FixedUpdate sample
                            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * options.RecordFps));
                            CliState.SetInt(KeyVmdFrames, frames);
                            Time.fixedDeltaTime = 1f / options.RecordFps;
                            character.UmaAnimator.speed = 1f;
                            recorder.StartRecording();
                        }
                        else
                        {
                            recorder.StartCoroutine(recorder.RecordCurrentLoop(clip, options.RecordFps,
                                () => CliState.SetInt(KeyVmdDone, 1)));
                        }
                        return;
                    }

                    if (options.RecordMode == "realtime")
                    {
                        int wanted = CliState.GetInt(KeyVmdFrames, 30) + 1;
                        if (recorder.FrameNumber < wanted) return; // still sampling
                        recorder.StopRecording();
                        CliState.SetInt(KeyVmdDone, 1);
                    }

                    if (CliState.GetInt(KeyVmdDone, 0) == 0) return; // still recording

                    // The component defaults to 2 (its interactive default); recordings from here should be
                    // full fidelity unless asked otherwise, which is also what Config.VmdKeyReductionLevel
                    // means in the UI.
                    recorder.KeyReductionLevel = options.RecordReduction > 0
                        ? options.RecordReduction
                        : Mathf.Max(1, Config.Instance.VmdKeyReductionLevel);
                    recorder.SaveVMD(container.name, vmdPath);
                    if (!File.Exists(vmdPath) || new FileInfo(vmdPath).Length == 0)
                    {
                        Fail($"recording did not write {vmdPath}");
                        return;
                    }
                    Debug.Log($"{Tag} OK {vmdPath} ({new FileInfo(vmdPath).Length} bytes)");
                    Succeed();
                    return;
                }

                case Stage.Export:
                {
                    var container = CurrentContainer();
                    if (container == null)
                    {
                        Fail("model container disappeared before export");
                        return;
                    }
                    string exportPath = CliState.GetString(KeyOutPath, "");

                    if (!string.IsNullOrEmpty(options.Variant) && container is UmaContainerProp propContainer
                        && propContainer.TextureSet != null)
                    {
                        // drive the actual UI row when it exists, so this also covers the dropdown path
                        bool appliedViaUi = TryClickTextureSetRow(options.Variant, out string rowName);
                        bool applied = appliedViaUi || propContainer.TextureSet.SetVariant(options.Variant);
                        Debug.Log($"{Tag} texture set variant '{options.Variant}' "
                                  + (applied
                                      ? $"applied via {(appliedViaUi ? $"UI row '{rowName}'" : "API")} "
                                        + $"(current {propContainer.TextureSet.CurrentVariant})"
                                      : $"not available, current is '{propContainer.TextureSet.CurrentVariant}'"));
                    }

                    if (options.DumpFace || options.DumpMaterials)
                    {
                        string stem = CliState.GetInt(KeyIsProp, 0) == 1
                            ? SceneStem(CliState.GetString(KeyPropName, ""))
                            : "";
                        if (options.DumpFace) DumpFacePipeline(container.gameObject, stem);
                        if (options.DumpMaterials)
                        {
                            DumpMaterials(container.gameObject, stem);
                            DumpShaderProperties(container.gameObject, stem);
                        }
                    }
                    if (options.ProbeMorphs && container is UmaContainerCharacter probed)
                    {
                        DumpMorphState(probed);
                    }
                    if (options.ProbeTears && container is UmaContainerCharacter tearProbe)
                    {
                        DumpTearState(tearProbe);
                    }
                    if (options.DumpShaders)
                    {
                        DumpShaderFacts(container);
                    }

                    Debug.Log($"{Tag} exporting to {exportPath}");
                    // the exporter records what it writes, so the pmx can be compared against the game's
                    // own data vertex by vertex rather than by matching positions after the fact
                    ModelExporter.VertexDumpPath = options.DumpFace ? exportPath + ".vertices.txt" : null;
                    bool fbx = FbxExporter.IsFbxPath(exportPath);
                    Debug.Log($"{Tag} format: {(fbx ? "FBX" : "PMX")}"
                              + (fbx
                                  ? ", uma refinement " + (Config.Instance.FbxApplyUmaRefinement ? "on" : "off")
                                  : ""));
                    if (container is UmaContainerCharacter character)
                    {
                        Debug.Log($"{Tag} the save dialog would suggest "
                                  + $"'{ExportNaming.ModelFile(character)}{(fbx ? ".fbx" : ".pmx")}'");
                        ModelExporter.ExportModel(character, exportPath);
                    }
                    else
                    {
                        ModelExporter.ExportModel(container, exportPath);
                    }
                    if (!File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
                    {
                        string missing = $"the export wrote no {exportPath}";
                        if (options.ScanExport) { ContinueSweep(missing); return; }
                        if (options.AllCostumes || options.AllChars) { ContinueBatch(missing); return; }
                        if (!string.IsNullOrEmpty(options.DumpProps))
                        {
                            var dumpEntry = DumpCurrentEntry();
                            if (dumpEntry != null)
                            {
                                DumpPath(options.DumpProps, dumpEntry, out string dumpFamily);
                                ContinueDump(Path.GetFullPath(options.DumpProps), dumpEntry, dumpFamily, exportPath,
                                             container, missing);
                                return;
                            }
                        }
                        Fail(missing);
                        return;
                    }
                    Debug.Log($"{Tag} OK {exportPath} ({new FileInfo(exportPath).Length} bytes)");
                    if (options.ScanExport) { ContinueSweep(null); return; }
                    if (!string.IsNullOrEmpty(options.DumpProps))
                    {
                        var dumpEntry = DumpCurrentEntry();
                        if (dumpEntry != null)
                        {
                            DumpPath(options.DumpProps, dumpEntry, out string dumpFamily);
                            ContinueDump(Path.GetFullPath(options.DumpProps), dumpEntry, dumpFamily, exportPath,
                                         container, null);
                            return;
                        }
                    }
                    if (options.AllCostumes || options.AllChars)
                    {
                        ContinueBatch(null);
                        return;
                    }
                    Succeed();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            // A sweep exists to find the scenes that throw, so one of them throwing is a result, not the end of
            // the run: it is recorded and the next prop is loaded. Any other stage keeps the old behaviour.
            if (CurrentOptions.ScanExport && CurrentStage == Stage.Export)
            {
                ContinueSweep($"{ex.GetType().Name}: {ex.Message}");
                return;
            }            if ((CurrentOptions.AllCostumes || CurrentOptions.AllChars) && CurrentStage == Stage.Export)
            {
                ContinueBatch($"{ex.GetType().Name}: {ex.Message}");
                return;
            }
            // a dump is a sweep over 1 300 models: one that throws is recorded and skipped, like a sweep's
            if (!string.IsNullOrEmpty(CurrentOptions.DumpProps)
                && (CurrentStage == Stage.Export || CurrentStage == Stage.WaitModel || CurrentStage == Stage.Settle))
            {
                var dumpEntry = DumpCurrentEntry();
                if (dumpEntry != null)
                {
                    DumpPath(CurrentOptions.DumpProps, dumpEntry, out string dumpFamily);
                    string dumpPath = Path.Combine(Path.GetFullPath(CurrentOptions.DumpProps), "unwritten");
                    dumpPath = CliState.GetString(KeyOutPath, dumpPath);
                    ContinueDump(Path.GetFullPath(CurrentOptions.DumpProps), dumpEntry, dumpFamily, dumpPath, null,
                                 $"{ex.GetType().Name}: {ex.Message}");
                    return;
                }
            }
            Fail($"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Sets a texture set row of the materials panel to on, which is exactly what clicking the
    /// dropdown/toggle does. Returns true when a matching row was found.
    /// </summary>
    private static bool TryClickTextureSetRow(string variant, out string rowName)
    {
        rowName = null;
        var list = UmaViewerUI.Instance != null && UmaViewerUI.Instance.ModelSettings != null
            ? UmaViewerUI.Instance.ModelSettings.MaterialsList
            : null;
        if (list == null) return false;

        string expected = UISettingsModel.TextureSetRowPrefix + variant;
        foreach (Transform child in list.content)
        {
            if (child.name != expected) continue;
            var toggle = child.GetComponentInChildren<UnityEngine.UI.Toggle>(true);
            if (toggle == null) continue;
            rowName = child.name;
            toggle.isOn = true; // fires the panel's onValueChanged listener
            return true;
        }
        return false;
    }

    /// <summary>The clip currently playing on the animator (first non-empty layer).</summary>
    private static AnimationClip CurrentClip(Animator animator)
    {
        if (animator == null) return null;
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(layer);
            if (clips != null && clips.Length > 0 && clips[0].clip != null) return clips[0].clip;
        }
        return null;
    }

    /// <summary>
    /// A character entry by id, taking it from the already built list when the viewer finished its
    /// startup, and otherwise building the minimal entry from the database so a headless run does not
    /// depend on the startup download completing.
    /// </summary>
    private static CharaEntry FindCharacter(int id)
    {
        var main = UmaViewerMain.Instance;
        var known = main.Characters.FirstOrDefault(c => c.Id == id);
        if (known != null) return known;

        var row = UmaDatabaseController.Instance?.CharaData?.FirstOrDefault(item => Convert.ToInt32(item["id"]) == id);
        if (row == null) return null;
        return new CharaEntry { Id = id, Name = row["charaname"].ToString(), EnName = "" };
    }

    /// <summary>Where a -umaScanExport sweep writes its files.</summary>
    private static string SweepFolder(Options options)
    {
        if (!string.IsNullOrEmpty(options.ScanOutDir)) return Path.GetFullPath(options.ScanOutDir);
        return Path.Combine(Path.GetDirectoryName(Application.dataPath), "HeadlessExports", "scan");
    }

    /// <summary>
    /// Ends one prop of a -umaScanExport sweep and starts the next.
    ///
    /// The per-prop state has to go with it: the out path, the "is a prop" flag and the prop name are what the
    /// export stage reads, and a leftover value would make the next prop write over the last one's file, or
    /// name it wrongly in the log. The stage goes back to <see cref="Stage.WaitStartup"/>, which is where the
    /// scan list is walked.
    /// </summary>
    private static void ContinueSweep(string failure)
    {
        string name = CliState.GetString(KeyPropName, "");
        int done = CliState.GetInt(KeyScanIndex, 0);
        if (failure != null)
        {
            CliState.SetInt(KeyScanFailures, CliState.GetInt(KeyScanFailures, 0) + 1);
            Debug.Log($"{Tag} [{done}] {name}: FAILED: {failure}");
        }
        else
        {
            string written = CliState.GetString(KeyOutPath, "");
            long bytes = File.Exists(written) ? new FileInfo(written).Length : 0;
            Debug.Log($"{Tag} [{done}] {name}: exported ({bytes} bytes)");
        }

        CliState.SetString(KeyOutPath, "");
        CliState.SetInt(KeyIsProp, 0);
        CliState.SetString(KeyPropName, "");
        CurrentStage = Stage.WaitStartup;
    }

    /// <summary>Name of the file a dump describes itself with, next to the model folders.</summary>
    private const string DumpIndexName = "index.json";

    /// <summary>The three prop families, by manifest path prefix. `attach` and `sweat` are not props - see
    /// docs/ASSET_FAMILIES.md - and their `clothes` variants are parts of a prop, not props.</summary>
    private static readonly string[] PropPrefixes = { "pfb_chr_prop", "pfb_toon_prop", "pfb_rich_prop" };

    /// <summary>Every prop the manifest holds, in path order, optionally filtered and capped.</summary>
    private static List<UmaDatabaseEntry> DumpCandidates(string filter, int count)
    {
        var main = UmaViewerMain.Instance;
        if (main == null || main.AbList == null) return new List<UmaDatabaseEntry>();

        var all = main.AbList.Values
            .Where(entry => IsPropPath(entry.Name))
            // a dump is meant to be complete: a prop whose bundle is not in the local cache yet is downloaded as
            // it is loaded (Config.DownloadMissingResources), and one that cannot be is reported and skipped
            .Where(entry => string.IsNullOrEmpty(filter) || entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        return count > 0 ? all.Take(count).ToList() : all;
    }

    /// <summary>True for a manifest path that is a prop.</summary>
    private static bool IsPropPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf("clothes", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        string name = Path.GetFileName(path);
        foreach (string prefix in PropPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The `3d/chara` family of a manifest path: "prop", "toonprop", "richprop", ...</summary>
    private static string PropFamilyOf(string path)
    {
        const string root = "3d/chara/";
        if (string.IsNullOrEmpty(path) || !path.StartsWith(root, StringComparison.Ordinal)) return "other";
        string rest = path.Substring(root.Length);
        int slash = rest.IndexOf('/');
        return slash > 0 ? rest.Substring(0, slash) : rest;
    }

    /// <summary>
    /// Where a prop's model goes: `&lt;root&gt;/&lt;family&gt;/&lt;prop folder&gt;/&lt;prefab&gt;.pmx`.
    ///
    /// One folder per model, because the exporter writes its textures into `Texture2D/` beside the file: a single
    /// flat folder would have every prop's maps in one place, which is fine on disk and wrong for a web page that
    /// fetches one model at a time.
    /// </summary>
    private static string DumpPath(string root, UmaDatabaseEntry entry, out string family)
    {
        family = PropFamilyOf(entry.Name);
        string prefab = Path.GetFileName(entry.Name);
        // the prop's own folder under the family, without the family prefix and without the prefab file name:
        // "3d/chara/richprop/rich_prop1005_00/pfb_rich_prop1005_00" -> "richprop/rich_prop1005_00"
        // the path already carries the family, so the root is the only thing to add
        string folder = Path.GetDirectoryName(entry.Name.Substring("3d/chara/".Length));
        return Path.Combine(root, folder ?? family, prefab + ".pmx");
    }

    /// <summary>One model in the dump's index.json.</summary>
    [Serializable]
    private class DumpEntry
    {
        /// <summary>Prefab name, e.g. `pfb_chr_prop1003_10`. Unique within the dump.</summary>
        public string name;
        /// <summary>Family folder: prop, toonprop, richprop.</summary>
        public string family;
        /// <summary>Manifest path the model came from.</summary>
        public string source;
        /// <summary>The model, relative to the dump root and always with forward slashes.</summary>
        public string model;
        /// <summary>Its texture folder, relative to the dump root, or "" when it has no textures.</summary>
        public string textures;
        /// <summary>Size of the .pmx in bytes.</summary>
        public long bytes;
        /// <summary>Everything under its folder, textures included.</summary>
        public long bytesTotal;
        /// <summary>Bone references in its skin, 0 for a mesh with no skin.</summary>
        public int bones;
        /// <summary>Bone references that are hand or finger bones: a prop held in a hand says so here.</summary>
        public int handBones;
        /// <summary>Renderer bounds, "x,y,z" in world units - how big the thing actually is.</summary>
        public string size;
        /// <summary>Why it was not written, or "" for one that was.</summary>
        public string failure;
    }

    /// <summary>The dump as a whole: what a page loads to know every model it could show.</summary>
    [Serializable]
    private class DumpIndex
    {
        public string generated;
        public string root;
        public int count;
        public List<DumpEntry> models = new List<DumpEntry>();
    }

    /// <summary>Reads an existing index.json into memory, so a resumed run keeps what the first one recorded.</summary>
    private static void LoadDumpIndex(string root)
    {
        if (_dumpIndexLoaded) return;
        _dumpIndexLoaded = true;

        string path = Path.Combine(root, DumpIndexName);
        if (!File.Exists(path)) return;
        try
        {
            var index = JsonUtility.FromJson<DumpIndex>(File.ReadAllText(path));
            if (index?.models == null) return;
            foreach (var model in index.models)
            {
                if (model != null && !string.IsNullOrEmpty(model.model)) _dumpEntries[model.model] = model;
            }
            Debug.Log($"{Tag} resumed from an index of {_dumpEntries.Count} model(s)");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} could not read {path}, starting the index fresh: {e.Message}");
        }
    }

    /// <summary>
    /// Records one model in the index and writes index.json.
    ///
    /// It is rewritten after every model rather than at the end: a full dump is 1 300 models and twenty minutes,
    /// and a run that is stopped - by the user, by a crash, by one bad bundle - should still leave an index that
    /// describes everything on disk.
    /// </summary>
    private static void RecordDump(string root, UmaDatabaseEntry entry, string family, string modelPath,
                                   UmaContainer container, string failure = null)
    {
        string modelRelative = RelativeTo(root, modelPath);
        string textureFolder = Path.Combine(Path.GetDirectoryName(modelPath), "Texture2D");

        var record = new DumpEntry
        {
            name = Path.GetFileName(entry.Name),
            family = family,
            source = entry.Name,
            model = modelRelative,
            textures = Directory.Exists(textureFolder) ? RelativeTo(root, textureFolder) : "",
            bytes = File.Exists(modelPath) ? new FileInfo(modelPath).Length : 0,
            bytesTotal = Directory.Exists(Path.GetDirectoryName(modelPath))
                ? Directory.GetFiles(Path.GetDirectoryName(modelPath), "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length)
                : 0,
            failure = failure ?? "",
        };
        if (container != null) FillRigFacts(record, container);

        _dumpEntries[modelRelative] = record;
        WriteDumpIndex(root, CliState.GetInt(KeyDumpTotal, _dumpEntries.Count));
    }

    /// <summary>Bone count, hand bone count and bounds - the three facts a picker wants without loading the model.</summary>
    private static void FillRigFacts(DumpEntry record, UmaContainer container)
    {
        int bones = 0;
        int handBones = 0;
        foreach (var skin in container.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var bone in skin.bones)
            {
                if (!bone) continue;
                bones++;
                string name = bone.name;
                if (name.IndexOf("Wrist", StringComparison.Ordinal) >= 0
                    || name.IndexOf("Hand_Attach", StringComparison.Ordinal) >= 0
                    || name.IndexOf("Index_", StringComparison.Ordinal) >= 0
                    || name.IndexOf("Thumb_", StringComparison.Ordinal) >= 0
                    || name.IndexOf("Middle_", StringComparison.Ordinal) >= 0
                    || name.IndexOf("Ring_", StringComparison.Ordinal) >= 0
                    || name.IndexOf("Pinky_", StringComparison.Ordinal) >= 0)
                {
                    handBones++;
                }
            }
        }
        record.bones = bones;
        record.handBones = handBones;

        bool any = false;
        var bounds = new Bounds(container.transform.position, Vector3.zero);
        foreach (var renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer) continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        record.size = any
            ? FormattableString.Invariant($"{bounds.size.x:F3},{bounds.size.y:F3},{bounds.size.z:F3}")
            : "";
    }

    /// <summary>A path as the index should carry it: relative to the dump root, forward slashes.</summary>
    private static string RelativeTo(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.Replace('\\', '/');
    }

    /// <summary>Writes index.json: every model recorded so far, sorted by model path.</summary>
    private static void WriteDumpIndex(string root, int expected)
    {
        var index = new DumpIndex
        {
            generated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            root = root.Replace('\\', '/'),
            count = expected > 0 ? expected : _dumpEntries.Count,
            models = _dumpEntries.Values
                .OrderBy(model => model.model, StringComparer.Ordinal)
                .ToList(),
        };
        File.WriteAllText(Path.Combine(root, DumpIndexName), JsonUtility.ToJson(index, true));
    }

    /// <summary>
    /// Ends one model of a `-umaDumpProps` run and starts the next: records it, then goes back to the stage that
    /// walks the list. The per-prop state is cleared with it, so the next model cannot inherit the last one's path.
    /// </summary>
    private static void ContinueDump(string root, UmaDatabaseEntry entry, string family, string modelPath,
                                     UmaContainer container, string failure)
    {
        RecordDump(root, entry, family, modelPath, container, failure);

        if (failure != null)
        {
            CliState.SetInt(KeyScanFailures, CliState.GetInt(KeyScanFailures, 0) + 1);
            Debug.Log($"{Tag} {Path.GetFileName(entry.Name)}: FAILED: {failure}");
        }
        else
        {
            var record = _dumpEntries[RelativeTo(root, modelPath)];
            Debug.Log($"{Tag} {record.name}: {record.bytes} bytes, {record.bytesTotal} with textures"
                      + (record.textures == "" ? "" : $" in {record.textures}")
                      + (record.handBones > 0 ? $", {record.handBones} hand bone(s)" : ""));
        }

        CliState.SetInt(KeyDumpIndex, CliState.GetInt(KeyDumpIndex, 0) + 1);
        CliState.SetString(KeyOutPath, "");
        CliState.SetInt(KeyIsProp, 0);
        CliState.SetString(KeyPropName, "");
        CurrentStage = Stage.WaitStartup;
    }

    /// <summary>The prop being dumped by the run in progress, found again from the state the export stage reads.</summary>
    private static UmaDatabaseEntry DumpCurrentEntry()
    {
        string name = CliState.GetString(KeyPropName, "");
        if (string.IsNullOrEmpty(name)) return null;
        var main = UmaViewerMain.Instance;
        if (main == null || main.AbList == null) return null;
        return main.AbList.TryGetValue(name, out var entry) ? entry : null;
    }


    /// <summary>Props/scenes to scan, most useful ones first, capped at <paramref name="count"/>.</summary>
    private static List<UmaDatabaseEntry> ScanCandidates(string filter, int count)
    {
        return UmaViewerMain.Instance.AbList.Values
            // scenes first, but a character prop is a legitimate scan target too: "what is this family, and which
            // of it hangs off a hand" is answered by loading a batch of them, and they load through the same
            // UmaContainerProp path the UI uses
            .Where(e => e.Name.StartsWith("3d/env", StringComparison.OrdinalIgnoreCase)
                        || e.Name.StartsWith(UmaDatabaseController.CharaPath, StringComparison.Ordinal))
            .Where(e => Path.GetFileName(e.Name).StartsWith("pfb_", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(e => File.Exists(e.Path)) // do not spend the scan on assets that were never downloaded
            .OrderBy(e => e.Name)
            .Take(Mathf.Max(1, count))
            .ToList();
    }

    /// <summary>
    /// One line on how a loaded prop is rigged: skinned to a hand, to a whole body, or not at all.
    ///
    /// This is how "which of these are held props" gets answered without opening sixty bundles by hand: a
    /// hand-held prop's skin names wrist/finger bones, a scene prop has none (or a single root), and a prop that
    /// merely reuses the uma armature names whole-body bones while sitting nowhere near a hand.
    /// </summary>
    private static string SummarizeRig(UmaContainer container)
    {
        if (container == null) return "!! no container";

        var skins = container.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var meshes = container.GetComponentsInChildren<MeshRenderer>(true);
        var handBones = new List<string>();
        var otherBones = new List<string>();
        int bones = 0;
        foreach (var skin in skins)
        {
            foreach (var bone in skin.bones)
            {
                if (!bone) continue;
                bones++;
                bool hand = bone.name.IndexOf("Wrist", StringComparison.Ordinal) >= 0
                            || bone.name.IndexOf("Hand_Attach", StringComparison.Ordinal) >= 0
                            || bone.name.IndexOf("Index_", StringComparison.Ordinal) >= 0
                            || bone.name.IndexOf("Thumb_", StringComparison.Ordinal) >= 0
                            || bone.name.IndexOf("Middle_", StringComparison.Ordinal) >= 0
                            || bone.name.IndexOf("Ring_", StringComparison.Ordinal) >= 0
                            || bone.name.IndexOf("Pinky_", StringComparison.Ordinal) >= 0;
                var list = hand ? handBones : otherBones;
                if (!list.Contains(bone.name)) list.Add(bone.name);
            }
        }

        Bounds? union = null;
        foreach (var renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer) continue;
            if (!union.HasValue) { union = renderer.bounds; continue; }
            var grown = union.Value;
            grown.Encapsulate(renderer.bounds);
            union = grown;
        }
        string size = union.HasValue
            ? $"({union.Value.size.x:F2},{union.Value.size.y:F2},{union.Value.size.z:F2})"
            : "(-)";

        return $"skinned {skins.Length} of {skins.Length + meshes.Length} renderer(s), {bones} bone ref(s), "
               + (handBones.Count > 0
                   ? $"HAND bones {handBones.Count}: {string.Join(",", handBones.Take(RigNamesShown))}"
                   : "no hand bones")
               + (otherBones.Count > 0
                   ? $" | other {otherBones.Count}: {string.Join(",", otherBones.Take(RigNamesShown))}"
                   : "")
               + $", size {size}";
    }

    /// <summary>
    /// How many of a prop's bone names the rig summary spells out. A prop can name fifty bones, and
    /// the names are the only description the game gives it, so the sweep needs more than the first
    /// handful - but a whole body skin's worth of names on one line buries the eight that say where
    /// it mounts and what it is.
    /// </summary>
    private const int RigNamesShown = 8;

    /// <summary>
    /// Wears the `-umaAttach` prefabs on the loaded character, before anything is measured.
    ///
    /// Returns false (having failed the run) when a pattern matches nothing or no character is loaded, because a
    /// measurement of an unattached character that was asked to be dressed is worse than no measurement.
    /// </summary>
    private static bool ApplyAttachments(List<string> patterns)
    {
        var builder = UmaViewerBuilder.Instance;
        var character = builder?.CurrentUMAContainer;
        if (character == null)
        {
            Fail("-umaAttach needs a character (nothing to attach to)");
            return false;
        }

        var candidates = UmaViewerMain.Instance.AbChara
            .Where(e => e.Name.StartsWith("3d/chara/attach/", StringComparison.Ordinal))
            .ToList();

        foreach (string pattern in patterns)
        {
            var matches = candidates
                .Where(e => e.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToList();
            if (matches.Count == 0)
            {
                Fail($"no attach prefab matches '{pattern}'; -umaListFamilies attach prints the family");
                return false;
            }            if (matches.Count > 1)
                Debug.Log($"{Tag} '{pattern}' matches {matches.Count} attach prefabs, wearing all of them: "
                          + string.Join(", ", matches.Select(m => Path.GetFileName(m.Name))));

            foreach (var match in matches)
            {
                var worn = builder.LoadAttach(match);
                if (worn == null)
                {
                    Fail($"could not wear {match.Name}");
                    return false;
                }
            }
        }

        var wornNames = character.Accessories.Select(a => a.name).ToList();
        Debug.Log($"{Tag} wearing {wornNames.Count} attachment(s) on {character.name}: {string.Join(", ", wornNames)}");
        return true;
    }


    private static string SummarizeMaterials(UmaContainer container)
    {
        if (container == null) return "!! no container";
        var renderers = container.GetComponentsInChildren<Renderer>(true);
        int slots = 0, textureless = 0, noMainTexProperty = 0, missingShader = 0;
        var shaders = new HashSet<string>();
        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                slots++;
                if (material == null) { textureless++; continue; }
                string shaderName = material.shader != null ? material.shader.name : "<none>";
                shaders.Add(shaderName);
                if (material.shader == null || shaderName == "Hidden/InternalErrorShader") { missingShader++; continue; }
                if (!material.HasProperty("_MainTex")) { noMainTexProperty++; continue; }
                if (material.GetTexture("_MainTex") == null) textureless++;
            }
        }

        var textureSet = (container as UmaContainerProp)?.TextureSet;
        string resolved = textureSet != null && textureSet.Variants.Count > 0
            ? $"textureSet={textureSet.CurrentVariant} of [{string.Join(",", textureSet.Variants)}] fixed={textureSet.AppliedSlots}"
            : "textureSet=none";
        return $"renderers={renderers.Length} slots={slots} textureless={textureless} "
               + $"noMainTexProp={noMainTexProperty} missingShader={missingShader} shaders={shaders.Count} {resolved}";
    }

    /// <summary>
    /// Points the character's materials at a light azimuth, for the grid: the Nars face shader shades with its
    /// view-rotated light direction, and _ViewDirX/_ViewDirY are what rotate it. Half a turn either way covers
    /// the face from both sides.
    /// </summary>
    private static Quaternion s_lightRestRotation;
    private static bool s_lightRestCaptured;
    private static double s_lightElevation = -1.0;
    private static float s_lightRestYaw = 0f;

    private static void ApplyLightAzimuth(double azimuth, double elevation)
    {
        s_lightElevation = elevation;
        // The scene's directional light is what the character's shading follows, so that is what turns. The
        // first call remembers where it started, and every azimuth is measured from there - so azimuth 0 gives
        // back exactly the viewer's own default lighting.
        int turned = 0;
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            if (!s_lightRestCaptured)
            {
                s_lightRestRotation = light.transform.rotation;
                // the scene's own azimuth is the zero: it is the direction the viewer lights the character from,
                // and rebuilding the direction without it put the sun behind the model
                s_lightRestYaw = light.transform.eulerAngles.y;
                s_lightRestCaptured = true;
            }
            light.transform.rotation = s_lightElevation >= 0.0
                ? Quaternion.Euler((float)s_lightElevation, s_lightRestYaw + (float)azimuth, 0f)
                : Quaternion.Euler(0f, (float)azimuth, 0f) * s_lightRestRotation;
            turned++;
            Debug.Log($"{Tag} turned the directional light '{light.name}' to azimuth {azimuth:+0;-0;0} degrees "
                      + $"(elevation {light.transform.eulerAngles.x:F0}, at {light.transform.eulerAngles.y:F0})"
                      // the vector itself, so the Blender tool can be given the same light: Unity's directional
                      // light travels along its own forward
                      + $", travelling along ({light.transform.forward.x:F4}, "
                      + $"{light.transform.forward.y:F4}, {light.transform.forward.z:F4})"
                      // the colour and intensity too: the game's pixel program multiplies the diffuse by a
                      // global that carries the light colour, so a warm scene light tints the face warm
                      + $", colour ({light.color.r:F3}, {light.color.g:F3}, {light.color.b:F3}) "
                      + $"intensity {light.intensity:F3}");
        }

        // and the Nars face shader's own control, for a model that does use it
        int applied = 0;
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (Material material in renderer.materials)
            {
                if (material == null || !material.HasProperty("_ViewDirX")) continue;
                material.SetFloat("_ViewDirX", (float)azimuth);
                applied++;
            }
        }
        if (turned == 0)
            Debug.LogWarning($"{Tag} no directional light in the scene to turn; _ViewDirX set on {applied} "
                             + "material(s) instead");
        else if (applied > 0)
            Debug.Log($"{Tag} and _ViewDirX on {applied} material(s)");
    }

    /// <summary>
    /// The camera angles to shoot: the comma separated list when one was given, otherwise the single -umaShotYaw
    /// value. One angle writes the file as named, several write "&lt;name&gt;_yaw+45.png" and so on.
    /// </summary>
    private static double[] ParseShotYaws(string list, double single)
    {
        if (string.IsNullOrWhiteSpace(list)) return new[] { single };
        var parsed = new List<double>();
        foreach (string part in list.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                parsed.Add(value);
            else
                Debug.Log($"{Tag} ignoring unreadable camera angle '{trimmed}'");
        }
        return parsed.Count > 0 ? parsed.ToArray() : new[] { single };
    }

    /// <summary>
    /// Renders the game's own view of the loaded character to a PNG, so a Blender shading setup can be
    /// compared against what the game actually looks like. The camera is moved for the shot - the game
    /// keeps its post processing, so this is the tone mapped result, not a raw buffer - and put back
    /// afterwards, which leaves the export path untouched.
    ///
    /// Returns null on success, or a message describing what was missing.
    /// </summary>
    private static string CaptureGameScreenshot(Options options, string path)
    {
        var builder = UmaViewerBuilder.Instance;
        var container = CurrentContainer();
        if (container == null) return "no model loaded to screenshot";

        var animationCamera = builder != null ? builder.AnimationCamera : null;
        Camera camera = animationCamera != null && animationCamera.isActiveAndEnabled ? animationCamera : Camera.main;
        if (camera == null) return "the viewer scene has no active camera";

        Bounds bounds = new Bounds(container.transform.position, Vector3.zero);
        bool any = false;
        foreach (var renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!any) return "the loaded model has no renderers";

        var character = container as UmaContainerCharacter;
        Transform head = character != null && character.HeadBone != null ? character.HeadBone.transform : null;
        // the game's bone axes point where the character looks, so the head's own forward is the way the face
        // is turned - the same axis the exporter uses to aim the eye bones
        Vector3 facing = head != null ? head.forward : container.transform.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        facing.Normalize();

        float height = Mathf.Max(bounds.size.y, 0.01f);
        Vector3 headPoint = head != null
            ? head.position
            : new Vector3(bounds.center.x, bounds.max.y - height * 0.08f, bounds.center.z);
        Vector3 target;
        float distance;
        switch (options.ShotView)
        {
            case "face":
                target = headPoint;
                distance = height * 0.15f;      // the head nearly fills the frame, for comparing shading
                break;
            case "head":
                target = headPoint;
                distance = height * 0.28f;
                break;
            case "upper":
                target = new Vector3(headPoint.x, bounds.min.y + height * 0.82f, headPoint.z);
                distance = height * 0.8f;
                break;
            default:
                target = bounds.center;
                distance = height * 1.9f;
                break;
        }
        Debug.Log($"{Tag} screenshot view '{options.ShotView}': bounds {bounds.size} centre {bounds.center} "
                  + $"head {headPoint} facing {facing} target {target} distance {distance:F3}");

        Vector3 direction = Quaternion.AngleAxis((float)options.ShotYaw, Vector3.up) * facing;
        Transform camTransform = camera.transform;
        Vector3 oldPosition = camTransform.position;
        Quaternion oldRotation = camTransform.rotation;
        float oldFov = camera.fieldOfView;
        try
        {
            camTransform.position = target + direction * distance;
            camTransform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
            camera.fieldOfView = 39.6f;     // what a 50 mm lens on a 36 mm sensor sees, like the Blender tool
            camera.ResetProjectionMatrix();

            var image = Screenshot.GrabFrame(camera, options.ShotWidth, options.ShotHeight,
                                             options.ShotTransparent);
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(image));
            UnityEngine.Object.Destroy(image);
        }
        finally
        {
            camTransform.position = oldPosition;
            camTransform.rotation = oldRotation;
            camera.fieldOfView = oldFov;
            camera.ResetProjectionMatrix();
        }
        return null;
    }

    /// <summary>The container the current run is operating on (character or prop/scene).</summary>
    private static UmaContainer CurrentContainer()
    {
        var builder = UmaViewerBuilder.Instance;
        if (builder == null) return null;
        return CliState.GetInt(KeyIsProp, 0) == 1 ? (UmaContainer)builder.CurrentOtherContainer : builder.CurrentUMAContainer;
    }

    /// <summary>
    /// Finds a prop/scene entry by exact asset path, or by a case insensitive substring of the path
    /// or of the file name.
    /// </summary>
    private static UmaDatabaseEntry FindPropEntry(string query)
    {
        var candidates = UmaViewerMain.Instance.AbList.Values.ToList();

        var exact = candidates.FirstOrDefault(e => string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var matches = candidates.Where(e => e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (matches.Count > 1)
        {
            Debug.LogWarning($"{Tag} '{query}' is ambiguous ({matches.Count} matches), using {matches[0].Name}; "
                             + $"others: {string.Join(", ", matches.Skip(1).Take(8).Select(m => m.Name))}");
        }
        return matches.FirstOrDefault();
    }

    /// <summary>
    /// Every texture asset in the character database whose name matches, filtered by name.
    ///
    /// This is how a variant set is *found* rather than assumed: the rain look exists because the game
    /// ships a "..._wet" twin of every dry map, and that was established by reading names like this. Asking
    /// for one character's textures and looking at the suffixes answers whether there is any other set (a
    /// dirty one, a seasonal one) without guessing from a single prefab's material.
    /// </summary>
    private static void PrintTextures(string filter)
    {
        var textures = UmaViewerMain.Instance.AbChara
            .Where(e => e != null && e.Name.Contains("/textures/"))
            .Where(e => string.IsNullOrEmpty(filter) || e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
        Debug.Log($"{Tag} character textures ({textures.Count} matching '{filter}'):");
        foreach (var entry in textures.Take(4000)) Debug.Log($"{Tag}   {entry.Name}");
        if (textures.Count > 4000) Debug.Log($"{Tag}   ... and {textures.Count - 4000} more");
    }

    private static void PrintProps(string filter)
    {
        var props = UmaViewerMain.Instance.AbList.Values
            .Where(e => e.Name.StartsWith("3d/env") && Path.GetFileName(e.Name).StartsWith("pfb_")
                        || (e.Name.StartsWith("cutt/cutt_son") && Path.GetFileName(e.Name).StartsWith("cutt_son")))
            .Where(e => string.IsNullOrEmpty(filter) || e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(e => e.Name)
            .ToList();
        Debug.Log($"{Tag} props/scenes ({props.Count} matching '{filter}'):");
        foreach (var entry in props.Take(2000)) Debug.Log($"{Tag}   {entry.Name}");
        if (props.Count > 2000) Debug.Log($"{Tag}   ... and {props.Count - 2000} more");
    }

    /// <summary>
    /// The families under `3d/chara/`, with what each one holds.
    ///
    /// The viewer asks for four of them by name (body, head, tail, mini) and touches `common` and `prop`
    /// through narrower paths; `attach`, `richprop`, `sweat` and `toonprop` are loaded into <see
    /// cref="UmaViewerMain.AbChara"/> at startup and then never asked for. Whether that is a missing
    /// feature or four families of non-character assets is a question about **names**, so this answers it
    /// from names alone: `AbChara` comes from the manifest, so nothing here opens a bundle (and
    /// `Config.DownloadMissingResources` cannot fire).
    ///
    /// The `names:` column masks digit runs (`pfb_chr_prop####_##`) so ten thousand entries collapse to
    /// the handful of forms a family is actually built from - that is what shows whether the prop panel's
    /// literal `pfb_chr_prop` filter (UmaViewerUI.cs:555) can see a family. The `panel` column then counts
    /// what that filter would offer today, so "does widening it help" is measurable rather than assumed.
    /// </summary>
    private static void PrintFamilies(string filter)
    {
        var main = UmaViewerMain.Instance;
        if (main?.AbChara == null)
        {
            Fail("no character database (did the viewer finish booting?)");
            return;
        }

        // the same prefix UmaViewerMain.LoadDatabase filters on, so "family" here means the same thing it
        // means there: everything under 3d/chara/
        string charaPath = UmaDatabaseController.CharaPath;
        var byFamily = new Dictionary<string, List<UmaDatabaseEntry>>(StringComparer.Ordinal);
        foreach (var entry in main.AbChara)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
            if (!entry.Name.StartsWith(charaPath, StringComparison.Ordinal)) continue;

            string rest = entry.Name.Substring(charaPath.Length);
            int slash = rest.IndexOf('/');
            string family = slash > 0 ? rest.Substring(0, slash) : rest;
            if (!byFamily.TryGetValue(family, out var list)) byFamily[family] = list = new List<UmaDatabaseEntry>();
            list.Add(entry);
        }

        var families = byFamily.Keys
            .Where(f => string.IsNullOrEmpty(filter) || f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Debug.Log($"{Tag} 3d/chara families ({families.Count} of {byFamily.Count} matching '{filter}', "
                  + $"{main.AbChara.Count} entries under '{charaPath}'):");
        Debug.Log($"{Tag}   family        entries prefabs  panel  prefab names");
        foreach (string family in families)
        {
            var entries = byFamily[family];
            int prefabs = entries.Count(e => Path.GetFileName(e.Name).StartsWith("pfb_", StringComparison.OrdinalIgnoreCase));
            // exactly UmaViewerUI.cs's prop-panel predicate, so the column is what the panel shows
            int panel = entries.Count(e => e.Name.Contains("pfb_chr_prop") && !e.Name.Contains("clothes"));
            List<string> stems = entries
                .Select(e => MaskDigits(Path.GetFileName(e.Name)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            string stemText = string.Join(" ", stems.Take(6)) + (stems.Count > 6 ? $" (+{stems.Count - 6})" : "");
            Debug.Log($"{Tag}   {family,-12} {entries.Count,7} {prefabs,7} {panel,6}  {stemText}");

            // three samples per family, sorted so the same run prints the same three
            foreach (string sample in entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).Take(3))
                Debug.Log($"{Tag}     e.g. {sample}");

            // When the filter names a single family the question is about *that* family, and the useful next
            // question is which ids it covers: a family's folder carries the character id and costume
            // (attach1048_00, bdy1001_00), which is what a "these belong on this character" panel pairs
            // against. Printed only for a single-family query, so a plain -umaListFamilies stays a summary.
            if (families.Count == 1)
                PrintFamilyFolders(entries, charaPath, family);
        }
    }

    /// <summary>The distinct `&lt;family&gt;/&lt;folder&gt;/` names a family is grouped into, with entry counts.</summary>
    private static void PrintFamilyFolders(List<UmaDatabaseEntry> entries, string charaPath, string family)
    {
        var folders = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            // 3d/chara/<family>/<folder>/<asset> - the folder is what carries the id and the costume
            string rest = entry.Name.Substring(charaPath.Length + family.Length).TrimStart('/');
            int slash = rest.IndexOf('/');
            string folder = slash > 0 ? rest.Substring(0, slash) : "";
            if (folder.Length == 0) continue;
            folders.TryGetValue(folder, out int count);
            folders[folder] = count + 1;
        }

        Debug.Log($"{Tag}   {family} folders ({folders.Count} distinct, folder x entries):");
        foreach (var pair in folders.OrderBy(p => p.Key, StringComparer.Ordinal).Take(80))
            Debug.Log($"{Tag}     {pair.Key,-28} {pair.Value,4}");
        if (folders.Count > 80) Debug.Log($"{Tag}     ... and {folders.Count - 80} more");
    }

    /// <summary>Collapses a digit run to one `#`, so a family's naming form shows through its ids.</summary>
    private static string MaskDigits(string name)
    {
        var masked = new System.Text.StringBuilder(name.Length);
        bool inDigits = false;
        foreach (char c in name)
        {
            bool digit = c >= '0' && c <= '9';
            if (digit)
            {
                if (!inDigits) masked.Append('#');
            }
            else masked.Append(c);
            inDigits = digit;
        }
        return masked.ToString();
    }

    /// <summary>
    /// Every property of every shader the model uses, with the value it has on the first material seen.
    /// This is how the parameters worth carrying into an export were identified (the ones the uma shader
    /// actually drives), and it is how a game update that renames them will be noticed.
    /// </summary>
    private static void DumpShaderProperties(GameObject root, string stem)
    {
        var seen = new HashSet<string>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                var shader = material.shader;
                if (!seen.Add(shader.name)) continue;

                Debug.Log($"{Tag} SHADER '{shader.name}' ({shader.GetPropertyCount()} properties), "
                          + $"values from material '{material.name}'");
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string name = shader.GetPropertyName(i);
                    var type = shader.GetPropertyType(i);
                    string value;
                    switch (type)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            value = material.GetFloat(name).ToString("F4");
                            break;
                        case ShaderPropertyType.Color:
                            var colour = material.GetColor(name);
                            value = $"({colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3})";
                            break;
                        case ShaderPropertyType.Vector:
                            var vector = material.GetVector(name);
                            value = $"({vector.x:F3},{vector.y:F3},{vector.z:F3},{vector.w:F3})";
                            break;
                        case ShaderPropertyType.Texture:
                            var texture = material.GetTexture(name);
                            value = texture != null ? texture.name : "<none>";
                            break;
                        default:
                            value = material.GetInt(name).ToString();
                            break;
                    }
                    Debug.Log($"{Tag}   SHADERPROP {name} | {type} | {value}");
                }
            }
        }
    }

    /// <summary>
    /// Reports what the loaded model's materials actually resolved to. Props and scenes get no
    /// material post-processing at all in UmaContainerProp, so materials the game assigns at runtime
    /// (weather/banner texture sets) stay empty and render flat white.
    /// </summary>
    /// <summary>
    /// Freezes every animator under the container at a normalised time. Two shots of the same model are only
    /// comparable if the pose matches, and this viewer's model animates: a baseline against a perturbed
    /// render otherwise shows the pose moving as well as whatever was perturbed.
    /// </summary>
    private static void PinContainerPose(UmaContainer container, double normalisedTime)
    {
        int pinned = 0;
        foreach (var animator in container.GetComponentsInChildren<Animator>(true))
        {
            if (animator.runtimeAnimatorController == null) continue;
            animator.speed = 0f;
            animator.Play(0, 0, (float)normalisedTime);
            animator.Update(0f);
            pinned++;
        }
        Debug.Log($"{Tag} -umaPinPose: froze {pinned} animator(s) at normalised time {normalisedTime:g}");
    }

    private class FloatOverride
    {
        public Material Material;
        public string Property;
        public float Original;
    }

    /// <summary>
    /// Sets named material floats for the duration of a shot. `_faceShadowAlpha` is the reason this exists:
    /// it is a *driven* property of the game's facial driven-key system (`Gallop/FaceDrivenKeyTarget` binds it
    /// to a `Shade_Ctrl` morph), so its value in the material is only its rest value of 0, and the cheek and
    /// nose regions are inert. Forcing it is how to see what those regions do.
    /// </summary>
    private static List<FloatOverride> ApplyShotFloatOverrides(GameObject root, List<string> specs,
                                                              string only = "")
    {
        var applied = new List<FloatOverride>();
        if (specs == null || specs.Count == 0) return applied;

        foreach (string raw in specs)
        {
            string spec = raw.Trim().Trim('\'', '"');
            int equals = spec.IndexOf('=');
            if (equals < 0)
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotFloat '{spec}': expected Property=value");
                continue;
            }
            string property = spec.Substring(0, equals).Trim().Trim('\'', '"');
            string value = spec.Substring(equals + 1).Trim().Trim('\'', '"');
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float number))
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotFloat '{spec}': bad number");
                continue;
            }

            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.materials)
                {
                    if (material == null || !material.HasProperty(property)) continue;
                    if (only.Length > 0 && !material.name.ToLowerInvariant().Contains(only.ToLowerInvariant()))
                        continue;
                    applied.Add(new FloatOverride
                    {
                        Material = material,
                        Property = property,
                        Original = material.GetFloat(property)
                    });
                    material.SetFloat(property, number);
                    count++;
                }
            }
            Debug.Log($"{Tag} -umaShotFloat: '{property}' set to {number} on {count} material slot(s)");
        }
        return applied;
    }

    private static void RestoreShotFloatOverrides(List<FloatOverride> applied)
    {
        foreach (var entry in applied)
        {
            if (entry.Material != null) entry.Material.SetFloat(entry.Property, entry.Original);
        }
        applied.Clear();
    }

    private class GlobalOverride
    {
        public string Name;
        public bool IsColour;
        public Color Colour;
        public float Value;
    }

    /// <summary>
    /// Sets named shader globals for the duration of a shot. The face program ends by blending its colour
    /// toward a global by a per-vertex factor, and that global lives in the viewer's own buffer
    /// (UmaViewerGlobalShader), so overriding it is how to tell whether the viewer's render is fogged or
    /// lightmapped in a way the game's material knows nothing about.
    /// </summary>
    private static List<GlobalOverride> ApplyShotGlobalOverrides(List<string> specs)
    {
        var applied = new List<GlobalOverride>();
        if (specs == null || specs.Count == 0) return applied;

        foreach (string raw in specs)
        {
            string spec = raw.Trim().Trim('\'', '"');
            int equals = spec.IndexOf('=');
            if (equals < 0)
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotGlobal '{spec}': expected Name=value");
                continue;
            }
            string name = spec.Substring(0, equals).Trim();
            string value = spec.Substring(equals + 1).Trim().Trim('\'', '"');
            string[] parts = value.Split(',');

            if (parts.Length >= 2)
            {
                var components = new float[4] { 0f, 0f, 0f, 1f };
                bool parsed = true;
                for (int i = 0; parsed && i < parts.Length && i < 4; i++)
                {
                    parsed = float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out components[i]);
                }
                if (!parsed)
                {
                    Debug.LogWarning($"{Tag} ignoring -umaShotGlobal '{spec}': bad colour");
                    continue;
                }
                var colour = new Color(components[0], components[1], components[2], components[3]);
                Color previous = Shader.GetGlobalColor(name);
                applied.Add(new GlobalOverride
                {
                    Name = name, IsColour = true, Colour = previous, Value = 0f
                });
                Shader.SetGlobalColor(name, colour);
                Debug.Log($"{Tag} -umaShotGlobal: '{name}' colour set to "
                          + $"{colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3}"
                          + $" (was {previous.r:F3},{previous.g:F3},{previous.b:F3},{previous.a:F3})");
            }
            else
            {
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float number))
                {
                    Debug.LogWarning($"{Tag} ignoring -umaShotGlobal '{spec}': bad number");
                    continue;
                }
                float original = Shader.GetGlobalFloat(name);
                applied.Add(new GlobalOverride { Name = name, IsColour = false, Value = original });
                Shader.SetGlobalFloat(name, number);
                Debug.Log($"{Tag} -umaShotGlobal: '{name}' float set to {number} (was {original})");
            }
        }
        return applied;
    }

    private static void RestoreShotGlobalOverrides(List<GlobalOverride> applied)
    {
        foreach (var entry in applied)
        {
            if (entry.IsColour) Shader.SetGlobalColor(entry.Name, entry.Colour);
            else Shader.SetGlobalFloat(entry.Name, entry.Value);
        }
        applied.Clear();
    }

    private class ShotTextureOverride
    {
        public Material Material;
        public string Property;
        public Texture Original;
    }

    /// <summary>
    /// Replaces named texture properties with a solid colour, so a term of the game's compiled shader can be
    /// asked what it reads: hand the term a colour it could not otherwise produce and see whether it turns up
    /// in the render. This is the only way to name a texture register - the shipped containers have no RDEF
    /// chunk, and the per-container records in the blob region are not in layout order.
    /// </summary>
    private static List<ShotTextureOverride> ApplyShotTextureOverrides(GameObject root, List<string> specs,
                                                                     string only = "")
    {
        var applied = new List<ShotTextureOverride>();
        if (specs == null || specs.Count == 0) return applied;

        foreach (string raw in specs)
        {
            // quoted forms turn up when a caller passes an array through a shell, so strip them rather
            // than throw on them
            string spec = raw.Trim().Trim('\'', '"');
            int equals = spec.IndexOf('=');
            if (equals < 0)
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotTexture '{spec}': expected Property=r,g,b or Property=null");
                continue;
            }
            string property = spec.Substring(0, equals).Trim().Trim('\'', '"');
            string value = spec.Substring(equals + 1).Trim().Trim('\'', '"');

            Texture replacement = null;
            if (!value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(',');
                var components = new float[3];
                bool parsed = parts.Length >= 3;
                for (int i = 0; parsed && i < 3; i++)
                {
                    parsed = float.TryParse(parts[i].Trim().Trim('\'', '"'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out components[i]);
                }
                if (!parsed)
                {
                    Debug.LogWarning($"{Tag} ignoring -umaShotTexture '{spec}': expected three numbers, "
                                     + $"got '{value}'");
                    continue;
                }
                var colour = new Color(components[0], components[1], components[2], 1f);
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = colour;
                texture.SetPixels(pixels);
                texture.Apply();
                replacement = texture;
            }

            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.materials)
                {
                    if (material == null || !material.HasProperty(property)) continue;
                    if (only.Length > 0 && !material.name.ToLowerInvariant().Contains(only.ToLowerInvariant()))
                        continue;
                    applied.Add(new ShotTextureOverride
                    {
                        Material = material,
                        Property = property,
                        Original = material.GetTexture(property)
                    });
                    material.SetTexture(property, replacement);
                    count++;
                }
            }
            Debug.Log($"{Tag} -umaShotTexture: '{property}' set to "
                      + (replacement == null ? "<null>" : value) + $" on {count} material slot(s)");
        }
        return applied;
    }

    private static void RestoreShotTextureOverrides(List<ShotTextureOverride> applied)
    {
        foreach (var entry in applied)
        {
            if (entry.Material != null) entry.Material.SetTexture(entry.Property, entry.Original);
        }
        applied.Clear();
    }

    /// <summary>
    /// Everything after the meta database: the material the game's shader actually receives, the textures bound to
    /// it, and the mesh facts the shader depends on. The stored bundle material is not necessarily the runtime one -
    /// the viewer overwrites textures and properties when it loads a character - and the face shader's
    /// _NormalizeNormal is 0, so the length of its vertex normals matters.
    /// </summary>
    /// <summary>Whether the face pipeline has already been dumped this run; the editor probes share it.</summary>
    public static bool FaceDumped;

    /// <summary>
    /// What the facial morph system holds at runtime, for a character that was set up but not exported.
    ///
    /// The extra morphs are built in code from the character's own parts (FaceDrivenKeyTarget.InitializeExtra),
    /// so a morph being absent means one of those parts was missing when the target initialised. This prints the
    /// parts themselves as well, so the report says *why* rather than only what.
    /// </summary>
    /// <summary>Watchers for -ProbeTears: the tears are sampled over wall clock time, not frames.</summary>
    private static bool _tearSamplingStarted;
    private static double _tearSampleDeadline;
    private static int _tearSampleCount;

    /// <summary>
    /// Switches the tears on the way the UI does - every `Tear*_Ctrl` weight to 1 - and marks the animation as
    /// running. Note the objects only animate once they are active, and the animator then needs real time.
    /// </summary>
    private static void BeginTearSampling()
    {
        var character = CurrentContainer() as UmaContainerCharacter;
        _tearSampleCount = 0;
        _tearSampleDeadline = CliHost.Now + Math.Max(0.1, _tearSampleSeconds);
        if (character == null || character.FaceDrivenKeyTarget == null)
        {
            Debug.Log($"{Tag} [tears] no facial target, nothing to sample");
            return;
        }
        int driven = 0;
        foreach (FacialOtherMorph morph in character.FaceDrivenKeyTarget.OtherMorphs)
        {
            if (morph == null || !morph.name.StartsWith("Tear")) continue;
            foreach (BindProperty property in morph.BindProperties)
            {
                if (property == null || property.Type != BindProperty.BindType.TearWeight) continue;
                character.FaceDrivenKeyTarget.ChangeMorphWeight(property, 1f, morph, false);
                driven++;
            }
        }
        Debug.Log($"{Tag} [tears] sampling '{character.name}' for {_tearSampleSeconds:F1}s, {driven} weight(s) raised");

        // and sweep the clip from end to end, because wall clock is far too slow in batch mode to walk 4.267s of
        // animation (two seconds of it advance the state by about 1%)
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            GameObject part = controller.CurrenObject < 1 ? controller.TearObject_0 : controller.TearObject_1;
            if (part == null) continue;
            SweepTearClip($"[tears]   sweep ctrl{index}", part);
        }
    }

    /// <summary>
    /// Steps the animator through the clip by normalised time and reports the posed extent each step. This is the
    /// routine an export would use to bake the roll into morphs, so it is the same code path either way.
    /// </summary>
    private static void SweepTearClip(string label, GameObject part)
    {
        Animator animator = part.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.Log($"{Tag} {label} no animator to sweep");
            return;
        }
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;
        animator.Rebind();
        animator.speed = 0f; // Play() sets the time; speed only decides whether it drifts between calls
        int movedSteps = 0;
        Vector3 first = Vector3.zero;
        for (int step = 0; step <= 10; step++)
        {
            float time = step / 10f;
            animator.Play(0, 0, time);
            animator.Update(0.0001f);
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked, false);
                    Vector3[] vertices = baked.vertices;
                    if (vertices.Length == 0) continue;
                    Vector3 min = vertices[0], max = vertices[0];
                    foreach (Vector3 vertex in vertices)
                    {
                        min = Vector3.Min(min, vertex);
                        max = Vector3.Max(max, vertex);
                    }
                    Vector3 size = max - min;
                    if (step == 0) first = size;
                    else if ((size - first).sqrMagnitude > 1e-8f) movedSteps++;
                    Debug.Log($"{Tag} {label} t={time:F1} size={VectorText(size)} min={VectorText(min)} max={VectorText(max)}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
        }
        animator.speed = 1f;
        Debug.Log($"{Tag} {label} steps that differ from t=0: {movedSteps} of 10");
    }

    private static double _tearSampleSeconds = 2.0;

    /// <summary>One sample per editor frame: where the animator is, and the posed extent of every active tear.</summary>
    private static void SampleTearFrame()
    {
        var character = CurrentContainer() as UmaContainerCharacter;
        if (character == null || character.TearControllers == null) return;
        _tearSampleCount++;
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            // SetWegiht shows CurrenObject and hides the other one, so that is the part that is really on screen
            GameObject part = controller.CurrenObject < 1 ? controller.TearObject_0 : controller.TearObject_1;
            if (part == null) continue;
            Animator animator = part.GetComponent<Animator>();
            // An animator in the default culling mode does not write its bone transforms when the renderer is not
            // being drawn, and batch mode draws nothing at all - which is exactly how a card that visibly rolls in
            // the editor measured rigid here. AlwaysAnimate is what makes the pose real without a camera.
            if (animator != null && animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            string state = "no animator";
            if (animator != null && animator.isActiveAndEnabled)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                state = $"norm={info.normalizedTime:F3} len={info.length:F3} "
                        + $"intransition={(animator.IsInTransition(0) ? 1 : 0)} speed={animator.speed:F2} "
                        + $"culling={animator.cullingMode}";
            }
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked, false);
                    Vector3[] vertices = baked.vertices;
                    if (vertices.Length == 0) continue;
                    Vector3 min = vertices[0], max = vertices[0];
                    foreach (Vector3 vertex in vertices)
                    {
                        min = Vector3.Min(min, vertex);
                        max = Vector3.Max(max, vertex);
                    }
                    Transform[] bones = renderer.bones;
                    string last = bones.Length > 0 && bones[bones.Length - 1] != null
                        ? $"{bones[bones.Length - 1].name}{VectorText(bones[bones.Length - 1].position)}"
                        : "none";
                    Debug.Log($"{Tag} [tears] frame {_tearSampleCount} ctrl{index} '{part.name}' "
                              + $"active={part.activeSelf} {state} size={VectorText(max - min)} "
                              + $"min={VectorText(min)} tip={last}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
        }
    }

    /// <summary>
    /// How the animated tears are actually built, because the exporter only ever sees half of it.
    ///
    /// A tear is a <see cref="SkinnedMeshRenderer"/> posed by an <see cref="Animator"/> whose clip is the prefab's
    /// own `default_animation`. `ModelExporter` reads `mesh.vertices`, which for a skinned mesh is the *bind*
    /// pose - if the clip scales a bone to stretch the card from the eye corner down the cheek, the exported card
    /// is only the bind-pose stub. This prints the bind-pose extent, the posed extent (through BakeMesh, which
    /// runs the skinning for real), the skeleton, and the clip's own curves, so an authored-long card and a
    /// card-that-only-becomes-long-when-posed can be told apart.
    /// </summary>
    private static void DumpTearState(UmaContainerCharacter character)
    {
        if (character == null || character.TearControllers == null)
        {
            Debug.Log($"{Tag} [tears] no tear controllers");
            return;
        }
        DumpTearBundles();
        Debug.Log($"{Tag} [tears] {character.TearControllers.Count} controller(s), "
                  + $"FaceDrivenKeyTarget={(character.FaceDrivenKeyTarget != null)}, "
                  + $"spring containers on the character={character.cySpringDataContainers.Count}");

        // springs bind by bone *name* into one dictionary built from every transform on the character, so a
        // container that lists joint000..joint010 is what makes a tear ribbon hang down the cheek
        int springHits = 0;
        foreach (CySpringDataContainer spring in character.cySpringDataContainers)
        {
            if (spring == null) continue;
            foreach (CySpringParamDataElement element in spring.springParam)
            {
                if (element == null || string.IsNullOrEmpty(element._boneName)) continue;
                if (element._boneName.IndexOf("joint", StringComparison.OrdinalIgnoreCase) < 0) continue;
                springHits++;
                Debug.Log($"{Tag} [tears] spring '{spring.name}' binds '{element._boneName}' "
                          + $"gravity={element._gravity:F3} stiffness={element._stiffnessForce:F5} "
                          + $"drag={element._dragForce:F3} springForce={VectorText(element._springForce)} "
                          + $"children={element._childElements.Count}");
            }
        }
        Debug.Log($"{Tag} [tears] spring params naming a 'joint': {springHits}");

        // every asset the prefab carries, because the body's chara_shader_effect was exactly the kind of thing
        // that hid in a table we had not enumerated yet
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            foreach (GameObject part in new[] { controller.TearObject_0, controller.TearObject_1 })
            {
                if (part == null) continue;
                AssetHolder holder = part.GetComponent<AssetHolder>();
                if (holder == null || holder._assetTable == null || holder._assetTable.list == null) continue;
                var entries = new List<string>();
                foreach (StringObjectPair pair in holder._assetTable.list)
                {
                    if (pair == null) continue;
                    entries.Add($"{pair.Key}={pair.Value.GetType().Name}:{pair.Value.name}");
                }
                Debug.Log($"{Tag} [tears] '{part.name}' asset table ({entries.Count}): {string.Join(", ", entries)}");
            }
        }

        // and the same path the UI slider takes, in case the pose is hung off the weight rather than off frames
        FaceDrivenKeyTarget facial = character.FaceDrivenKeyTarget;
        if (facial != null && facial.OtherMorphs != null)
        {
            foreach (FacialOtherMorph morph in facial.OtherMorphs)
            {
                if (morph == null || !morph.name.StartsWith("Tear")) continue;
                foreach (BindProperty property in morph.BindProperties)
                {
                    if (property == null || property.Type != BindProperty.BindType.TearWeight) continue;
                    Debug.Log($"{Tag} [tears] driving '{morph.name}' TearWeight to 1 through the facial target");
                    facial.ChangeMorphWeight(property, 1f, morph, false);
                }
            }
            for (int index = 0; index < character.TearControllers.Count; index++)
            {
                TearController controller = character.TearControllers[index];
                if (controller == null) continue;
                Debug.Log($"{Tag} [tears] after weight: controller {index} weight={controller.Weight:F3} "
                          + $"0active={(controller.TearObject_0 != null && controller.TearObject_0.activeSelf)} "
                          + $"1active={(controller.TearObject_1 != null && controller.TearObject_1.activeSelf)}");
                DumpTearPartWidth($"[tears]   {index}a weighted", controller.TearObject_0);
            }
        }

        // the ribbon points along its own local +Y, so whichever transform it is hung from decides whether the
        // stream runs down the cheek or up over the eye - print the attach bones' orientation, and the head's
        Transform head = null;
        foreach (Transform candidate in character.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "Head") { head = candidate; break; }
        }
        if (head != null)
        {
            var attach = new List<string> { "Eye_tear_attach_01_L", "Eye_tear_attach_02_L", "Eye_tear_attach_03_L",
                                            "Eye_tear_attach_01_R", "Eye_tear_attach_02_R", "Eye_tear_attach_03_R" };
            Debug.Log($"{Tag} [tears] Head rot={VectorText(head.eulerAngles)} up={VectorText(head.up)}");
            foreach (string name in attach)
            {
                Transform bone = head.Find(name);
                if (bone == null) { Debug.Log($"{Tag} [tears]   {name}: not found under Head"); continue; }
                Debug.Log($"{Tag} [tears]   {name} localRot={VectorText(bone.localEulerAngles)} "
                          + $"worldRot={VectorText(bone.eulerAngles)} up={VectorText(bone.up)} "
                          + $"forward={VectorText(bone.forward)}");
            }
        }
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            Debug.Log($"{Tag} [tears] controller {index}: dir={controller.CurrentDir} "
                      + $"weight={controller.Weight:F3} object={controller.CurrenObject} speed={controller.Speed:F3}");
            DumpTearPart($"[tears]   {index}a", controller.TearObject_0);
            DumpTearPart($"[tears]   {index}b", controller.TearObject_1);
        }
    }

    private static void DumpTearPart(string label, GameObject part)
    {
        if (part == null)
        {
            Debug.Log($"{Tag} {label} (null)");
            return;
        }
        Transform transform = part.transform;
        Debug.Log($"{Tag} {label} '{part.name}' active={part.activeSelf} "
                  + $"localPos={VectorText(transform.localPosition)} worldPos={VectorText(transform.position)} "
                  + $"localRot={VectorText(transform.localEulerAngles)} localScale={VectorText(transform.localScale)} "
                  + $"lossyScale={VectorText(transform.lossyScale)}");
        var chain = new List<string>();
        for (Transform up = transform.parent; up != null && chain.Count < 6; up = up.parent) chain.Add(up.name);
        Debug.Log($"{Tag} {label} parent chain: {string.Join(" < ", chain)}");

        AssetHolder holder = part.GetComponent<AssetHolder>();
        if (holder != null && holder._assetTableValue != null)
        {
            Debug.Log($"{Tag} {label} sheet grid: "
                      + $"{holder._assetTableValue["texture_width"]}x{holder._assetTableValue["texture_height"]} "
                      + $"cells {holder._assetTableValue["texture_part_width"]}x"
                      + $"{holder._assetTableValue["texture_part_height"]}");
        }

        // what is actually attached, because that says how the ribbon is meant to move: an Animator, a
        // CySpringDataContainer, a DynamicBone, or nothing at all
        var types = new Dictionary<string, int>();
        foreach (Component component in part.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            string name = component.GetType().Name;
            types[name] = types.TryGetValue(name, out int count) ? count + 1 : 1;
        }
        Debug.Log($"{Tag} {label} components: "
                  + string.Join(", ", types.OrderBy(pair => pair.Key).Select(pair => pair.Key + (pair.Value > 1 ? "x" + pair.Value : ""))));
        foreach (CySpringDataContainer spring in part.GetComponentsInChildren<CySpringDataContainer>(true))
        {
            Debug.Log($"{Tag} {label}   CySpringDataContainer '{spring.name}': "
                      + $"springParam={spring.springParam.Count} connected={spring.ConnectedBoneList.Count} "
                      + $"dynamicBones={spring.DynamicBones.Count} collisions={spring.collisionParam.Count}");
        }
        foreach (CySpring spring in part.GetComponentsInChildren<CySpring>(true))
        {
            Debug.Log($"{Tag} {label}   CySpring '{spring.name}' on '{spring.gameObject.name}'");
        }

        // the parts sit switched off until a controller raises their weight, and skinning only evaluates on an
        // active renderer, so the measurement happens on an activated copy of the state and the state is restored
        bool wasActive = part.activeSelf;
        part.SetActive(true);
        try
        {
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                Debug.Log($"{Tag} {label} skinned '{renderer.name}' mesh '{(mesh != null ? mesh.name : "null")}' "
                          + $"verts={(mesh != null ? mesh.vertexCount : 0)} "
                          + $"bindLocalBounds={(mesh != null ? BoundsText(mesh.bounds) : "none")} "
                          + $"bones={renderer.bones.Length} rootBone={(renderer.rootBone ? renderer.rootBone.name : "none")}");
                Transform[] bones = renderer.bones;
                for (int b = 0; b < bones.Length && b < 8; b++)
                {
                    Transform bone = bones[b];
                    if (bone == null) continue;
                    Debug.Log($"{Tag} {label}   bone[{b}] '{bone.name}' localPos={VectorText(bone.localPosition)} "
                              + $"localScale={VectorText(bone.localScale)} worldPos={VectorText(bone.position)}");
                }
                if (bones.Length > 8)
                {
                    Debug.Log($"{Tag} {label}   ... {bones.Length - 8} more bones");
                }
                if (bones.Length > 0 && bones[bones.Length - 1] != null)
                {
                    Debug.Log($"{Tag} {label}   last bone '{bones[bones.Length - 1].name}' "
                              + $"worldPos={VectorText(bones[bones.Length - 1].position)}");
                }

                Animator animator = part.GetComponent<Animator>() ?? part.GetComponentInChildren<Animator>(true);
                DumpTearAnimator(label, animator, part);

                // the game raises the weight, which activates the object, and only then does the animator run -
                // an inactive animator never evaluates, so this is the state the mesh is actually drawn in
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    animator.speed = 1f;
                    animator.Play(0, 0, 0f);
                    for (int step = 0; step < 45; step++) animator.Update(1f / 30f);
                    DumpTearBaked(label + " played", renderer, -1f);
                    animator.speed = 0f;
                }

                foreach (float time in new[] { 0f, 0.25f, 0.5f, 0.75f, 0.999f })
                {
                    if (animator != null && animator.runtimeAnimatorController != null && animator.avatar != null)
                    {
                        // speed 1 with a tiny step: the pose is set by the normalised time, and the step is what
                        // makes Unity evaluate it at all (a zero step often evaluates nothing)
                        animator.speed = 1f;
                        animator.Play(0, 0, time);
                        animator.Update(0.0001f);
                    }
                    if (bones.Length > 0)
                    {
                        var moved = new List<string>();
                        for (int b = 0; b < bones.Length && b < 5; b++)
                        {
                            if (bones[b] != null) moved.Add($"{bones[b].name}{VectorText(bones[b].position)}");
                        }
                        Debug.Log($"{Tag} {label} t={time:F3} joints: {string.Join(" ", moved)}");
                    }
                    DumpTearBaked(label, renderer, time);
                }
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    animator.speed = 1f;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label} measurement failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            part.SetActive(wasActive);
        }
    }

    /// <summary>
    /// Every asset in the tear bundles, with curve counts for anything that is an animation. The clip the
    /// reconstruction picks (`default_animation`) is empty as loaded; if the real animation lives in a sibling
    /// clip, it is in one of these bundles and this finds it.
    /// </summary>
    private static void DumpTearBundles()
    {
        if (CliEditorProbeHooks.DumpTearBundles == null)
        {
            Debug.LogWarning($"{Tag} DumpTearBundles needs the editor: it reads animation data through UnityEditor");
            return;
        }
        CliEditorProbeHooks.DumpTearBundles();
    }


    /// <summary>The posed extent only, for re-measuring a part after the weight has been driven.</summary>
    private static void DumpTearPartWidth(string label, GameObject part)
    {
        if (part == null) return;
        bool wasActive = part.activeSelf;
        part.SetActive(true);
        try
        {
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                DumpTearBaked(label, renderer, -1f);
            }
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label} measure failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            part.SetActive(wasActive);
        }
    }

    /// <summary>The posed extent of a skinned mesh: BakeMesh runs the skinning, reading vertices does not.</summary>
    private static void DumpTearBaked(string label, SkinnedMeshRenderer renderer, float time)
    {
        var baked = new Mesh();
        try
        {
            renderer.BakeMesh(baked, false);
            Vector3[] vertices = baked.vertices;
            if (vertices.Length == 0)
            {
                Debug.Log($"{Tag} {label} t={time:F3} baked: no vertices");
                return;
            }
            Vector3 min = vertices[0], max = vertices[0];
            foreach (Vector3 vertex in vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
            Debug.Log($"{Tag} {label} t={time:F3} baked={vertices.Length} verts "
                      + $"min={VectorText(min)} max={VectorText(max)} size={VectorText(max - min)}");
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label} t={time:F3} BakeMesh failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(baked);
        }
    }

    /// <summary>
    /// The clip's serialized fields, to a shallow depth: which curve lists exist and how big they are. This is
    /// how you tell "the clip is empty" apart from "the clip has data that GetCurveBindings does not report".
    /// </summary>
    private static void DumpClipSerialized(string label, AnimationClip clip)
    {
        if (CliEditorProbeHooks.DumpClipSerialized == null)
        {
            Debug.LogWarning($"{Tag} DumpClipSerialized needs the editor: it reads animation data through UnityEditor");
            return;
        }
        CliEditorProbeHooks.DumpClipSerialized(label, clip);
    }

    private static void DumpTearAnimator(string label, Animator animator, GameObject part)
    {
        if (CliEditorProbeHooks.DumpTearAnimator == null)
        {
            Debug.LogWarning($"{Tag} DumpTearAnimator needs the editor: it reads animation data through UnityEditor");
            return;
        }
        CliEditorProbeHooks.DumpTearAnimator(label, animator, part);
    }

    private static string VectorText(Vector3 value) => $"({value.x:F3},{value.y:F3},{value.z:F3})";

    private static string BoundsText(UnityEngine.Bounds bounds) =>
        $"center={VectorText(bounds.center)} size={VectorText(bounds.size)}";

    private static void DumpMorphState(UmaContainerCharacter character)
    {
        FaceDrivenKeyTarget facial = character.FaceDrivenKeyTarget;
        Debug.Log($"{Tag} [morphs] character {character.name}, FaceDrivenKeyTarget "
                  + (facial == null ? "NULL" : $"'{facial.name}'"));
        Debug.Log($"{Tag} [morphs] CheekTex_0={(character.CheekTex_0 ? character.CheekTex_0.name : "NULL")} "
                  + $"CheekTex_1={(character.CheekTex_1 ? character.CheekTex_1.name : "NULL")} "
                  + $"CheekTex_2={(character.CheekTex_2 ? character.CheekTex_2.name : "NULL")}");
        Debug.Log($"{Tag} [morphs] StaticTear_L={(character.StaticTear_L ? character.StaticTear_L.name : "NULL")} "
                  + $"StaticTear_R={(character.StaticTear_R ? character.StaticTear_R.name : "NULL")} "
                  + $"manga L/R={character.LeftMangaObject.Count}/{character.RightMangaObject.Count} "
                  + $"tearControllers={character.TearControllers.Count}");

        foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string lower = renderer.name.ToLowerInvariant();
            if (!lower.Contains("cheek") && !lower.Contains("face") && !lower.Contains("eyebrow") && !lower.Contains("manga"))
                continue;
            var material = renderer.sharedMaterial;
            Debug.Log($"{Tag} [morphs] renderer '{renderer.name}' active={renderer.gameObject.activeSelf} "
                      + $"material '{(material ? material.name : "none")}' "
                      + $"mainTex '{(material && material.mainTexture ? material.mainTexture.name : "none")}' "
                      + $"shader '{(material && material.shader ? material.shader.name : "none")}'");
        }

        if (facial == null) return;
        Debug.Log($"{Tag} [morphs] OtherMorphs: {facial.OtherMorphs.Count}");
        foreach (var morph in facial.OtherMorphs)
        {
            if (morph == null) continue;
            Debug.Log($"{Tag} [morphs]   '{morph.name}' locator={(morph.locator ? morph.locator.name : "none")} "
                      + $"binds={morph.BindProperties.Count}");
            foreach (var property in morph.BindProperties)
            {
                if (property == null) continue;
                var prefabs = new List<string>();
                foreach (var prefab in property.BindPrefabs) if (prefab) prefabs.Add(prefab.name);
                Debug.Log($"{Tag} [morphs]     {property.Type} {property.Part} "
                          + $"property='{property.PropertyName}' "
                          + $"material='{(property.BindMaterial ? property.BindMaterial.name : "none")}' "
                          + $"texture='{(property.BindTexture ? property.BindTexture.name : "none")}' "
                          + $"prefabs=[{string.Join(",", prefabs)}]");
            }
        }
    }

    /// <summary>
    /// The eye-highlight numbers the game actually ships: how far <c>_Limit</c> drops in the teary state, and
    /// the throb's rate and amplitude - the three values docs/UMA_EYE_SHADER.md leaves open.
    ///
    /// This is worth reading here rather than reaching for an extractor for two reasons. The harness already
    /// loads the game's own bundles, and UmaContainerCharacter swaps each material's shader for the game's
    /// *shipped* compiled shader out of shader.a (<see cref="UmaAssetManager.EyeShader"/>), so the property list
    /// and the values below are the real ones rather than a port's. What a compiled shader can give is
    /// properties and defaults, never source - the decompile stays the source of truth for the logic.
    ///
    /// Prints every property of the eye/face/cheek materials rather than a guessed subset: the property names
    /// on the shipped shader are not known in advance, so a filter is what would hide the answer. Then it prints
    /// chara_shader_effect, the one asset the game keeps runtime material-driving numbers in, and scans a few
    /// motion bundles for clips that animate a material property - where a keyframed teary threshold would live.
    /// </summary>
    private static void DumpShaderFacts(UmaContainer container)
    {
        if (CliEditorProbeHooks.DumpShaderFacts == null)
        {
            Debug.LogWarning($"{Tag} DumpShaderFacts needs the editor: it reads animation data through UnityEditor");
            return;
        }
        CliEditorProbeHooks.DumpShaderFacts(container);
    }

    /// <summary>Case insensitive containment, shared with the editor probes.</summary>
    public static bool Contains(string text, string needle) =>
        !string.IsNullOrEmpty(text) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// chara_shader_effect, printed as the serialised fields it is stored as. This is the one asset that carries
    /// runtime material-driving numbers rather than a static material value: CharaShaderEffectData.updateUV
    /// writes _UVEmissiveScroll and _UVEmissivePower from these params every frame, which is the mechanism
    /// behind the animated tears. The fields are private, so this goes through SerializedObject.
    /// </summary>
    private static void DumpShaderEffectData(string label, CharaShaderEffectData data)
    {
        if (CliEditorProbeHooks.DumpShaderEffectData == null)
        {
            Debug.LogWarning($"{Tag} DumpShaderEffectData needs the editor: it reads animation data through UnityEditor");
            return;
        }
        CliEditorProbeHooks.DumpShaderEffectData(label, data);
    }

    private static void DumpFacePipeline(GameObject root, string stem)
    {
        if (CliEditorProbeHooks.DumpFacePipeline == null)
        {
            Debug.LogWarning($"{Tag} DumpFacePipeline needs the editor: it reads shader keywords through UnityEditor");
            return;
        }
        CliEditorProbeHooks.DumpFacePipeline(root, stem);
    }

    /// <summary>
    /// Writes the arrays the exporter reads for one renderer, so the pmx it produced can be compared
    /// against the game's own data vertex by vertex rather than through a histogram. The positions are
    /// written in exactly the space the exporter writes them in
    /// (root.InverseTransformPoint(renderer.transform.TransformPoint(baked.vertices[i]))), which makes
    /// the pmx's own order a valid alignment for the rest.
    /// </summary>
    public static void DumpMeshArrays(SkinnedMeshRenderer renderer, Mesh mesh, GameObject root)
    {
        string exportPath = CliState.GetString(KeyOutPath, "");
        if (string.IsNullOrEmpty(exportPath)) return;

        var baked = new Mesh();
        renderer.BakeMesh(baked, true);
        var lines = new List<string>
        {
            $"renderer {renderer.name}",
            $"mesh {mesh.name}",
            $"isReadable {mesh.isReadable}",
            $"vertexCount {mesh.vertexCount}",
            $"bakedVertexCount {baked.vertexCount}",
            $"subMeshCount {mesh.subMeshCount}",
            $"rootWorldToLocal {MatrixText(root.transform.worldToLocalMatrix)}",
            $"rendererLocalToWorld {MatrixText(renderer.transform.localToWorldMatrix)}",
        };

        var normals = mesh.normals;
        var bakedNormals = baked.normals;
        var colours = mesh.colors;
        var uv = mesh.uv;
        var uv2 = mesh.uv2;
        var uv3 = mesh.uv3;

        lines.Add($"positions {mesh.vertexCount}");
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            var p = root.transform.InverseTransformPoint(renderer.transform.TransformPoint(baked.vertices[i]));
            lines.Add($"{p.x:R} {p.y:R} {p.z:R}");
        }

        lines.Add($"normals {normals.Length}");
        foreach (var n in normals) lines.Add($"{n.x:R} {n.y:R} {n.z:R}");
        lines.Add($"bakedNormals {bakedNormals.Length}");
        foreach (var n in bakedNormals) lines.Add($"{n.x:R} {n.y:R} {n.z:R}");

        lines.Add($"colours {colours.Length}");
        foreach (var c in colours) lines.Add($"{c.r:R} {c.g:R} {c.b:R} {c.a:R}");
        lines.Add($"uv {uv.Length}");
        foreach (var t in uv) lines.Add($"{t.x:R} {t.y:R}");
        lines.Add($"uv2 {uv2.Length}");
        foreach (var t in uv2) lines.Add($"{t.x:R} {t.y:R}");
        lines.Add($"uv3 {uv3.Length}");
        foreach (var t in uv3) lines.Add($"{t.x:R} {t.y:R}");

        string dumpPath = exportPath + ".mesh.txt";
        File.AppendAllLines(dumpPath, lines);
        Debug.Log($"{Tag} [face] wrote {lines.Count} lines of mesh arrays to {dumpPath}");
    }

    private static string MatrixText(Matrix4x4 m)
    {
        var parts = new List<string>(16);
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                parts.Add(m[row, column].ToString("R"));
        return string.Join(" ", parts);
    }

    private static void DumpMaterials(GameObject root, string stem)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var shaderCounts = new Dictionary<string, int>();
        var problemMaterials = new List<string>();
        var textures = new HashSet<string>();
        int materialCount = 0;
        int noMainTexProperty = 0;
        int nullMainTex = 0;
        int missingShader = 0;

        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                materialCount++;
                if (material == null)
                {
                    problemMaterials.Add($"{renderer.name}: <null material slot>");
                    continue;
                }

                string shaderName = material.shader != null ? material.shader.name : "<no shader>";
                shaderCounts[shaderName] = shaderCounts.TryGetValue(shaderName, out int count) ? count + 1 : 1;

                if (material.shader == null || shaderName == "Hidden/InternalErrorShader") missingShader++;

                bool hasMainTex = material.HasProperty("_MainTex");
                if (!hasMainTex)
                {
                    noMainTexProperty++;
                    Color color = material.HasProperty("_Color") ? material.color : Color.white;
                    problemMaterials.Add($"{renderer.name} / {material.name}: shader '{shaderName}' has no _MainTex, "
                                         + $"color {color} -> renders flat {(color == Color.white ? "WHITE" : "colour")}");
                    continue;
                }

                var mainTex = material.mainTexture;
                if (mainTex == null)
                {
                    nullMainTex++;
                    string properties = string.Join(", ", material.GetTexturePropertyNames()
                        .Select(p => $"{p}={(material.GetTexture(p) == null ? "null" : material.GetTexture(p).name)}"));
                    problemMaterials.Add($"{renderer.name} / {material.name}: _MainTex is NULL (shader '{shaderName}') "
                                         + $"texture properties: [{properties}]");
                }
                else
                {
                    textures.Add(mainTex.name);
                }
            }
        }

        Debug.Log($"{Tag} material dump for {root.name}: {renderers.Length} renderers, {materialCount} material slots");
        Debug.Log($"{Tag}   distinct shaders: {shaderCounts.Count}");
        foreach (var pair in shaderCounts.OrderByDescending(p => p.Value).Take(25))
        {
            Debug.Log($"{Tag}     {pair.Value,4}  {pair.Key}");
        }
        Debug.Log($"{Tag}   materials with no _MainTex property: {noMainTexProperty}");
        Debug.Log($"{Tag}   materials with a NULL _MainTex  : {nullMainTex}");
        Debug.Log($"{Tag}   materials with a missing shader : {missingShader}");
        Debug.Log($"{Tag}   distinct main textures used      : {textures.Count}");
        foreach (string problem in problemMaterials.Take(60)) Debug.Log($"{Tag}   ! {problem}");
        if (problemMaterials.Count > 60) Debug.Log($"{Tag}   ! ... and {problemMaterials.Count - 60} more suspicious materials");

        // components that might be assigning textures/banners at runtime
        var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(b => b != null)
            .GroupBy(b => b.GetType().Name)
            .OrderByDescending(g => g.Count())
            .ToList();
        Debug.Log($"{Tag}   components on the prefab: {string.Join(", ", behaviours.Select(g => $"{g.Key}x{g.Count()}").Take(30))}");

        if (string.IsNullOrEmpty(stem)) return;

        // everything the loaded bundles brought into memory that belongs to this scene - this is both
        // the evidence that the prefab's materials are intentionally textureless and the candidate
        // list a texture-set dropdown would offer
        var materialAssets = Resources.FindObjectsOfTypeAll<Material>()
            .Where(m => m.name.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(m => m.name)
            .ToList();
        Debug.Log($"{Tag}   material assets matching '{stem}': {materialAssets.Count}");
        foreach (var material in materialAssets.Take(40))
        {
            string properties = string.Join(", ", material.GetTexturePropertyNames()
                .Select(p => $"{p}={(material.GetTexture(p) == null ? "null" : material.GetTexture(p).name)}"));
            Debug.Log($"{Tag}     {material.name}: shader={material.shader?.name} [{properties}]");
        }

        var textureAssets = Resources.FindObjectsOfTypeAll<Texture2D>()
            .Where(t => t.name.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(t => t.name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        Debug.Log($"{Tag}   texture assets matching '{stem}': {textureAssets.Count}");
        foreach (string texture in textureAssets.Take(80)) Debug.Log($"{Tag}     {texture}");

        // The interesting question: for a material like "mtl_<stem>_000_<suffix>" that has no texture,
        // does a "tex_<stem>_<variant>_<suffix>" texture set exist as a separate loadable asset?
        var suffixes = Resources.FindObjectsOfTypeAll<Material>()
            .Where(m => m.name.StartsWith($"mtl_{stem}_", StringComparison.OrdinalIgnoreCase))
            .Where(m => m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == null)
            .Select(m => MaterialSuffix(m.name, stem))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();
        if (suffixes.Count > 0)
        {
            Debug.Log($"{Tag}   probing texture sets for textureless suffixes: {string.Join(", ", suffixes)}");
            ProbeTextureVariants(stem, suffixes);
        }
    }

    /// <summary>"mtl_&lt;stem&gt;_000_base01" -> "base01" (drops the 3 digit variant token).</summary>
    private static string MaterialSuffix(string materialName, string stem)
    {
        string prefix = $"mtl_{stem}_";
        if (!materialName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = materialName.Substring(prefix.Length).Split('_');
        return parts.Length > 1 ? string.Join("_", parts.Skip(1)) : null;
    }

    /// <summary>Enumerates the alternative texture sets for a suffix and tries to load each one.</summary>
    private static void ProbeTextureVariants(string stem, List<string> suffixes)
    {
        var main = UmaViewerMain.Instance;
        foreach (string suffix in suffixes)
        {
            string needle = $"tex_{stem}_";
            var candidates = main.AbList.Values
                .Where(e => e.Name.StartsWith("3d/env", StringComparison.OrdinalIgnoreCase))
                .Where(e =>
                {
                    string file = Path.GetFileName(e.Name);
                    return file.StartsWith(needle, StringComparison.OrdinalIgnoreCase)
                           && file.EndsWith($"_{suffix}", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(e => e.Name)
                .ToList();

            Debug.Log($"{Tag}     suffix '{suffix}': {candidates.Count} texture set(s) in the database");
            foreach (var entry in candidates.Take(8))
            {
                if (!File.Exists(entry.Path))
                {
                    Debug.Log($"{Tag}       MISSING ON DISK {entry.Name}");
                    continue;
                }
                try
                {
                    var texture = entry.Get<Texture2D>();
                    Debug.Log(texture != null
                        ? $"{Tag}       OK    {entry.Name} -> {texture.width}x{texture.height} ({texture.format})"
                        : $"{Tag}       EMPTY {entry.Name} -> bundle has no Texture2D");
                }
                catch (Exception ex)
                {
                    Debug.Log($"{Tag}       FAIL  {entry.Name}: {ex.GetType().Name} {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// "pfb_env_home10001_main000_000" -> "env_home10001_main000": the part shared by the prefab, its
    /// materials and every texture variant set that belongs to it.
    /// </summary>
    private static string SceneStem(string entryName)
    {
        string name = Path.GetFileName(entryName);
        if (name.StartsWith("pfb_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(4);
        var parts = name.Split('_');
        if (parts.Length > 1 && parts[parts.Length - 1].Length <= 4 && parts[parts.Length - 1].All(char.IsDigit))
        {
            name = string.Join("_", parts.Take(parts.Length - 1));
        }
        return name;
    }

    /// <summary>One model a batch run has to write: a character and one of its costumes.</summary>
    [Serializable]
    private class BatchJob
    {
        public int id;
        public string costume;
    }

    /// <summary>
    /// The models a `--all-costumes` / `--all-chars` run will write, in the order it will write them.
    ///
    /// It is rebuilt from the manifest rather than carried in memory, because a batch is long enough that the
    /// editor's play-mode domain reload will happen in the middle of it: what is left is the index in
    /// <see cref="KeyBatchIndex"/>, and the plan has to be derivable again from the game data alone.
    /// </summary>
    private static List<BatchJob> BatchPlan(Options options, out string failure)
    {
        failure = null;
        var plan = new List<BatchJob>();
        var main = UmaViewerMain.Instance;

        var characters = new List<CharaEntry>();
        if (options.AllChars)
        {
            characters.AddRange(main.Characters.OrderBy(chara => chara.Id));
        }
        else
        {
            var one = FindCharacter(options.CharId);
            if (one == null)
            {
                failure = $"--all-costumes needs a character: pass --char <id> (see `list chars`)";
                return plan;
            }
            characters.Add(one);
        }

        foreach (var chara in characters)
        {
            if (options.AllCostumes)
            {
                foreach (string costume in CostumeIds(chara))
                {
                    plan.Add(new BatchJob { id = chara.Id, costume = costume });
                }
                continue;
            }

            // no --all-costumes: one job per character, at the costume that was asked for (or its default)
            plan.Add(new BatchJob { id = chara.Id, costume = options.CostumeId ?? "" });
        }

        return plan;
    }

    /// <summary>The costume suffixes a character has, in manifest order: `00`, `01`, `0004_00`, ...</summary>
    private static List<string> CostumeIds(CharaEntry chara)
    {
        return UmaViewerMain.Instance.AbChara
            .Where(entry => entry.Name.StartsWith(UmaDatabaseController.BodyPath)
                            && !entry.Name.Contains("clothes")
                            && entry.Name.Contains($"pfb_bdy{chara.Id}"))
            .Select(entry => entry.Name.Split('_').Last())
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Where a batch writes one model: `&lt;out folder&gt;/&lt;id&gt;_&lt;costume&gt;.pmx`.</summary>
    private static string BatchPath(string folder, BatchJob job) =>
        Path.Combine(folder, $"{job.id}_{job.costume}.pmx");

    /// <summary>
    /// Walks one model of a batch: plans the run if it has not been planned, skips what is already on disk, and
    /// otherwise loads the model and lets the export stage write it. <see cref="ContinueBatch"/> picks it up again
    /// afterwards, so a batch costs one boot rather than one per model.
    /// </summary>
    private static void BatchStep(Options options)
    {
        string folder = Path.GetFullPath(options.OutPath);
        // a batch writes many models, so --out is a folder here: a file name is almost certainly a mistake, and
        // creating a directory called "1001_00.pmx" would be a confusing way to find out
        string extension = Path.GetExtension(folder).ToLowerInvariant();
        if (extension == ".pmx" || extension == ".fbx")
        {
            Fail($"--all-costumes and --all-chars write a folder: --out is '{options.OutPath}', "
                 + "which looks like one file. Give a directory instead, e.g. --out D:/out/costumes");
            return;
        }

        var plan = BatchPlan(options, out string failure);
        if (failure != null)
        {
            Fail(failure);
            return;
        }
        if (plan.Count == 0)
        {
            Fail("nothing to export: no characters or costumes matched");
            return;
        }

        Directory.CreateDirectory(folder);
        if (CliState.GetInt(KeyBatchPlanned, 0) == 0)
        {
            CliState.SetInt(KeyBatchPlanned, 1);
            CliState.SetInt(KeyBatchTotal, plan.Count);
            CliState.SetInt(KeyBatchIndex, 0);
            CliState.SetInt(KeyBatchWritten, 0);
            CliState.SetInt(KeyBatchBytes, 0);
            CliState.SetInt(KeyScanFailures, 0);
            Debug.Log($"{Tag} batch: {plan.Count} model(s) into {folder}");
        }

        int index = CliState.GetInt(KeyBatchIndex, 0);
        // skip what is already there without spending a frame on each: a resumed batch is mostly skips
        int skipped = 0;
        while (index < plan.Count)
        {
            string existing = BatchPath(folder, plan[index]);
            if (!File.Exists(existing) || new FileInfo(existing).Length == 0) break;
            index++;
            skipped++;
        }
        if (skipped > 0)
        {
            CliState.SetInt(KeyBatchIndex, index);
            Debug.Log($"{Tag} batch: {skipped} model(s) already on disk, skipping to {index}/{plan.Count}");
        }

        if (index >= plan.Count)
        {
            int written = CliState.GetInt(KeyBatchWritten, 0);
            int failures = CliState.GetInt(KeyScanFailures, 0);
            long bytes = CliState.GetInt(KeyBatchBytes, 0);
            Debug.Log($"{Tag} batch: {written} model(s) written, {failures} failed, {bytes / (1024 * 1024)} MB"
                      + (failures > 0 ? " - the failures are named above" : ""));
            // the counters stay readable for the result line; the flag says the next run must plan again
            CliState.SetInt(KeyBatchPlanned, 0);
            CliState.SetString(KeyOutPath, folder);
            if (failures > 0 && written == 0)
            {
                Fail($"batch: none of the {plan.Count} model(s) could be written");
                return;
            }
            Succeed();
            return;
        }

        var job = plan[index];
        var chara = FindCharacter(job.id);
        if (chara == null)
        {
            CliState.SetInt(KeyBatchIndex, index + 1);
            CliState.SetInt(KeyScanFailures, CliState.GetInt(KeyScanFailures, 0) + 1);
            Debug.LogWarning($"{Tag} [{index + 1}/{plan.Count}] character {job.id} is not in this install, skipping");
            return;
        }

        if (!TryResolveCostume(chara, job.costume ?? "", out string costumeId, out string costumeFailure))
        {
            CliState.SetInt(KeyBatchIndex, index + 1);
            CliState.SetInt(KeyScanFailures, CliState.GetInt(KeyScanFailures, 0) + 1);
            Debug.LogWarning($"{Tag} [{index + 1}/{plan.Count}] {chara.Id}_{job.costume}: {costumeFailure}");
            return;
        }

        string target = BatchPath(folder, new BatchJob { id = chara.Id, costume = costumeId });
        CliState.SetString(KeyOutPath, target);
        CliState.SetInt(KeyIsProp, 0);
        CliState.SetString(KeyPropName, "");
        Debug.Log($"{Tag} [{index + 1}/{plan.Count}] exporting {chara.Id}_{costumeId} ({chara.Name})");
        StartLoad(chara, costumeId);
        CurrentStage = Stage.WaitModel;
    }

    /// <summary>
    /// Ends one model of a batch and moves to the next. The counters live in the session, so a batch that is
    /// interrupted reports what it actually wrote rather than starting the count from zero next time.
    /// </summary>
    private static void ContinueBatch(string failure)
    {
        int index = CliState.GetInt(KeyBatchIndex, 0);
        int total = CliState.GetInt(KeyBatchTotal, 0);
        string written = CliState.GetString(KeyOutPath, "");

        if (failure != null)
        {
            CliState.SetInt(KeyScanFailures, CliState.GetInt(KeyScanFailures, 0) + 1);
            Debug.Log($"{Tag} [{index + 1}/{total}] {Path.GetFileName(written)}: FAILED: {failure}");
        }
        else
        {
            long bytes = File.Exists(written) ? new FileInfo(written).Length : 0;
            CliState.SetInt(KeyBatchWritten, CliState.GetInt(KeyBatchWritten, 0) + 1);
            CliState.SetInt(KeyBatchBytes, CliState.GetInt(KeyBatchBytes, 0) + (int)bytes);
            Debug.Log($"{Tag} [{index + 1}/{total}] {Path.GetFileName(written)}: {bytes} bytes");
        }

        CliState.SetInt(KeyBatchIndex, index + 1);
        CliState.SetString(KeyOutPath, "");
        CliState.SetInt(KeyIsProp, 0);
        CurrentStage = Stage.WaitStartup;
    }

    private static void StartLoad(CharaEntry chara, string costumeId)
    {
        var main = UmaViewerMain.Instance;
        var builder = UmaViewerBuilder.Instance;

        // Mirrors UmaViewerUI's character selection: same asset set, same load entry point.
        var list = new List<UmaDatabaseEntry>();
        list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && a.Name.Contains(chara.Id.ToString())));
        list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.HeadPath) && a.Name.Contains(chara.Id.ToString())));

        var tailPath = UmaViewerUI.Instance != null ? UmaViewerUI.Instance.getCharaTailPath(chara) : null;
        if (!string.IsNullOrEmpty(tailPath))
        {
            list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(tailPath)));
        }

        if (main.AbList.TryGetValue("3d/animator/drivenkeylocator", out var drivenKeyEntry)) list.Add(drivenKeyEntry);
        string motionPath = $"3d/motion/event/body/chara/chr{chara.Id}_00/anm_eve_chr{chara.Id}_00_idle01_loop";
        if (main.AbList.TryGetValue(motionPath, out var motionEntry)) list.Add(motionEntry);

        builder.UnloadUma();
        UmaAssetManager.PreLoadAndRun(list, delegate
        {
            try
            {
                builder.StartCoroutine(builder.LoadUma(chara, costumeId, false, string.Empty));
            }
            catch (Exception ex)
            {
                Fail($"loading character failed: {ex.Message}");
            }
        });
    }

    private static bool TryResolveCostume(CharaEntry chara, string requested, out string costumeId, out string failure)
    {
        costumeId = "";
        failure = null;

        var bodies = UmaViewerMain.Instance.AbChara
            .Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && !a.Name.Contains("clothes") && a.Name.Contains($"pfb_bdy{chara.Id}"))
            .OrderBy(a => a.Name)
            .ToList();

        if (bodies.Count == 0)
        {
            failure = $"no body assets found for character {chara.Id}";
            return false;
        }

        if (string.IsNullOrEmpty(requested))
        {
            costumeId = bodies[0].Name.Split('_').Last();
            return true;
        }

        var match = bodies.FirstOrDefault(b => b.Name.Split('_').Last() == requested);
        if (match == null)
        {
            failure = $"costume '{requested}' not found for character {chara.Id}; available: "
                      + string.Join(", ", bodies.Select(b => b.Name.Split('_').Last()));
            return false;
        }

        costumeId = requested;
        return true;
    }

    private static void PrintCharacters()
    {
        var main = UmaViewerMain.Instance;
        Debug.Log($"{Tag} characters:");
        foreach (var chara in main.Characters.OrderBy(c => c.Id))
        {
            string costumes = string.Join(",", main.AbChara
                .Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && !a.Name.Contains("clothes") && a.Name.Contains($"pfb_bdy{chara.Id}"))
                .Select(a => a.Name.Split('_').Last())
                .Distinct()
                .OrderBy(x => x));
            Debug.Log($"{Tag}   {chara.Id}\t{chara.Name}\t[{costumes}]");
        }
    }

    /// <summary>
    /// Explains one sound asset's transcript - the text half of the audio export, so the join against the
    /// game data can be checked without exporting (or playing) anything. Accepts a story sheet
    /// (041047007), an asset name (snd_voi_story_041047007, snd_voi_home_100100) or a full asset path.
    /// </summary>
    private static void PrintTranscript(string query)
    {
        // a story sheet is also its timeline's name; print what the sheet resolves to first
        const string storyPrefix = "snd_voi_story_";
        string sheet = query;
        int at = sheet.IndexOf(storyPrefix, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) sheet = sheet.Substring(at + storyPrefix.Length);
        sheet = Path.GetFileNameWithoutExtension(sheet);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Dictionary<int, List<TranscriptLine>> byCue = AudioTranscripts.ForSheet(sheet);
        if (byCue.Count > 0)
        {
            int lines = 0;
            foreach (KeyValuePair<int, List<TranscriptLine>> pair in byCue.OrderBy(p => p.Key))
            {
                foreach (TranscriptLine line in pair.Value)
                {
                    Debug.Log($"{Tag} cue {pair.Key,4} {line.Speaker}: {OneLine(line.Text)}"
                              + $"  [story {line.StoryId} {line.StoryTitle}]");
                    lines++;
                }
            }
            Debug.Log($"{Tag} sheet {sheet}: {byCue.Count} cue(s) carry text, {lines} line(s), "
                      + $"resolved in {stopwatch.ElapsedMilliseconds} ms");
        }
        else
        {
            Debug.Log($"{Tag} sheet {sheet}: no story timeline carries lines for it "
                      + $"(resolved in {stopwatch.ElapsedMilliseconds} ms)");
        }

        UmaDatabaseEntry awb = FindSoundAsset(query);
        if (awb == null)
        {
            Fail($"no sound asset matches '{query}'");
            return;
        }

        List<SoundCues.Cue> cues = SoundCues.Of(awb);
        Debug.Log($"{Tag} {awb.Name}: {cues?.Count ?? 0} clip(s)"
                  + (cues == null ? ", no acb next to it so the cue names are unknown" : ""));
        Debug.Log($"{Tag} character voice table: {UmaDatabaseController.SystemVoiceTextCount} cue name(s)");
        if (cues == null) return;

        for (int i = 0; i < cues.Count; i++)
        {
            string text = AudioTranscripts.Summarize(AudioTranscripts.ForClip(awb, i));
            Debug.Log($"{Tag} clip {i,4} {cues[i].Name ?? "-"}: {(string.IsNullOrEmpty(text) ? "-" : OneLine(text))}");
        }
    }

    /// <summary>A sound asset by full path, asset name, or the bare name its file carries.</summary>
    private static UmaDatabaseEntry FindSoundAsset(string query)
    {
        var main = UmaViewerMain.Instance;
        if (main?.AbList == null) return null;

        string file = Path.GetFileNameWithoutExtension(query);
        foreach (string candidate in new[] { query, $"sound/c/{query}.awb", $"sound/v/{query}.awb", $"sound/c/{file}.awb", $"sound/v/{file}.awb" })
        {
            if (main.AbList.TryGetValue(candidate, out UmaDatabaseEntry entry)) return entry;
        }
        return null;
    }


    private static bool _audioExported;

    /// <summary>
    /// Writes one clip of a sound asset to an mp3, with its transcript in the tag. Accepts a story sheet
    /// (041047007), the asset name (snd_voi_story_041047007) or a full asset path.
    /// </summary>
    private static bool StartAudioExport(Options options)
    {
        string query = options.ExportAudio;
        var main = UmaViewerMain.Instance;
        if (main?.AbList == null) { Fail("no asset list"); return false; }

        UmaDatabaseEntry entry = null;
        foreach (string candidate in new[]
                 {
                     query,
                     $"sound/c/{query}.awb",
                     $"sound/c/snd_voi_story_{query}.awb",
                     $"sound/v/{query}.awb",
                 })
        {
            if (main.AbList.TryGetValue(candidate, out entry)) break;
            entry = null;
        }
        if (entry == null) { Fail($"no sound asset matches '{query}'"); return false; }

        var streams = UmaViewerBuilder.Instance.LoadAudioStreams(entry);
        if (streams.Count == 0) { Fail($"{entry.Name}: no clips (file missing?)"); return false; }
        if (options.ExportClip >= streams.Count)
        {
            Fail($"{entry.Name} has {streams.Count} clip(s), so -umaExportClip {options.ExportClip} is out of range");
            return false;
        }

        string path = string.IsNullOrEmpty(options.OutPath)
            ? Path.Combine("HeadlessExports", Path.GetFileNameWithoutExtension(entry.Name) + ".mp3")
            : options.OutPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

        AudioTranscriptTag? tag = AudioTranscripts.Tag(entry, options.ExportClip);
        CliState.SetString(KeyOutPath, path);
        _audioExported = false;
        Debug.Log($"{Tag} exporting {entry.Name} clip {options.ExportClip} of {streams.Count} to {path}"
                  + (tag.HasValue ? " with a transcript tag" : " (no transcript for this clip)"));
        UmaViewerBuilder.Instance.StartCoroutine(RunAudioExport(streams, path, options.ExportClip, tag));
        return true;
    }

    private static System.Collections.IEnumerator RunAudioExport(List<UmaWaveStream> streams, string path,
                                                                 int clip, AudioTranscriptTag? tag)
    {
        yield return AudioExporter.ExportAudio(streams, path, clip, tag);
        _audioExported = true;
    }

    /// <summary>Reads the tag back out of the written file, so a broken writer is visible here.</summary>
    private static void ReportTag(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Fail($"no mp3 at {path}"); return; }
        byte[] head = File.ReadAllBytes(path);
        Debug.Log($"{Tag} wrote {head.Length} bytes to {path}");
        if (head.Length < 10 || head[0] != (byte)'I' || head[1] != (byte)'D' || head[2] != (byte)'3')
        {
            Debug.Log($"{Tag} the file carries no ID3 tag - the clip has no transcript");
            return;
        }

        int size = ((head[6] & 0x7F) << 21) | ((head[7] & 0x7F) << 14) | ((head[8] & 0x7F) << 7) | (head[9] & 0x7F);
        Debug.Log($"{Tag} ID3v2.{head[3]} tag, {size} bytes of frames");
        int at = 10;
        while (at + 10 <= 10 + size)
        {
            string id = System.Text.Encoding.ASCII.GetString(head, at, 4);
            if (id.Trim((char)0).Length == 0) break;
            int frameSize = (head[at + 4] << 24) | (head[at + 5] << 16) | (head[at + 6] << 8) | head[at + 7];
            if (frameSize <= 0 || at + 10 + frameSize > head.Length) break;
            byte encoding = head[at + 10];
            int textAt = at + 10 + 1;
            if (id == "USLT" || id == "COMM") textAt += 7;   // language (3) + empty utf-16 descriptor (4)
            int length = at + 10 + frameSize - textAt;
            string text = encoding == 1
                ? System.Text.Encoding.Unicode.GetString(head, textAt, Math.Max(0, length)).TrimStart('\uFEFF')
                : System.Text.Encoding.UTF8.GetString(head, textAt, Math.Max(0, length));
            Debug.Log($"{Tag}   {id} ({frameSize} bytes): {text.Replace("\r\n", " / ").Replace("\n", " / ").Trim((char)0)}");
            at += 10 + frameSize;
        }
    }

    /// <summary>One log line for a multi-line dialogue line.</summary>
    private static string OneLine(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Replace("\r\n", " / ").Replace("\r", " ").Replace("\n", " / ");

    private static void PrintCostumes(CharaEntry chara)
    {
        var costumes = UmaViewerMain.Instance.AbChara
            .Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && !a.Name.Contains("clothes") && a.Name.Contains($"pfb_bdy{chara.Id}"))
            .Select(a => a.Name)
            .OrderBy(a => a)
            .ToList();
        Debug.Log($"{Tag} costumes for {chara.Id} ({chara.Name}):");
        foreach (var name in costumes) Debug.Log($"{Tag}   {name.Split('_').Last()}\t{name}");
    }

    /// <summary>A run's result as one line, for anything that would rather parse than read a log.</summary>
    [Serializable]
    private class RunResult
    {
        public bool ok;
        /// <summary>What kind of run it was: export, export-prop, vmd, screenshot, scan, scan-export, dump, list,
        /// transcript, audio.</summary>
        public string mode;
        /// <summary>The file the run wrote, with forward slashes, or "" when it wrote none.</summary>
        public string path;
        public long bytes;
        public double seconds;
        /// <summary>Models that failed inside a sweep or a dump, where the run itself still succeeded.</summary>
        public int failures;
        /// <summary>Models a batch planned to write.</summary>
        public int jobs;
        /// <summary>How many of them it wrote.</summary>
        public int written;
        public string error;
    }

    /// <summary>
    /// Writes the `JSON` line when `--json` was given. It is emitted on success and on failure alike, so a caller
    /// never has to tell "no JSON" from "the run died before it could write one" by reading the exit code as well.
    /// </summary>
    private static void EmitResult(string error)
    {
        var options = CurrentOptions;
        if (options == null || !options.Json) return;

        string written = CliState.GetString(KeyOutPath, "");
        var result = new RunResult
        {
            ok = error == null,
            mode = RunMode(options),
            // a dump writes a folder, not a file: name the folder, so the line is useful for both kinds of run
            path = (string.IsNullOrEmpty(written) && !string.IsNullOrEmpty(options.DumpProps)
                        ? options.DumpProps
                        : written).Replace('\\', '/'),
            bytes = !string.IsNullOrEmpty(written) && File.Exists(written) ? new FileInfo(written).Length : 0,
            seconds = RunStartedUtc == default
                ? 0
                : Math.Round((DateTime.UtcNow - RunStartedUtc).TotalSeconds, 1),
            failures = CliState.GetInt(KeyScanFailures, 0),
            jobs = CliState.GetInt(KeyBatchTotal, 0),
            written = CliState.GetInt(KeyBatchWritten, 0),
            error = error ?? "",
        };
        Debug.Log($"{Tag} JSON {JsonUtility.ToJson(result)}");
    }

    /// <summary>What this run is doing, in one word, for the result line.</summary>
    private static string RunMode(Options options)
    {
        if (!string.IsNullOrEmpty(options.DumpProps)) return "dump";
        if (!string.IsNullOrEmpty(options.ScanProps)) return options.ScanExport ? "scan-export" : "scan";
        if (!string.IsNullOrEmpty(options.ExportAudio)) return "audio";
        if (!string.IsNullOrEmpty(options.Transcript)) return "transcript";
        if (options.ListChars || options.ListCostumes || options.ListProps || options.ListTextures
            || options.ListFamilies) return "list";
        if (!string.IsNullOrEmpty(options.Screenshot)) return "screenshot";
        if (!string.IsNullOrEmpty(options.RecordVmd)) return "vmd";
        if (options.AllCostumes || options.AllChars) return "batch";
        if (!string.IsNullOrEmpty(options.Prop)) return "export-prop";
        return "export";
    }

    private static void Succeed()
    {
        CurrentStage = Stage.Done;
        // the result line is written first: it reads the options back out of the session, and clearing them
        // here is what makes the next run start clean
        EmitResult(null);
        CliState.SetString(KeyOptions, "");
        CliHost.Disarm();
        // every mode has to emit this marker: the wrappers wait for it, and a mode that only logs its
        // own summary looks like a hang to them
        Debug.Log($"{Tag} OK");
        CliHost.Exit(0);
    }

    private static void Fail(string message)
    {
        CurrentStage = Stage.Done;
        EmitResult(message);
        CliState.SetString(KeyOptions, "");
        CliHost.Disarm();
        Debug.LogError($"{Tag} FAILED: {message}");
        CliHost.Exit(1);
    }
}
