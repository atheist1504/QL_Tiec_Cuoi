namespace QLTiecCuoi.Api.Services;

/// <summary>Đọc số tiền thành chữ tiếng Việt, dùng cho dòng "Bằng chữ" trên hợp đồng.</summary>
public static class VietnameseNumber
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] Units = ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"];

    /// <summary>Ví dụ 124520000 → "Một trăm hai mươi bốn triệu năm trăm hai mươi nghìn đồng".</summary>
    public static string ToMoneyWords(decimal amount)
    {
        var n = (long)decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Không đọc số âm.");
        var words = ToWords(n);
        return char.ToUpper(words[0]) + words[1..] + " đồng";
    }

    public static string ToWords(long n)
    {
        if (n == 0) return "không";

        var groups = new List<int>();
        for (var x = n; x > 0; x /= 1000) groups.Add((int)(x % 1000));
        if (groups.Count > Units.Length) throw new ArgumentOutOfRangeException(nameof(n), "Số quá lớn.");

        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0) continue;
            var isLeading = i == groups.Count - 1;
            var text = ReadGroup(groups[i], full: !isLeading);
            parts.Add(Units[i].Length == 0 ? text : $"{text} {Units[i]}");
        }
        return string.Join(" ", parts);
    }

    /// <summary>Đọc nhóm 3 chữ số; full = đọc cả "không trăm", "linh" khi không phải nhóm đầu.</summary>
    private static string ReadGroup(int g, bool full)
    {
        int h = g / 100, t = g / 10 % 10, u = g % 10;
        var w = new List<string>();

        if (h > 0 || full) w.Add($"{Digits[h]} trăm");

        if (t == 0)
        {
            if (u > 0 && (h > 0 || full)) w.Add("linh");
        }
        else if (t == 1) w.Add("mười");
        else w.Add($"{Digits[t]} mươi");

        if (u > 0)
        {
            w.Add(u switch
            {
                1 when t > 1 => "mốt",
                5 when t > 0 => "lăm",
                _ => Digits[u],
            });
        }
        return string.Join(" ", w);
    }
}
