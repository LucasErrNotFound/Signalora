using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Signalora.Models;
using Signalora.Services.Interface;

namespace Signalora.Services;

public class NetworkScanner : INetworkScanner
{
    private Timer _monitoringTimer;
    private Action<DeviceModel, DeviceChangeType> _onDeviceChanged;
    private Dictionary<string, DeviceModel> _previousDevices = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public async Task<List<DeviceModel>> ScanNetworkAsync()
    {
        await _scanLock.WaitAsync();
        try
        {
            var devices = new List<DeviceModel>();
            var networkInfo = await GetNetworkInfoAsync();

            if (string.IsNullOrEmpty(networkInfo.NetworkPrefix))
                return devices;

            // Get ARP table entries
            var arpDevices = await GetArpTableDevicesAsync();
            
            // Scan network range
            var tasks = new List<Task<DeviceModel>>();
            for (int i = 1; i < 255; i++)
            {
                var ip = $"{networkInfo.NetworkPrefix}.{i}";
                tasks.Add(ScanDeviceAsync(ip, arpDevices));
            }

            var results = await Task.WhenAll(tasks);
            devices = results.Where(d => d != null).ToList();

            return devices;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task<DeviceModel> ScanDeviceAsync(string ipAddress, Dictionary<string, string> arpTable)
    {
        try
        {
            var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 100);

            if (reply.Status == IPStatus.Success)
            {
                var macAddress = arpTable.ContainsKey(ipAddress) 
                    ? arpTable[ipAddress] 
                    : "Unknown";

                var deviceName = await GetDeviceNameAsync(ipAddress);
                var category = DetermineDeviceCategory(deviceName, macAddress);
                var signalStrength = DetermineSignalStrength(reply.RoundtripTime);
                var connectionType = DetermineConnectionType(ipAddress);

                return new DeviceModel
                {
                    Name = deviceName,
                    IpAddress = ipAddress,
                    MacAddress = macAddress,
                    Status = "Active",
                    Category = category,
                    Connection = connectionType,
                    SignalStrength = signalStrength,
                    Icon = GetDeviceIcon(category)
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error scanning {ipAddress}: {ex.Message}");
        }

        return null;
    }

    public async Task<NetworkInfo> GetNetworkInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                foreach (var ni in networkInterfaces)
                {
                    var ipProps = ni.GetIPProperties();
                    var ipv4 = ipProps.UnicastAddresses
                        .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (ipv4 != null)
                    {
                        var ip = ipv4.Address.ToString();
                        var parts = ip.Split('.');
                        
                        return new NetworkInfo
                        {
                            LocalIpAddress = ip,
                            SubnetMask = ipv4.IPv4Mask?.ToString() ?? "255.255.255.0",
                            Gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "",
                            NetworkPrefix = $"{parts[0]}.{parts[1]}.{parts[2]}"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network info: {ex.Message}");
            }

            return new NetworkInfo();
        });
    }

    private async Task<Dictionary<string, string>> GetArpTableDevicesAsync()
    {
        return await Task.Run(() =>
        {
            var arpTable = new Dictionary<string, string>();

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    arpTable = GetArpTableWindows();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    arpTable = GetArpTableLinux();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    arpTable = GetArpTableMacOS();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading ARP table: {ex.Message}");
            }

            return arpTable;
        });
    }

    private Dictionary<string, string> GetArpTableWindows()
    {
        var arpTable = new Dictionary<string, string>();
        
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"(\d+\.\d+\.\d+\.\d+)\s+([\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2})");

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var ip = match.Groups[1].Value;
                    var mac = match.Groups[2].Value.Replace("-", ":").ToUpper();
                    arpTable[ip] = mac;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error parsing Windows ARP: {ex.Message}");
        }

