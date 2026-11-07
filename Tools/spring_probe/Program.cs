// Drives the game's own spring solver (CySpringPlugin.dll) outside the game, on a chain we build.
//
// The point is not to reimplement anything: it is to find out whether the shipped plugin runs standalone
// and, once it does, to have the reference the Blender side can be compared against.
//
// Two things are exercised:
//   * the exported NativeClothUpdate, called for a few frames on a five-bone chain with the numbers the
//     tail actually carries (stiffness 170..250, drag 200..1000, gravity 50), and
//   * the same input through the managed port in `CySpringNative.UpdateNativeCloth`, which is the copy
//     the viewer ships. Printing both shows whether they agree, and either one can be the reference.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

internal static class Program
{
    private const string DefaultDll =
        @"C:\Users\user\Umamusume\umamusume_Data\Plugins\x86_64\CySpringPlugin.dll";

    private const int Bones = 5;
    private const float LinkLength = 0.25f;
    private const int Frames = 30;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ClothUpdate(IntPtr cond, int nCond, IntPtr collisions, IntPtr clothParams,
                                      IntPtr parentWorldPosition, IntPtr parentWorldRotation);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string mode = args.Length > 0 ? args[0] : "managed";
        string dll = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : DefaultDll;
        string tracePath = OptionValue(args, "--trace");

        Console.WriteLine($"struct sizes: working={Marshal.SizeOf<NativeClothWorking>()} "
                          + $"collision={Marshal.SizeOf<NativeClothCollision>()} "
                          + $"param={Marshal.SizeOf<NativeClothParameter>()}");

        if (mode == "managed" || mode == "both")
        {
            Console.WriteLine();
            Console.WriteLine("== the viewer's managed port (the game's own equations), 120 frames, no colliders");
            RunManaged(120, 0.0f, "no-collider");
            Console.WriteLine();
            Console.WriteLine("== the same, with a sphere collider the chain would otherwise pass through");
            RunManaged(120, 0.6f, "sphere-collider");
        }

        if (!string.IsNullOrEmpty(tracePath))
        {
            WriteTrace(tracePath);
            Console.WriteLine();
            Console.WriteLine($"trace written to {tracePath} "
                              + $"({Runs.Count} run(s), {Runs.Sum(r => r.Frames.Count)} frame(s))");
        }

        if (mode == "native" || mode == "both")
        {
            Console.WriteLine();
            Console.WriteLine("== native CySpringPlugin.dll");
            // Known outcome on this install: the plugin's DllMain refuses to initialise outside the game
            // (0x8007045A), and skipping it with DONT_RESOLVE_DLL_REFERENCES makes the exported function
            // fault. The native solver is therefore not reachable from a foreign process as shipped.
            if (!File.Exists(dll))
            {
                Console.Error.WriteLine($"no plugin at {dll}");
                return 2;
            }

            IntPtr library;
            try
            {
                library = NativeLibrary.Load(dll);
            }
            catch (Exception error)
            {
                Console.WriteLine($"plain load failed: {error.Message}");
                library = LoadWithoutInit(dll);
                if (library == IntPtr.Zero)
                {
                    Console.Error.WriteLine("DONT_RESOLVE_DLL_REFERENCES load failed too");
                    return 2;
                }
                Console.WriteLine("loaded with DONT_RESOLVE_DLL_REFERENCES (DllMain skipped) - the call below "
                                  + "is expected to fault, and that is the finding");
            }

            IntPtr symbol = NativeLibrary.GetExport(library, "NativeClothUpdate");
            Console.WriteLine($"loaded {Path.GetFileName(dll)}, NativeClothUpdate at 0x{symbol.ToInt64():X}");
            var clothUpdate = Marshal.GetDelegateForFunctionPointer<ClothUpdate>(symbol);
            Console.WriteLine($"exports: {string.Join(", ", Exports(library))}");
            RunNative(clothUpdate, 30, 0.0f, "native");
        }

        return 0;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private const uint DontResolveDllReferences = 0x00000001;

