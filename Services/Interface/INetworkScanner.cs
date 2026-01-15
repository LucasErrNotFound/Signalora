using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Signalora.Models;

namespace Signalora.Services.Interface;

public interface INetworkScanner
{
    /// <summary>
    /// Scans the network for connected devices
    /// </summary>
    Task<List<DeviceModel>> ScanNetworkAsync();
    
    /// <summary>
    /// Gets the current network information
    /// </summary>
    Task<NetworkInfo> GetNetworkInfoAsync();
    
    /// <summary>
    /// Starts monitoring network changes
    /// </summary>
    void StartMonitoring(Action<DeviceModel, DeviceChangeType> onDeviceChanged);
    
    /// <summary>
    /// Stops monitoring network changes
    /// </summary>
    void StopMonitoring();
}

public class NetworkInfo
{
    public string LocalIpAddress { get; set; }
    public string SubnetMask { get; set; }
    public string Gateway { get; set; }
    public string NetworkPrefix { get; set; }
}

public enum DeviceChangeType
{
    Connected,
    Disconnected,
    Updated
}