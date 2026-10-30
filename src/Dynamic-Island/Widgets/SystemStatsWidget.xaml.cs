using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Widgets;

public partial class SystemStatsWidget : UserControl
{
    private readonly SystemStatsReader _reader = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public SystemStatsWidget()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Render();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _reader.Read(); // Point de départ des écarts : le premier affichage porte sur la seconde suivante.
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    private void Render()
    {
        var stats = _reader.Read();
        CpuBar.Width = CpuTrack.ActualWidth * stats.CpuPercent / 100;
        CpuText.Text = stats.CpuPercent.ToString("0", CultureInfo.InvariantCulture) + " %";
        MemoryBar.Width = MemoryTrack.ActualWidth * stats.MemoryPercent / 100;
        MemoryText.Text = $"{stats.MemoryUsedGb.ToString("0.0", CultureInfo.InvariantCulture)} / {stats.MemoryTotalGb.ToString("0", CultureInfo.InvariantCulture)} Go";
        NetworkText.Text = $"↓ {FormatSpeed(stats.DownloadBytesPerSecond)}   ↑ {FormatSpeed(stats.UploadBytesPerSecond)}";
    }

    private static string FormatSpeed(double bytesPerSecond) => bytesPerSecond >= 1024 * 1024
        ? (bytesPerSecond / (1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " Mo/s"
        : (bytesPerSecond / 1024).ToString("0", CultureInfo.InvariantCulture) + " Ko/s";
}
