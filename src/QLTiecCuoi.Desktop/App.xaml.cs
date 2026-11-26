using System.Windows;
using System.Windows.Media.Imaging;

namespace QLTiecCuoi.Desktop;

public partial class App : Application
{
    private static readonly BitmapFrame AppIcon =
        BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));

    protected override void OnStartup(StartupEventArgs e)
    {
        // Mọi cửa sổ (lịch sảnh, báo giá, hợp đồng, chat...) dùng chung icon ứng dụng.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window { Icon: null } w) w.Icon = AppIcon;
            }));
        base.OnStartup(e);
    }
}