    private static IntPtr LoadWithoutInit(string path)
    {
        IntPtr module = LoadLibraryExW(path, IntPtr.Zero, DontResolveDllReferences);
        if (module == IntPtr.Zero)
        {
            Console.Error.WriteLine($"LoadLibraryEx failed, win32 error {Marshal.GetLastWin32Error()}");
        }
        return module;
    }

    private static string[] Exports(IntPtr library)
    {
        // the three names read out of the PE export table earlier; report which of them resolve
        var names = new[] { "NativeClothUpdate", "NativeClothSkirtUpdate", "NativeSkirtUpdate" };
        var found = new System.Collections.Generic.List<string>();
        foreach (string name in names)
        {
            if (NativeLibrary.TryGetExport(library, name, out _)) found.Add(name);
        }
        return found.ToArray();
    }

    /// <summary>A five-bone chain hanging from a hip: the shape and numbers of tail0001</summary>
    private static void BuildChain(out NativeClothWorking[] bones, out NativeClothCollision[] collisions,
                                   out NativeClothParameter[] parameters,
                                   out Vector3[] parentPosition, out Quaternion[] parentRotation,
                                   float colliderRadius)
    {
        float[] stiffness = { 170f, 190f, 210f, 230f, 250f };
        float[] drag = { 200f, 400f, 600f, 800f, 1000f };
        float[] radius = { 0.03f, 0.05f, 0.075f, 0.075f, 0.075f };

        bones = new NativeClothWorking[Bones];
        for (int i = 0; i < Bones; i++)
        {
            var position = new Vector3(0f, 1.5f - i * LinkLength, 0f);
            bones[i] = new NativeClothWorking
            {
                _initLocalRotation = Quaternion.identity,
                _oldRotation = Quaternion.identity,
                _finalRotation = Quaternion.identity,
                _boneAxis = new Vector3(0f, -1f, 0f),
                _currentPosition = position,
                _prevPosition = position,
                _oldPosition = i == 0 ? new Vector3(0f, 1.5f + LinkLength, 0f) : new Vector3(0f, 1.5f - (i - 1) * LinkLength, 0f),
                _initBoneDistance = LinkLength,
                _stiffnessForce = stiffness[i],
                _dragForce = drag[i],
                _collisionRadius = radius[i],
                _gravity = 50f,
                _capability = CySpring.eCapability.None,
                _capsGroupIndex = 0,
                _activeCollision = 0,
                _existCollision = false,
                _isSkip = false,
                _wind = 0,
                _windGroupIndex = 0f,
                _windPower = new Vector3(0f, 0f, 0f),
                _springForce = new Vector3(0f, 0f, 0f),
            };
        }

        // one sphere around the hip, big enough that the lower bones would pass through it
        collisions = new NativeClothCollision[1];
        collisions[0] = new NativeClothCollision
        {
            _type = 0,                                  // CollisionType.Sphere
            _position = new Vector3(0f, -0.5f, 0f),
            _position2 = new Vector3(0f, 0.5f, 0f),
            _radius = colliderRadius,
            _parentIndex = 0,
            _purpose = 0,
        };
        if (colliderRadius > 0f)
        {
            for (int i = 0; i < Bones; i++)
            {
                bones[i]._activeCollision = 1;
                bones[i]._cIndex0 = 0;
                bones[i]._existCollision = true;
            }
        }

        parameters = new NativeClothParameter[1];
        parameters[0] = new NativeClothParameter
        {
            _stiffnessForceRate = 1f,
            _dragForceRate = 1f,
            _gravityRate = 1f,
            _collisionSwitch = true,
            _capability0 = CySpring.eCapability.None,
            _capability1 = CySpring.eCapability.None,
            _capability2 = CySpring.eCapability.None,
            _capability3 = CySpring.eCapability.None,
            _capability4 = CySpring.eCapability.None,
            _gravityDirection0 = new Vector3(0f, -1f, 0f),
            _gravityDirection1 = new Vector3(0f, -1f, 0f),
            _gravityDirection2 = new Vector3(0f, -1f, 0f),
            _gravityDirection3 = new Vector3(0f, -1f, 0f),
            _gravityDirection4 = new Vector3(0f, -1f, 0f),
            _forceScale = new Vector3(1f, 1f, 1f),
        };

        parentPosition = new[] { new Vector3(0f, 1.5f + LinkLength, 0f) };
        parentRotation = new[] { Quaternion.identity };
    }

