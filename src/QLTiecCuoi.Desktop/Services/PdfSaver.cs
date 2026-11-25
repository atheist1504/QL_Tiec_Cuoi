using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace QLTiecCuoi.Desktop.Services;

/// <summary>Hỏi nơi lưu file PDF, ghi file rồi mở bằng trình xem PDF mặc định.</summary>
public static class PdfSaver
{
    public static async Task SaveAndOpenAsync(Window owner, string fileName, Func<Task<byte[]>> download)
    {
        var dlg = new SaveFileDialog
        {
            FileName = fileName,
            DefaultExt = ".pdf",
            Filter = "Tệp PDF (*.pdf)|*.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            var bytes = await download();
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (ApiException ex)
        {
            MessageBox.Show(owner, ex.Message, "Không xuất được PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (IOException ex)
        {
            MessageBox.Show(owner, $"Không ghi được file: {ex.Message}", "Không xuất được PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
