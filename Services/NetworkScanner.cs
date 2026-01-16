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
            var hostName = hostEntry.HostName;
            
            // Try to get just the computer name (first part before domain)
            if (hostName.Contains('.'))
            {
                hostName = hostName.Split('.')[0];
            }
            
            return hostName;
        }
        catch
        {
            // Fallback: Use last octet of IP
            return $"Device-{ipAddress.Split('.').Last()}";
        }
    }

    private string DetermineDeviceCategory(string deviceName, string macAddress)
    {
        var name = deviceName.ToLower();
        var mac = macAddress.ToUpper();

        // Enhanced MAC address OUI (Organizationally Unique Identifier) database
        var mobileOUIs = new Dictionary<string, string>
        {
            // Apple
            { "00:1A:11", "Phone" }, { "00:26:B0", "Phone" }, { "00:50:C2", "Phone" },
            { "A4:D1:8C", "Phone" }, { "F0:DB:E2", "Phone" }, { "8C:29:37", "Phone" },
            { "DC:2B:2A", "Phone" }, { "B8:78:2E", "Phone" }, { "C8:BC:C8", "Phone" },
            { "00:A0:40", "Phone" }, { "00:0D:93", "Phone" }, { "00:17:F2", "Phone" },
            { "00:1C:B3", "Phone" }, { "00:1E:C2", "Phone" }, { "00:1F:5B", "Phone" },
            { "00:21:E9", "Phone" }, { "00:23:12", "Phone" }, { "00:23:32", "Phone" },
            { "00:23:6C", "Phone" }, { "00:23:DF", "Phone" }, { "00:24:8C", "Phone" },
            { "00:25:00", "Phone" }, { "00:25:4B", "Phone" }, { "00:25:BC", "Phone" },
            { "00:26:08", "Phone" }, { "00:26:4A", "Phone" }, { "00:26:BB", "Phone" },
            
            // Samsung
            { "00:07:AB", "Phone" }, { "00:12:FB", "Phone" }, { "00:15:B9", "Phone" },
            { "00:16:32", "Phone" }, { "00:17:C9", "Phone" }, { "00:18:AF", "Phone" },
            { "00:1B:98", "Phone" }, { "00:1C:43", "Phone" }, { "00:1D:25", "Phone" },
            { "00:1E:7D", "Phone" }, { "00:1F:CC", "Phone" }, { "00:21:19", "Phone" },
            { "00:21:4C", "Phone" }, { "00:23:39", "Phone" }, { "00:23:D6", "Phone" },
            { "00:26:37", "Phone" }, { "D0:17:6A", "Phone" }, { "E8:50:8B", "Phone" },
            { "34:23:BA", "Phone" }, { "A0:0B:BA", "Phone" }, { "3C:8B:FE", "Phone" },
            
            // Huawei
            { "00:18:82", "Phone" }, { "00:1E:10", "Phone" }, { "00:25:68", "Phone" },
            { "00:46:4B", "Phone" }, { "00:9A:CD", "Phone" }, { "04:02:1F", "Phone" },
            { "10:1F:74", "Phone" }, { "28:6E:D4", "Phone" }, { "34:6B:D3", "Phone" },
            { "48:46:FB", "Phone" }, { "58:2A:F7", "Phone" }, { "70:72:3C", "Phone" },
            
            // Xiaomi
            { "00:9E:C8", "Phone" }, { "14:F6:5A", "Phone" }, { "28:E3:1F", "Phone" },
            { "34:80:B3", "Phone" }, { "50:8F:4C", "Phone" }, { "64:09:80", "Phone" },
            { "68:DF:DD", "Phone" }, { "74:51:BA", "Phone" }, { "78:02:F8", "Phone" },
            { "8C:BE:BE", "Phone" }, { "98:FA:E3", "Phone" }, { "F8:A4:5F", "Phone" },
            
            // OnePlus
            { "A8:5E:45", "Phone" }, { "AC:37:43", "Phone" }, { "D4:6A:A8", "Phone" },
            
            // Google Pixel
            { "74:E5:F9", "Phone" }, { "F4:F5:A5", "Phone" }
        };

        // Check MAC OUI first (most reliable for mobile devices)
        var macPrefix = mac.Length >= 8 ? mac.Substring(0, 8) : "";
        if (mobileOUIs.TryGetValue(macPrefix, out var category))
        {
            return category;
        }

        // Name-based detection (enhanced)
        if (name.Contains("iphone") || name.Contains("android") || name.Contains("samsung") ||
            name.Contains("galaxy") || name.Contains("pixel") || name.Contains("oneplus") ||
            name.Contains("huawei") || name.Contains("xiaomi") || name.Contains("oppo") ||
            name.Contains("vivo") || name.Contains("realme") || name.Contains("nokia") ||
            name.Contains("motorola") || name.Contains("lg-") || name.Contains("htc"))
            return "Phone";

        if (name.Contains("ipad") || name.Contains("tablet") || name.Contains("tab-"))
            return "Tablet";

        if (name.Contains("watch") || name.Contains("band") || name.Contains("fit") ||
            name.Contains("wearable"))
            return "Wearable";

        if (name.Contains("tv") || name.Contains("chromecast") || name.Contains("roku") ||
            name.Contains("firestick") || name.Contains("apple-tv") || name.Contains("shield"))
            return "TV";

        // Improved laptop/desktop detection
        if (name.Contains("laptop") || name.Contains("macbook") || name.Contains("thinkpad") ||
            name.Contains("latitude") || name.Contains("pavilion") || name.Contains("inspiron") ||
            name.Contains("vivobook") || name.Contains("zenbook") || name.Contains("ideapad") ||
            name.Contains("surface") || name.Contains("matebook") || name.Contains("notebook"))
            return "Laptop";

        // Only classify as Desktop if it explicitly contains desktop-related terms
        // Remove the "DESKTOP" check as it's often a Windows hostname prefix
        if (name.Contains("desktop-pc") || name.Contains("workstation") || 
            name.Contains("tower") || name.Contains("optiplex"))
            return "Desktop";

        if (name.Contains("printer") || name.Contains("scanner") || name.Contains("canon") ||
            name.Contains("epson") || name.Contains("hp-") || name.Contains("brother"))
            return "Printer";

        if (name.Contains("camera") || name.Contains("cam") || name.Contains("ring") ||
            name.Contains("nest-cam") || name.Contains("arlo"))
            return "Camera";

        if (name.Contains("speaker") || name.Contains("echo") || name.Contains("homepod") ||
            name.Contains("google-home") || name.Contains("alexa") || name.Contains("sonos"))
            return "Speaker";

        // If name starts with "DESKTOP-", it's likely a Windows PC, check further
        if (name.StartsWith("desktop-"))
        {
            // Default to Laptop for Windows PCs unless we have more info
            return "Laptop";
        }

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
            var currentDeviceDict = currentDevices.ToDictionary(d => d.MacAddress); // Use MAC as unique identifier

            // Check for new or updated devices
            foreach (var device in currentDevices)
            {
                if (!_previousDevices.ContainsKey(device.MacAddress))
                {
                    // Truly new device
                    _onDeviceChanged?.Invoke(device, DeviceChangeType.Connected);
                }
                else if (!DevicesAreEqual(_previousDevices[device.MacAddress], device))
                {
                    // Device exists but properties changed
                    _onDeviceChanged?.Invoke(device, DeviceChangeType.Updated);
                }
            }

            // Check for disconnected devices
            foreach (var prevDevice in _previousDevices.Values)
            {
                if (!currentDeviceDict.ContainsKey(prevDevice.MacAddress))
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
        return device1.MacAddress == device2.MacAddress &&
               device1.Status == device2.Status &&
               device1.SignalStrength == device2.SignalStrength;
    }
}