    private static void RunNative(ClothUpdate call, int frames, float colliderRadius, string label)
    {
        BuildChain(out NativeClothWorking[] bones, out NativeClothCollision[] collisions,
                   out NativeClothParameter[] parameters, out Vector3[] parentPosition,
                   out Quaternion[] parentRotation, colliderRadius);

        Print(label, 0, bones);

        int workingSize = Marshal.SizeOf<NativeClothWorking>();
        int collisionSize = Marshal.SizeOf<NativeClothCollision>();
        int parameterSize = Marshal.SizeOf<NativeClothParameter>();
        IntPtr working = Marshal.AllocHGlobal(workingSize * bones.Length);
        IntPtr collisionPtr = Marshal.AllocHGlobal(collisionSize * collisions.Length);
        IntPtr parameterPtr = Marshal.AllocHGlobal(parameterSize * parameters.Length);
        IntPtr positionPtr = Marshal.AllocHGlobal(12 * parentPosition.Length);
        IntPtr rotationPtr = Marshal.AllocHGlobal(16 * parentRotation.Length);

        try
        {
            for (int frame = 1; frame <= frames; frame++)
            {
                WriteArray(working, bones);
                WriteArray(collisionPtr, collisions);
                WriteArray(parameterPtr, parameters);
                WriteVec3Array(positionPtr, parentPosition);
                WriteQuatArray(rotationPtr, parentRotation);

                call(working, bones.Length, collisionPtr, parameterPtr, positionPtr, rotationPtr);

                bones = ReadArray<NativeClothWorking>(working, bones.Length);
                if (frame % 10 == 0 || frame == 1) Print(label, frame, bones);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(working);
            Marshal.FreeHGlobal(collisionPtr);
            Marshal.FreeHGlobal(parameterPtr);
            Marshal.FreeHGlobal(positionPtr);
            Marshal.FreeHGlobal(rotationPtr);
        }
    }

    private static void RunManaged(int frames, float colliderRadius, string traceName)
    {
        BuildChain(out NativeClothWorking[] bones, out NativeClothCollision[] collisions,
                   out NativeClothParameter[] parameters, out Vector3[] parentPosition,
                   out Quaternion[] parentRotation, colliderRadius);
        var run = new Run { Name = traceName, ColliderRadius = colliderRadius };
        Runs.Add(run);
        Record(run, bones);
        Print("managed", 0, bones);
        for (int frame = 1; frame <= frames; frame++)
        {
            CySpringNative.UpdateNativeCloth(bones, collisions, parameters, parentPosition, parentRotation);
            Record(run, bones);
            if (frame % 20 == 0 || frame == 1) Print("managed", frame, bones);
        }
        // a collider's position is relative to the bone it is attached to, which is what parentWorldPosition holds
        CheckInvariants("managed", bones, colliderRadius, collisions[0]._position + parentPosition[0], out string report);
        Console.WriteLine("  " + report);
    }

    private static void Print(string label, int frame, NativeClothWorking[] bones)
    {
        var text = new StringBuilder($"  {label,-16} f{frame,-4}");
        for (int i = 0; i < bones.Length; i++)
        {
            Vector3 p = bones[i]._currentPosition;
            text.Append($" [{i}] {p.x,7:F3} {p.y,7:F3} {p.z,7:F3} |");
        }
        Console.WriteLine(text.ToString());
    }

    /// <summary>
    /// The invariants a working spring has to keep, printed rather than asserted: every link stays at its
    /// rest length, nothing goes NaN, and a collider the chain would pass through pushes it out.
    /// </summary>
    private static bool CheckInvariants(string label, NativeClothWorking[] bones, float colliderRadius,
                                        Vector3 colliderCentre, out string report)
    {
        var text = new StringBuilder();
        bool ok = true;

        for (int i = 1; i < bones.Length; i++)
        {
            float length = Vector3.Distance(bones[i]._currentPosition, bones[i - 1]._currentPosition);
            bool inRange = Math.Abs(length - LinkLength) < 1e-3f;
            ok &= inRange;
            text.Append($" link{i}={length:F4}{(inRange ? "" : " (!)")}");
        }

        foreach (NativeClothWorking bone in bones)
        {
            Vector3 p = bone._currentPosition;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
                || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
            {
                ok = false;
                text.Append(" NaN/Inf (!)");
                break;
            }
        }

        if (colliderRadius > 0f)
        {
            float clearance = float.MaxValue;
            foreach (NativeClothWorking bone in bones)
            {
                clearance = Math.Min(clearance,
                    Vector3.Distance(bone._currentPosition, colliderCentre) - colliderRadius - bone._collisionRadius);
            }
            // Reported, not asserted: resolving a collision and then re-projecting the bone onto its
            // parent's rest-length sphere can leave it slightly inside (the geometry is over-constrained
            // when the collider is this large), so the number is meant to be compared against the same run
            // with the collider disabled - penetration must drop a lot, it does not have to reach zero.
            text.Append($" penetration={-clearance:F4}");
        }

        report = $"{label}:{(ok ? " ok" : " FAILED")}{text}";
        return ok;
    }

    /// <summary>One run of the solver: the frames it produced, so another implementation can be held to them.</summary>
    private sealed class Run
    {
        public string Name;
        public float ColliderRadius;
        public readonly List<string> Frames = new List<string>();
    }

    private static readonly List<Run> Runs = new List<Run>();

    private static void Record(Run run, NativeClothWorking[] bones)
    {
        var frame = new StringBuilder("[");
        for (int i = 0; i < bones.Length; i++)
        {
            Vector3 p = bones[i]._currentPosition;
            Quaternion q = bones[i]._finalRotation;
            if (i > 0) frame.Append(',');
            frame.Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append(',')
                 .Append(F(q.x)).Append(',').Append(F(q.y)).Append(',').Append(F(q.z)).Append(',')
                 .Append(F(q.w));
        }
        frame.Append(']');
        run.Frames.Add(frame.ToString());
    }

    /// <summary>Round-trippable for a float, so a comparison is not decided by the printer.</summary>
    private static string F(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static void WriteTrace(string path)
    {
        var json = new StringBuilder();
        json.Append("{\"scenario\":\"synthetic-tail\",\"linkLength\":");
        json.Append(F(LinkLength)).Append(",\"bones\":").Append(Bones).Append(",\"runs\":[");
        for (int r = 0; r < Runs.Count; r++)
        {
            if (r > 0) json.Append(',');
            Run run = Runs[r];
            json.Append("{\"name\":\"").Append(run.Name).Append("\",\"colliderRadius\":")
                .Append(F(run.ColliderRadius)).Append(",\"frames\":[")
                .Append(string.Join(",", run.Frames)).Append("]}");
        }
        json.Append("]}");
        File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
    }

    private static string OptionValue(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static void WriteArray<T>(IntPtr target, T[] source) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        for (int i = 0; i < source.Length; i++)
        {
            Marshal.StructureToPtr(source[i], target + i * size, false);
        }
    }

    private static T[] ReadArray<T>(IntPtr source, int count) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        var result = new T[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = Marshal.PtrToStructure<T>(source + i * size);
        }
        return result;
    }

    private static void WriteVec3Array(IntPtr target, Vector3[] source)
    {
        for (int i = 0; i < source.Length; i++)
        {
            Marshal.StructureToPtr(source[i], target + i * 12, false);
        }
    }

    private static void WriteQuatArray(IntPtr target, Quaternion[] source)
    {
        for (int i = 0; i < source.Length; i++)
        {
            Marshal.StructureToPtr(source[i], target + i * 16, false);
        }
    }
}

