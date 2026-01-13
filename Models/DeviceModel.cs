namespace Signalora.Models;

public class DeviceModel
{
    public int Id { get; set; }
    public string Name { get; set; }            // iPhone 16 Pro Max, Samsung A14 5G, etc
    public string IpAddress { get; set; }       // 192.168.1.20, etc
    public string MacAddress { get; set; }      // 00-1A-2B-3C-4D-5E
    public string Status { get; set; }          // Connected, Disconnected, etc
    public string Category { get; set; }        // Phone, Pc, etc
    public string Connection { get; set; }      // Ethernet, Wireless, etc
    public string SignalStrength { get; set; }  // Good, Bad, etc
    public string Icon { get; set; }            // Icon of Device
    public string DeviceInfo => $"{IpAddress} - {MacAddress}";
}