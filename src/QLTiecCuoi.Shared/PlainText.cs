using System.Text.RegularExpressions;

namespace QLTiecCuoi.Shared;

/// <summary>Đổi câu trả lời dạng markdown của chatbot thành chữ thường để hiện trong khung chat WPF.</summary>
public static partial class PlainText
{
    public static string FromMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text.Replace("\r\n", "\n");
        s = Bold().Replace(s, "$2");                // **đậm** hoặc __đậm__ → đậm
        s = Italic().Replace(s, "$1");              // *nghiêng* → nghiêng (không đụng phép nhân "2 * 3")
        s = Heading().Replace(s, "");               // # Tiêu đề → Tiêu đề
        s = Bullet().Replace(s, "$1• ");            // "* mục" hoặc "- mục" → "• mục"
        s = InlineCode().Replace(s, "$1");          // `mã` → mã
        return s.Trim();
    }

    [GeneratedRegex(@"(\*\*|__)(.+?)\1", RegexOptions.Singleline)]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])\*(?!\s)([^*\n]+?)(?<!\s)\*(?![\w*])")]
    private static partial Regex Italic();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)[*\-+]\s+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCode();
}
