using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HotAvalonia;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ShadUI;
using SkiaSharp;
using Signalora.Models;
using Signalora.Services;
using Signalora.Services.Interface;

namespace Signalora.ViewModels;

public partial class DashboardViewModel : ViewModelBase, INavigable
{
    [ObservableProperty] private ObservableCollection<DeviceModel> _connectedDevices = new();
    [ObservableProperty] private DateTime _devicesSelectedDate = DateTime.Today;
    [ObservableProperty] private ISeries[] _devicesSeriesCollection;
    [ObservableProperty] private Axis[] _devicesLineChartXAxes;
    [ObservableProperty] private int _totalDevices;
    [ObservableProperty] private int _activeDevices;
    [ObservableProperty] private int _inactiveDevices;
    [ObservableProperty] private double _currentBandwidth;
    [ObservableProperty] private int _signalQuality;
    [ObservableProperty] private string _securityStatus = "Secure";
    [ObservableProperty] private ObservableCollection<ActivityLog> _recentActivities = new();
    
    private readonly DialogManager _dialogManager;
    private readonly ToastManager _toastManager;
    private readonly PageManager _pageManager;
    private readonly INetworkScanner _networkScanner;
    private readonly Dictionary<DateTime, List<DeviceModel>> _deviceHistory = new();

    public DashboardViewModel(DialogManager dialogManager, ToastManager toastManager, 
        PageManager pageManager, INetworkScanner networkScanner)
    {
        _dialogManager = dialogManager;
        _toastManager = toastManager;
        _pageManager = pageManager;
        _networkScanner = networkScanner;
    }

    public DashboardViewModel()
    {
        _dialogManager = new DialogManager();
        _toastManager = new ToastManager();
        _pageManager = new PageManager(new ServiceProvider());
        _networkScanner = new NetworkScanner();
    }

    [AvaloniaHotReload]
    public void Initialize()
    {
        _ = LoadDashboardDataAsync();
        StartMonitoring();
    }

    private async Task LoadDashboardDataAsync()
    {
        try
        {
            var devices = await _networkScanner.ScanNetworkAsync();
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ConnectedDevices.Clear();
                foreach (var device in devices)
                {
                    ConnectedDevices.Add(device);
                }
                
                UpdateStatistics();
                SaveDeviceSnapshot();
                _ = UpdateDevicesConnectedChartAsync();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading dashboard data: {ex.Message}");
        }
    }