        return arpTable;
    }

    private Dictionary<string, string> GetArpTableLinux()
    {
        var arpTable = new Dictionary<string, string>();
        
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"arp -n\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"(\d+\.\d+\.\d+\.\d+).*?([\da-fA-F]{2}:[\da-fA-F]{2}:[\da-fA-F]{2}:[\da-fA-F]{2}:[\da-fA-F]{2}:[\da-fA-F]{2})");

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var ip = match.Groups[1].Value;
                    var mac = match.Groups[2].Value.ToUpper();
                    arpTable[ip] = mac;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error parsing Linux ARP: {ex.Message}");
        }

        return arpTable;
    }

    private Dictionary<string, string> GetArpTableMacOS()
    {
        var arpTable = new Dictionary<string, string>();
        
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/sbin/arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var regex = new Regex(@"\((\d+\.\d+\.\d+\.\d+)\) at ([\da-fA-F]{1,2}:[\da-fA-F]{1,2}:[\da-fA-F]{1,2}:[\da-fA-F]{1,2}:[\da-fA-F]{1,2}:[\da-fA-F]{1,2})");

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var ip = match.Groups[1].Value;
                    var mac = match.Groups[2].Value.ToUpper();
                    arpTable[ip] = mac;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error parsing macOS ARP: {ex.Message}");
        }

        return arpTable;
    }

    private async Task<string> GetDeviceNameAsync(string ipAddress)
    {
        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
            return hostEntry.HostName.Split('.')[0];
        }
        catch
        {
            return $"Device-{ipAddress.Split('.').Last()}";
        }
    }

    private string DetermineDeviceCategory(string deviceName, string macAddress)
    {
        var name = deviceName.ToLower();
        var mac = macAddress.ToUpper();

        // Common MAC address prefixes
        var mobileVendors = new[] { "00:1A:11", "00:26:B0", "00:50:C2", "A4:D1:8C" }; // Apple, Samsung, etc.
        
        if (name.Contains("iphone") || name.Contains("android") || name.Contains("samsung") || 
            name.Contains("galaxy") || name.Contains("pixel") || name.Contains("oneplus"))
            return "Phone";
        
        if (name.Contains("ipad") || name.Contains("tablet"))
            return "Tablet";
        
        if (name.Contains("watch") || name.Contains("band") || name.Contains("fit"))
            return "Wearable";
        
        if (name.Contains("tv") || name.Contains("chromecast") || name.Contains("roku") || 
            name.Contains("firestick") || name.Contains("apple-tv"))
            return "TV";
        
        if (name.Contains("laptop") || name.Contains("macbook") || name.Contains("thinkpad") || 
            name.Contains("dell") || name.Contains("hp") || name.Contains("asus") || name.Contains("lenovo"))
            return "Laptop";
        
        if (name.Contains("desktop") || name.Contains("pc") || name.Contains("workstation"))
            return "Desktop";
        
        if (name.Contains("printer") || name.Contains("scanner"))
            return "Printer";
        
        if (name.Contains("camera") || name.Contains("cam"))
            return "Camera";
        
        if (name.Contains("speaker") || name.Contains("echo") || name.Contains("homepod") || 
            name.Contains("google-home"))
            return "Speaker";

        return "Unknown";
    }

    private string DetermineSignalStrength(long roundtripTime)
    {
        if (roundtripTime < 10) return "Excellent";
        if (roundtripTime < 50) return "Good";
        if (roundtripTime < 100) return "Fair";
        return "Poor";
    }

    private string DetermineConnectionType(string ipAddress)
    {
        // This is a simplified determination
        // In reality, you'd need to check the network interface type
        // For now, we'll use a heuristic based on network analysis
        
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up);

            foreach (var ni in interfaces)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    var props = ni.GetIPProperties();
                    var localIp = props.UnicastAddresses
                        .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);
                    
                    if (localIp != null && ipAddress.StartsWith(localIp.Address.ToString().Substring(0, 10)))
                    {
                        return "Ethernet";
                    }
                }
            }
        }
        catch { }

        return "Wireless";
    }

    private string GetDeviceIcon(string category)
    {
        return category switch
        {
            "Phone" => "\uE1E7",
            "Tablet" => "\uE1E7",
            "Laptop" => "\uE167",
            "Desktop" => "\uE167",
            "TV" => "\uE1E1",
            "Printer" => "\uE1E0",
            "Camera" => "\uE156",
            "Speaker" => "\uE1DB",
            "Wearable" => "\uE1E7",
            _ => "\uE167"
        };
    }

    public void StartMonitoring(Action<DeviceModel, DeviceChangeType> onDeviceChanged)
    {
        _onDeviceChanged = onDeviceChanged;
        _monitoringTimer = new Timer(async _ => await MonitorNetworkChanges(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    public void StopMonitoring()
    {
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;
    }

    private async Task MonitorNetworkChanges()
    {
        try
        {
            var currentDevices = await ScanNetworkAsync();
            var currentDeviceDict = currentDevices.ToDictionary(d => d.IpAddress);

            // Check for new or updated devices
            foreach (var device in currentDevices)
            {
                if (!_previousDevices.ContainsKey(device.IpAddress))
                {
                    _onDeviceChanged?.Invoke(device, DeviceChangeType.Connected);
                }
                else if (!DevicesAreEqual(_previousDevices[device.IpAddress], device))
                {
                    _onDeviceChanged?.Invoke(device, DeviceChangeType.Updated);
                }
            }

            // Check for disconnected devices
            foreach (var prevDevice in _previousDevices.Values)
            {
                if (!currentDeviceDict.ContainsKey(prevDevice.IpAddress))
                {
                    prevDevice.Status = "Disconnected";
                    _onDeviceChanged?.Invoke(prevDevice, DeviceChangeType.Disconnected);
                }
            }

            _previousDevices = currentDeviceDict;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error monitoring network: {ex.Message}");
        }
    }

    private bool DevicesAreEqual(DeviceModel device1, DeviceModel device2)
    {
        return device1.IpAddress == device2.IpAddress &&
               device1.MacAddress == device2.MacAddress &&
               device1.Status == device2.Status &&
               device1.SignalStrength == device2.SignalStrength;
    }
}