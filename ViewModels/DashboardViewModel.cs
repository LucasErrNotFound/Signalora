using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HotAvalonia;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ShadUI;
using SkiaSharp;
using Signalora.Models;

namespace Signalora.ViewModels;

public partial class DashboardViewModel : ViewModelBase, INavigable
{
    [ObservableProperty] private ObservableCollection<DeviceModel> _connectedDevices = [];
    [ObservableProperty] private DateTime _devicesSelectedDate = DateTime.Today;
    [ObservableProperty] private ISeries[] _devicesSeriesCollection;
    [ObservableProperty] private Axis[] _devicesLineChartXAxes;
    
    private readonly DialogManager _dialogManager;
    private readonly ToastManager _toastManager;
    private readonly PageManager _pageManager;

    public DashboardViewModel(DialogManager dialogManager, ToastManager toastManager, PageManager pageManager)
    {
        _dialogManager = dialogManager;
        _toastManager = toastManager;
        _pageManager = pageManager;
    }

    public DashboardViewModel()
    {
        _dialogManager = new DialogManager();
        _toastManager = new ToastManager();
        _pageManager = new PageManager(new ServiceProvider());
    }

    [AvaloniaHotReload]
    public void Initialize()
    {
        _ = UpdateDevicesConnectedChartAsync();
        GenerateMockDevices();
    }

    private void GenerateMockDevices()
    {
        var random = new Random();
        var deviceNames = new[]
        {
            "Samsung A14 5G", "Apple Watch", "MacBook Pro", "Thinkpad P50",
            "iPhone 15 Pro", "iPad Air", "Google Pixel 8", "Surface Pro",
            "Dell XPS 15", "HP Pavilion", "Asus ROG", "Lenovo Yoga"
        };

        var statuses = new[] { "Active", "Idle", "Offline" };

        ConnectedDevices.Clear();

        for (int i = 0; i < random.Next(4, 10); i++)
        {
            var device = new DeviceModel
            {
                Name = deviceNames[random.Next(deviceNames.Length)],
                IpAddress = $"192.168.1.{random.Next(1, 255)}",
                MacAddress = $"{GenerateRandomHex()}:{GenerateRandomHex()}:{GenerateRandomHex()}:{GenerateRandomHex()}:{GenerateRandomHex()}:{GenerateRandomHex()}",
                Status = statuses[random.Next(statuses.Length)],
                Icon = "\uE167;"
            };
            ConnectedDevices.Add(device);
        }
    }

    private string GenerateRandomHex()
    {
        var random = new Random();
        return random.Next(0, 256).ToString("X2");
    }

    private async Task UpdateDevicesConnectedChartAsync()
    {
        var days = new List<string>();
        var mobileDevices = new List<double>();
        var desktopDevices = new List<double>();
        var random = new Random();

        // Generate data for the last 7 days
        for (int i = 6; i >= 0; i--)
        {
            var date = DevicesSelectedDate.AddDays(-i);
            days.Add(date.ToString("MMM dd"));
            mobileDevices.Add(random.Next(5, 15));
            desktopDevices.Add(random.Next(2, 8));
        }

        DevicesSeriesCollection =
        [
            new LineSeries<double>
            {
                Name = "Mobile",
                Values = mobileDevices,
                ShowDataLabels = false,
                Fill = new SolidColorPaint(SKColors.Coral.WithAlpha(100)),
                Stroke = new SolidColorPaint(SKColors.Coral) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3
            },

            new LineSeries<double>
            {
                Name = "Desktop",
                Values = desktopDevices,
                ShowDataLabels = false,
                Fill = new SolidColorPaint(SKColors.MediumPurple.WithAlpha(100)),
                Stroke = new SolidColorPaint(SKColors.MediumPurple) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3
            }
        ];

        DevicesLineChartXAxes =
        [
            new Axis
            {
                Labels = days.ToArray(),
                LabelsPaint = new SolidColorPaint { Color = SKColors.Gray },
                TextSize = 12,
                MinStep = 1
            }
        ];
    }

    partial void OnDevicesSelectedDateChanged(DateTime value)
    {
        _ = UpdateDevicesConnectedChartAsync();
    }
}