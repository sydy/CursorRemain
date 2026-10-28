using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CursorTokenCore;

static class RustCore
{
    const string Lib = "cursor_core";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr cr_call(byte[] op, byte[] input);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void cr_free(IntPtr ptr);

    public static JsonElement Call(string op, object input)
    {
        var opBytes = Encoding.UTF8.GetBytes(op + "\0");
        var json = JsonSerializer.Serialize(input);
        var inputBytes = Encoding.UTF8.GetBytes(json + "\0");
        var ptr = cr_call(opBytes, inputBytes);
        if (ptr == IntPtr.Zero) throw new InvalidOperationException("cursor_core 没有返回结果");
        try
        {
            var text = Marshal.PtrToStringUTF8(ptr) ?? "";
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        finally
        {
            cr_free(ptr);
        }
    }

    public static string String(string op, object input) => Call(op, input).GetString() ?? "";

    public static bool Bool(string op, object input) => Call(op, input).GetBoolean();

    public static string[] Strings(string op, object input) =>
        Call(op, input).EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
}
