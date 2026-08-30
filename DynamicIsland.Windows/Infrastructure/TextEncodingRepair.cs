using System.Text;

namespace DynamicIsland.Windows.Infrastructure;

internal static class TextEncodingRepair
{
    private static readonly Encoding Windows1252;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    static TextEncodingRepair()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public static string RepairUtf8ReadAsWindows1252(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var repaired = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (TryDecodeSequence(value, index, out var decoded, out var consumed))
            {
                repaired.Append(decoded);
                index += consumed;
            }
            else
            {
                repaired.Append(value[index]);
                index++;
            }
        }

        return repaired.ToString();
    }

    private static bool TryDecodeSequence(string value, int index, out string decoded, out int consumed)
    {
        decoded = string.Empty;
        consumed = 0;

        byte first;
        try { first = Windows1252.GetBytes(value.Substring(index, 1))[0]; }
        catch (EncoderFallbackException) { return false; }

        var length = first switch
        {
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 0
        };
        if (length == 0 || index + length > value.Length) return false;

        byte[] bytes;
        try { bytes = Windows1252.GetBytes(value.Substring(index, length)); }
        catch (EncoderFallbackException) { return false; }
        if (bytes.Skip(1).Any(valueByte => valueByte is < 0x80 or > 0xBF)) return false;

        try
        {
            decoded = StrictUtf8.GetString(bytes);
            consumed = length;
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }
}
