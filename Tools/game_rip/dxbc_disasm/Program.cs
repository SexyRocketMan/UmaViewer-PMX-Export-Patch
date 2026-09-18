using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Disassemble every DXBC blob in a region of bytes, using the same D3DDisassemble the tools use.
//
// Unity's baked shaders are a blob region with an index at the front and DXBC programs inside. Rather than trust a
// hand-rolled chunk parser, this hands each candidate to D3DDisassemble and lets it decide: it validates the
// container, and the assembly it returns names the constant buffers and resources, which is how the fragment
// shader for a given material can be identified without any guesswork.

internal static class Program
{
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3DDisassemble(IntPtr srcData, IntPtr srcDataSize, uint flags, IntPtr comments,
                                             out IntPtr disassembly);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);

    // minimal ID3DBlob: three IUnknown slots, then GetBufferPointer and GetBufferSize
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        IntPtr GetBufferPointer();
        IntPtr GetBufferSize();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetPointer(IntPtr self);

    private static byte[] Slice(byte[] data, int offset, int length)
    {
        var result = new byte[length];
        Array.Copy(data, offset, result, 0, length);
        return result;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: dxbc_disasm <region.bin> [outDir]");
            return 1;
        }
        byte[] data = File.ReadAllBytes(args[0]);
        string outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetDirectoryName(args[0]) ?? ".", "dxbc");
        Directory.CreateDirectory(outDir);

        int index = 0, written = 0;
        while (true)
        {
            int offset = Find(data, index);
            if (offset < 0) break;
            index = offset + 4;

            // The container declares its own length at offset 24. Slicing to the next DXBC magic instead
            // truncates any container that has a DXBC string inside it, which is why every attempt failed.
            if (offset + 32 > data.Length) continue;
            int declared = BitConverter.ToInt32(data, offset + 24);
            int chunkCount = BitConverter.ToInt32(data, offset + 28);
            if (declared < 64 || declared > 200000 || chunkCount < 1 || chunkCount > 32) continue;
            int length = declared;
            if (offset + length > data.Length) continue;

            byte[] blob = Slice(data, offset, length);
            IntPtr pinned = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, pinned, blob.Length);
                int hr = D3DDisassemble(pinned, (IntPtr)blob.Length, 0, IntPtr.Zero, out IntPtr output);
                if (hr != 0 || output == IntPtr.Zero)
                {
                    Console.WriteLine($"offset {offset}: disassembly failed hr=0x{hr:X8}");
                    continue;
                }
                // Read the blob through its vtable rather than through COM interop, which was landing on the
                // wrong slot: slots 0-2 are QueryInterface/AddRef/Release, so the buffer accessors are 3 and 4.
                IntPtr vtable = Marshal.ReadIntPtr(output);
                var getPointer = Marshal.GetDelegateForFunctionPointer<GetPointer>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
                var getSize = Marshal.GetDelegateForFunctionPointer<GetPointer>(Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size));
                IntPtr pointer = getPointer(output);
                long size = getSize(output).ToInt64();
                string text = Marshal.PtrToStringAnsi(pointer, (int)size) ?? "";

                string path = Path.Combine(outDir, $"blob_{offset:D7}.txt");
                File.WriteAllText(path, text, Encoding.UTF8);
                written++;

                string kind = text.Contains("ps_") ? "pixel" : text.Contains("vs_") ? "vertex" : "other";
                var named = new System.Collections.Generic.List<string>();
                foreach (string name in new[] { "_ToonStep", "_ToonFeather", "_TripleMaskMap", "_ToonMap",
                                                "_MainTex", "_CylinderBlend", "_FaceUp", "_VertexColorToonPower",
                                                "_MaskColorTex", "_OptionMaskMap" })
                {
                    if (text.Contains(name)) named.Add(name);
                }
                Console.WriteLine($"offset {offset:D7}  {kind,-6} {length,6} bytes  -> {Path.GetFileName(path)}"
                                  + (named.Count > 0 ? "   names: " + string.Join(",", named) : ""));
            }
            finally
            {
                Marshal.FreeHGlobal(pinned);
            }
        }
        Console.WriteLine($"{written} blobs disassembled into {outDir}");
        return 0;
    }

    private static int Find(byte[] data, int start)
    {
        for (int i = start; i + 4 <= data.Length; i++)
        {
            if (data[i] == (byte)'D' && data[i + 1] == (byte)'X' && data[i + 2] == (byte)'B' && data[i + 3] == (byte)'C')
                return i;
        }
        return -1;
    }
}


