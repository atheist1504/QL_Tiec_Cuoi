using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Shared;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QLTiecCuoi.Api.Services;

/// <summary>Thông tin bên A (nhà hàng) in trên hợp đồng, báo giá.</summary>
public class RestaurantOptions
{
    public string Name { get; set; } = "Nhà hàng tiệc cưới (tên demo)";
    public string Address { get; set; } = "(địa chỉ nhà hàng)";
    public string Phone { get; set; } = "(số điện thoại)";
    public string Representative { get; set; } = "(người đại diện)";
}

/// <summary>Xuất báo giá và hợp đồng ra PDF bằng thư viện QuestPDF (bản Community, miễn phí).</summary>
public class PdfService(AppDbContext db, ContractService contracts, TimeProvider clock, RestaurantOptions restaurant)
{
    private static readonly CultureInfo Vi = new("vi-VN");

    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<(byte[] Pdf, string FileName)> QuotePdfAsync(int quoteId)
    {
        var q = await db.Quotes.Include(x => x.Hall).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == quoteId)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy báo giá.");
        var code = $"BG-{q.Id:00000}";

        var pdf = Build(page =>
        {
            page.Content().Column(col =>
            {
                col.Spacing(6);
                Letterhead(col);
                col.Item().PaddingTop(10).AlignCenter().Text("BẢNG BÁO GIÁ TIỆC CƯỚI").FontSize(16).Bold();
                col.Item().AlignCenter().Text($"Số: {code} · Ngày lập: {Local(q.CreatedAt):dd/MM/yyyy} · Hiệu lực đến: {q.ValidUntil:dd/MM/yyyy}").FontSize(10);
                col.Item().PaddingTop(8).Text(t =>
                {
                    t.Span("Kính gửi: ").Bold();
                    t.Span($"{q.CustomerName} – Điện thoại {q.CustomerPhone}");
                });
                col.Item().Text($"Tiệc cưới tại {q.Hall.Name}, ca {Labels.Of(q.Shift).ToLower()} {DayText(q.EventDate)}, {q.Tables} bàn (10 khách/bàn).");
                LinesTable(col, q);
                col.Item().PaddingTop(6).Text(t =>
                {
                    t.Span("Bằng chữ: ").Bold();
                    t.Span(VietnameseNumber.ToMoneyWords(q.Total)).Italic();
                });
                if (q.Note is not null) col.Item().Text($"Ghi chú: {q.Note}");
                col.Item().PaddingTop(8).Text("Báo giá chưa có giá trị giữ sảnh. Sảnh chỉ được chốt khi hai bên ký hợp đồng và Quý khách đặt cọc.")
                    .FontSize(10).Italic();
            });
        });
        return (pdf, $"{code}.pdf");
    }

    public async Task<(byte[] Pdf, string FileName)> ContractPdfAsync(int contractId)
    {
        var c = await contracts.LoadAsync(contractId);
        var q = c.Quote;
        var paid = c.Payments.Sum(p => p.Amount);
        var signed = Local(c.CreatedAt);

        var pdf = Build(page =>
        {
            page.Content().Column(col =>
            {
                col.Spacing(5);
                col.Item().AlignCenter().Text("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM").Bold();
                col.Item().AlignCenter().Text("Độc lập – Tự do – Hạnh phúc").Bold().Underline();
                col.Item().PaddingTop(12).AlignCenter().Text("HỢP ĐỒNG DỊCH VỤ TIỆC CƯỚI").FontSize(16).Bold();
                col.Item().AlignCenter().Text($"Số: {c.Code}");
                col.Item().PaddingTop(6).Text($"Hôm nay, ngày {signed:dd} tháng {signed:MM} năm {signed:yyyy}, chúng tôi gồm:");

                col.Item().PaddingTop(4).Text("BÊN A (Bên cung cấp dịch vụ):").Bold();
                Field(col, "Tên", restaurant.Name);
                Field(col, "Địa chỉ", restaurant.Address);
                Field(col, "Điện thoại", restaurant.Phone);
                Field(col, "Đại diện", restaurant.Representative);

                col.Item().PaddingTop(4).Text("BÊN B (Khách hàng):").Bold();
                Field(col, "Họ tên", c.CustomerName);
                Field(col, "Điện thoại", c.CustomerPhone);
                Field(col, "Địa chỉ", c.CustomerAddress ?? "........................................................");
                Field(col, "Số CCCD", c.CustomerIdNumber ?? "........................................................");

                col.Item().PaddingTop(6).Text("Hai bên thống nhất ký hợp đồng với các điều khoản sau:");

                Article(col, "Điều 1. Nội dung dịch vụ");
                col.Item().Text($"Bên A tổ chức tiệc cưới cho Bên B tại {c.Booking.Hall.Name}, ca {Labels.Of(c.Booking.Shift).ToLower()} {DayText(c.Booking.EventDate)}, số lượng {q.Tables} bàn (10 khách/bàn), theo thực đơn và dịch vụ tại Điều 2.");

                Article(col, "Điều 2. Chi tiết và giá trị hợp đồng");
                LinesTable(col, q);
                col.Item().Text(t =>
                {
                    t.Span("Bằng chữ: ").Bold();
                    t.Span(VietnameseNumber.ToMoneyWords(c.Total)).Italic();
                });

                Article(col, "Điều 3. Đặt cọc và thanh toán");
                col.Item().Text($"1. Bên B đặt cọc {c.DepositPercent:0.##}% giá trị hợp đồng: {Labels.Money(c.DepositRequired)} ({VietnameseNumber.ToMoneyWords(c.DepositRequired)}).");
                col.Item().Text("2. Phần còn lại được thanh toán khi thanh lý hợp đồng ngay sau tiệc, tính theo số bàn thực tế và các khoản phát sinh (nếu có).");
                col.Item().Text($"3. Đến thời điểm in hợp đồng, Bên B đã thanh toán {Labels.Money(paid)}; còn lại {Labels.Money(c.Total - paid)}.");

                Article(col, "Điều 4. Trách nhiệm của hai bên");
                col.Item().Text("1. Bên B chốt thực đơn và số bàn chậm nhất 7 ngày trước ngày tổ chức; thay đổi sau thời hạn này được lập phụ lục hợp đồng.");
                col.Item().Text("2. Bên A bảo đảm sảnh, thực đơn và dịch vụ đúng như Điều 1 và Điều 2.");
                col.Item().Text("3. Hợp đồng lập thành 02 bản, mỗi bên giữ 01 bản có giá trị như nhau.");
                if (c.Note is not null) col.Item().Text($"Ghi chú: {c.Note}");

                col.Item().PaddingTop(16).PaddingBottom(70).Row(row =>
                {
                    row.RelativeItem().AlignCenter().Column(s =>
                    {
                        s.Item().AlignCenter().Text("ĐẠI DIỆN BÊN A").Bold();
                        s.Item().AlignCenter().Text("(Ký, ghi rõ họ tên)").Italic().FontSize(10);
                    });
                    row.RelativeItem().AlignCenter().Column(s =>
                    {
                        s.Item().AlignCenter().Text("ĐẠI DIỆN BÊN B").Bold();
                        s.Item().AlignCenter().Text("(Ký, ghi rõ họ tên)").Italic().FontSize(10);
                    });
                });
            });
        });
        return (pdf, $"{c.Code}.pdf");
    }

    private static byte[] Build(Action<PageDescriptor> content) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11).LineHeight(1.25f));
            content(page);
            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Trang ").FontSize(9);
                t.CurrentPageNumber().FontSize(9);
                t.Span(" / ").FontSize(9);
                t.TotalPages().FontSize(9);
            });
        })).GeneratePdf();

    private void Letterhead(ColumnDescriptor col)
    {
        col.Item().Text(restaurant.Name).FontSize(13).Bold();
        col.Item().Text($"{restaurant.Address} · {restaurant.Phone}").FontSize(10);
        col.Item().LineHorizontal(0.5f);
    }

    private static void Field(ColumnDescriptor col, string label, string value) =>
        col.Item().PaddingLeft(12).Text(t =>
        {
            t.Span($"{label}: ");
            t.Span(value);
        });

    private static void Article(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(6).Text(title).Bold();

    /// <summary>Bảng chi tiết: tiền sảnh, thực đơn, dịch vụ, cộng, VAT, tổng.</summary>
    private static void LinesTable(ColumnDescriptor col, Quote q)
    {
        var rows = new List<(string Name, decimal Price, string Qty, decimal Amount)>
        {
            ($"Thuê {q.Hall.Name}, ca {Labels.Of(q.Shift).ToLower()}", q.HallPrice, "1", q.HallPrice),
        };
        rows.AddRange(q.Lines.OrderBy(l => l.Kind).ThenBy(l => l.Id)
            .Select(l => (l.Name, l.UnitPrice, $"{l.Quantity} {l.Unit}", l.Amount)));

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(28);
                c.RelativeColumn(5);
                c.RelativeColumn(2);
                c.RelativeColumn(1.5f);
                c.RelativeColumn(2.2f);
            });

            static IContainer Cell(IContainer x) => x.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3).PaddingHorizontal(2);
            table.Header(h =>
            {
                foreach (var title in new[] { "STT", "Hạng mục", "Đơn giá", "SL", "Thành tiền" })
                    h.Cell().Element(Cell).Background(Colors.Grey.Lighten3).Text(title).Bold();
            });

            var i = 0;
            foreach (var r in rows)
            {
                table.Cell().Element(Cell).Text((++i).ToString());
                table.Cell().Element(Cell).Text(r.Name);
                table.Cell().Element(Cell).AlignRight().Text(Labels.Money(r.Price));
                table.Cell().Element(Cell).Text(r.Qty);
                table.Cell().Element(Cell).AlignRight().Text(Labels.Money(r.Amount));
            }

            void Total(string label, decimal value, bool bold = false)
            {
                table.Cell().ColumnSpan(4).Element(Cell).AlignRight().Text(t => { var s = t.Span(label); if (bold) s.Bold(); });
                table.Cell().Element(Cell).AlignRight().Text(t => { var s = t.Span(Labels.Money(value)); if (bold) s.Bold(); });
            }
            Total("Cộng trước thuế", q.Subtotal);
            Total($"VAT {q.VatPercent:0.##}%", q.VatAmount);
            Total("TỔNG CỘNG", q.Total, bold: true);
        });
    }

    private DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone);

    private static string DayText(DateOnly d) => $"{Vi.DateTimeFormat.GetDayName(d.DayOfWeek)}, ngày {d:dd/MM/yyyy}";
}
