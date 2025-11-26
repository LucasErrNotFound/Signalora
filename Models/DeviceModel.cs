namespace Signalora.Models;

public class DeviceModel
{
    public string Name { get; set; }
    public string IpAddress { get; set; }
    public string MacAddress { get; set; }
    public string Status { get; set; }
    public string Icon { get; set; }
    public string DeviceInfo => $"{IpAddress} - {MacAddress}";
}