    private void StartMonitoring()
    {
        _networkScanner.StartMonitoring((device, changeType) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                switch (changeType)
                {
                    case DeviceChangeType.Connected:
                        // Check if device already exists using MAC address
                        var existingByMac = ConnectedDevices.FirstOrDefault(d => d.MacAddress == device.MacAddress);
                        if (existingByMac == null)
                        {
                            ConnectedDevices.Add(device);
                            AddActivityLog("Connected", device.Name, "success");
                            
                            _toastManager
                                .CreateToast("Device Connected")
                                .WithContent($"{device.Name} joined the network")
                                .DismissOnClick()
                                .ShowSuccess();
                        }
                        break;

                    case DeviceChangeType.Disconnected:
                        var disconnected = ConnectedDevices.FirstOrDefault(d => d.MacAddress == device.MacAddress);
                        if (disconnected != null)
                        {
                            disconnected.Status = "Disconnected";
                            AddActivityLog("Disconnected", device.Name, "error");
                        }
                        break;

                    case DeviceChangeType.Updated:
                        var existing = ConnectedDevices.FirstOrDefault(d => d.MacAddress == device.MacAddress);
                        if (existing != null)
                        {
                            // Update properties without recreating the object
                            existing.SignalStrength = device.SignalStrength;
                            existing.Status = device.Status;
                            existing.IpAddress = device.IpAddress;
                            existing.Name = device.Name;
                        }
                        break;
                }
                
                UpdateStatistics();
            });
        });
    }

    private void UpdateStatistics()
    {
        TotalDevices = ConnectedDevices.Count;
        ActiveDevices = ConnectedDevices.Count(d => d.Status == "Active");
        InactiveDevices = ConnectedDevices.Count(d => d.Status != "Active");
        
        // Calculate average bandwidth (simulated)
        CurrentBandwidth = ActiveDevices * 5.5; // Rough estimate
        
        // Calculate signal quality based on device signal strengths
        var excellentCount = ConnectedDevices.Count(d => d.SignalStrength == "Excellent");
        var goodCount = ConnectedDevices.Count(d => d.SignalStrength == "Good");
        var fairCount = ConnectedDevices.Count(d => d.SignalStrength == "Fair");
        
        if (TotalDevices > 0)
        {
            SignalQuality = (int)((excellentCount * 100 + goodCount * 75 + fairCount * 50) / (double)TotalDevices);
        }
        else
        {
            SignalQuality = 0;
        }
    }

    private void SaveDeviceSnapshot()
    {
        var today = DateTime.Today;
        if (!_deviceHistory.ContainsKey(today))
        {
            _deviceHistory[today] = new List<DeviceModel>();
        }
        
        _deviceHistory[today] = ConnectedDevices.ToList();
    }

    private async Task UpdateDevicesConnectedChartAsync()
    {
        await Task.Run(() =>
        {
            var days = new List<string>();
            var mobileDevices = new List<double>();
            var desktopDevices = new List<double>();

            // Generate data for the last 7 days
            for (int i = 6; i >= 0; i--)
            {
                var date = DevicesSelectedDate.AddDays(-i);
                days.Add(date.ToString("MMM dd"));

                if (_deviceHistory.ContainsKey(date.Date))
                {
                    var devices = _deviceHistory[date.Date];
                    mobileDevices.Add(devices.Count(d => d.Category == "Phone" || d.Category == "Tablet"));
                    desktopDevices.Add(devices.Count(d => d.Category == "Desktop" || d.Category == "Laptop"));
                }
                else
                {
                    // For dates without data, use current ratio
                    var mobileRatio = ConnectedDevices.Count > 0 
                        ? (double)ConnectedDevices.Count(d => d.Category == "Phone" || d.Category == "Tablet") / ConnectedDevices.Count 
                        : 0.6;
                    var desktopRatio = ConnectedDevices.Count > 0 
                        ? (double)ConnectedDevices.Count(d => d.Category == "Desktop" || d.Category == "Laptop") / ConnectedDevices.Count 
                        : 0.4;
                    
                    var estimatedTotal = Math.Max(3, ConnectedDevices.Count * 0.8);
                    mobileDevices.Add(Math.Round(estimatedTotal * mobileRatio));
                    desktopDevices.Add(Math.Round(estimatedTotal * desktopRatio));
                }
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
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
            });
        });
    }

    private void AddActivityLog(string action, string deviceName, string type)
    {
        var activity = new ActivityLog
        {
            Action = action,
            DeviceName = deviceName,
            Timestamp = DateTime.Now,
            Type = type
        };

        RecentActivities.Insert(0, activity);
        
        // Keep only the last 10 activities
        while (RecentActivities.Count > 10)
        {
            RecentActivities.RemoveAt(RecentActivities.Count - 1);
        }
    }

    partial void OnDevicesSelectedDateChanged(DateTime value)
    {
        _ = UpdateDevicesConnectedChartAsync();
    }

    protected override void DisposeManagedResources()
    {
        _networkScanner.StopMonitoring();
        base.DisposeManagedResources();
    }
}

public class ActivityLog
{
    public string Action { get; set; }
    public string DeviceName { get; set; }
    public DateTime Timestamp { get; set; }
    public string Type { get; set; }
    public string TimeAgo => GetTimeAgo();

    private string GetTimeAgo()
    {
        var timeSpan = DateTime.Now - Timestamp;
        
        if (timeSpan.TotalSeconds < 60)
            return "Just now";
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes} min ago";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours} hr ago";
        
        return Timestamp.ToString("MMM dd, HH:mm");
    }
}