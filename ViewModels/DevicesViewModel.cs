using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotAvalonia;
using ShadUI;
using Signalora.Models;
using Signalora.Services;
using Signalora.Services.Interface;

namespace Signalora.ViewModels;

public partial class DevicesViewModel : ViewModelBase, INavigable
{
    [ObservableProperty] private ObservableCollection<DeviceModel> _devices = new();
    [ObservableProperty] private ObservableCollection<DeviceModel> _filteredDevices = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private int _totalDevices;
    [ObservableProperty] private int _activeDevices;
    [ObservableProperty] private int _inactiveDevices;
    
    private readonly DialogManager _dialogManager;
    private readonly ToastManager _toastManager;
    private readonly PageManager _pageManager;
    private readonly INetworkScanner _networkScanner;
    private Timer _autoScanTimer;
    private bool _isInitialized = false;

    // Events to notify other ViewModels about device changes
    public event Action<ObservableCollection<DeviceModel>> DevicesUpdated;
    public event Action<DeviceModel, DeviceChangeType> DeviceChanged;

    public ObservableCollection<string> FilterOptions { get; } = new()
    {
        "All", "Active", "Disconnected", "Phone", "Laptop", "Desktop", 
        "Tablet", "TV", "Printer", "Camera", "Speaker", "Wearable"
    };

    public DevicesViewModel(
        DialogManager dialogManager, 
        ToastManager toastManager, 
        PageManager pageManager,
        INetworkScanner networkScanner)
    {
        _dialogManager = dialogManager;
        _toastManager = toastManager;
        _pageManager = pageManager;
        _networkScanner = networkScanner;
    }

    public DevicesViewModel()
    {
        _dialogManager = new DialogManager();
        _toastManager = new ToastManager();
        _pageManager = new PageManager(new ServiceProvider());
        _networkScanner = new NetworkScanner();
    }

    [AvaloniaHotReload]
    public void Initialize()
    {
        if (_isInitialized) return;
        
        _isInitialized = true;
        _ = ScanNetworkAsync();
        StartMonitoring();
        StartAutoScan();
    }
    
    [RelayCommand]
    private async Task ScanNetworkAsync()
    {
        if (IsScanning) return;
        
        IsScanning = true;
        
        try
        {
            var devices = await _networkScanner.ScanNetworkAsync();
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Devices.Clear();
                
                foreach (var device in devices)
                {
                    device.Id = Devices.Count + 1;
                    Devices.Add(device);
                }
                
                UpdateStatistics();
                ApplyFilters();
                
                // Notify other ViewModels about the update
                DevicesUpdated?.Invoke(Devices);
                
                _toastManager
                    .CreateToast("Scan Complete")
                    .WithContent($"Found {devices.Count} device(s) on the network")
                    .DismissOnClick()
                    .ShowSuccess();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _toastManager
                    .CreateToast("Scan Failed")
                    .WithContent($"Error: {ex.Message}")
                    .DismissOnClick()
                    .ShowError();
            });
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void StartAutoScan()
    {
        // Auto-scan every 8 seconds (configurable)
        _autoScanTimer = new Timer(async _ =>
        {
            await ScanNetworkAsync();
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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
                        var existingByMac = Devices.FirstOrDefault(d => d.MacAddress == device.MacAddress);
                        if (existingByMac == null)
                        {
                            device.Id = Devices.Count + 1;
                            Devices.Add(device);
                            
                            _toastManager
                                .CreateToast("Device Connected")
                                .WithContent($"{device.Name} ({device.IpAddress})")
                                .DismissOnClick()
                                .ShowSuccess();
                            
                            // Notify subscribers about the change
                            DeviceChanged?.Invoke(device, changeType);
                        }
                        break;

                    case DeviceChangeType.Disconnected:
                        var disconnectedDevice = Devices.FirstOrDefault(d => d.MacAddress == device.MacAddress);
                        if (disconnectedDevice != null)
                        {
                            disconnectedDevice.Status = "Disconnected";
                            
                            _toastManager
                                .CreateToast("Device Disconnected")
                                .WithContent($"{device.Name} ({device.IpAddress})")
                                .DismissOnClick()
                                .ShowWarning();
                            
                            // Notify subscribers about the change
                            DeviceChanged?.Invoke(disconnectedDevice, changeType);
                        }
                        break;

                    case DeviceChangeType.Updated:
                        var existingDevice = Devices.FirstOrDefault(d => d.MacAddress == device.MacAddress);
                        if (existingDevice != null)
                        {
                            existingDevice.SignalStrength = device.SignalStrength;
                            existingDevice.Status = device.Status;
                            existingDevice.IpAddress = device.IpAddress;
                            existingDevice.Name = device.Name;
                            
                            // Notify subscribers about the change
                            DeviceChanged?.Invoke(existingDevice, changeType);
                        }
                        break;
                }
                
                UpdateStatistics();
                ApplyFilters();
                
                // Notify other ViewModels
                DevicesUpdated?.Invoke(Devices);
            });
        });
    }

    private void UpdateStatistics()
    {
        TotalDevices = Devices.Count;
        ActiveDevices = Devices.Count(d => d.Status == "Active");
        InactiveDevices = Devices.Count(d => d.Status != "Active");
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedFilterChanged(string value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = Devices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(d =>
                d.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                d.IpAddress.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                d.MacAddress.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedFilter != "All")
        {
            if (SelectedFilter == "Active" || SelectedFilter == "Disconnected")
            {
                filtered = filtered.Where(d => d.Status == SelectedFilter);
            }
            else
            {
                filtered = filtered.Where(d => d.Category == SelectedFilter);
            }
        }

        FilteredDevices.Clear();
        foreach (var device in filtered)
        {
            FilteredDevices.Add(device);
        }
    }

    protected override void DisposeManagedResources()
    {
        _autoScanTimer?.Dispose();
        _networkScanner.StopMonitoring();
        base.DisposeManagedResources();
    }
}