using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation using nmcli and iw commands.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxWiFiInfoProvider : IWiFiInfoProvider
{
    private readonly ILogger<LinuxWiFiInfoProvider> _logger;

    public LinuxWiFiInfoProvider(ILogger<LinuxWiFiInfoProvider> logger)
    {
        _logger = logger;
    }

    public bool IsSupported => true;

    public WiFiInfo? GetWiFiInfo(string adapterId)
    {
        // Try to find the interface name from the adapter ID
        // On Linux, adapter ID is typically the interface name (e.g., wlan0, wlp2s0)
        var allInfo = GetAllWiFiInfo();

        // Try exact match first
        if (allInfo.TryGetValue(adapterId, out var info))
            return info;

        // Try partial match (adapterId might contain the interface name)
        foreach (var kvp in allInfo)
        {
            if (adapterId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    public Dictionary<string, WiFiInfo> GetAllWiFiInfo()
    {
        var result = new Dictionary<string, WiFiInfo>();

        try
        {
            // Try nmcli first (works without root)
            var nmcliResult = RunCommand("nmcli", "-t -f DEVICE,ACTIVE,SSID,SIGNAL,FREQ,CHAN,SECURITY device wifi list");
            if (!string.IsNullOrEmpty(nmcliResult))
            {
                ParseNmcliOutput(nmcliResult, result);
            }

            // If no results, try iw
            if (result.Count == 0)
            {
                // Get list of wireless interfaces
                var iwDevResult = RunCommand("iw", "dev");
                if (!string.IsNullOrEmpty(iwDevResult))
                {
                    var interfaces = ParseIwDevInterfaces(iwDevResult);
                    foreach (var iface in interfaces)
                    {
                        var linkResult = RunCommand("iw", $"dev {iface} link");
                        if (!string.IsNullOrEmpty(linkResult))
                        {
                            var wifiInfo = ParseIwLinkOutput(linkResult);
                            if (wifiInfo != null)
                            {
                                result[iface] = wifiInfo;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get WiFi info on Linux");
        }

        return result;
    }

    private void ParseNmcliOutput(string output, Dictionary<string, WiFiInfo> result)
    {
        // Format: DEVICE:ACTIVE:SSID:SIGNAL:FREQ:CHAN:SECURITY
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':');
            if (parts.Length >= 4 && parts[1] == "yes") // Only active connections
            {
                var device = parts[0];
                var ssid = parts[2];

                int.TryParse(parts[3], out var signal);

                string? frequency = parts.Length > 4 ? parts[4] : null;
                int.TryParse(parts.Length > 5 ? parts[5] : null, out var channel);
                string? security = parts.Length > 6 ? parts[6] : null;

                // Parse frequency to determine band
                string? freqBand = null;
                int? freqMhz = null;
                if (frequency != null && int.TryParse(frequency.Replace(" MHz", ""), out var parsedFreq))
                {
                    freqMhz = parsedFreq;
                    freqBand = parsedFreq switch
                    {
                        >= 2400 and < 2500 => "2.4 GHz",
                        >= 5000 and < 6000 => "5 GHz",
                        >= 6000 => "6 GHz",
                        _ => null
                    };
                }

                result[device] = new WiFiInfo
                {
                    Ssid = ssid,
                    SignalStrength = signal,
                    Channel = channel > 0 ? channel : null,
                    FrequencyMhz = freqMhz,
                    Band = freqBand,
                    Security = security
                };
            }
        }
    }

    private List<string> ParseIwDevInterfaces(string output)
    {
        var interfaces = new List<string>();
        var regex = InterfaceRegex();

        foreach (Match match in regex.Matches(output))
        {
            interfaces.Add(match.Groups[1].Value);
        }

        return interfaces;
    }

    private WiFiInfo? ParseIwLinkOutput(string output)
    {
        if (output.Contains("Not connected"))
            return null;

        var ssidMatch = SsidRegex().Match(output);
        var signalMatch = SignalRegex().Match(output);
        var freqMatch = FreqRegex().Match(output);
        var bitrateMatch = BitrateRegex().Match(output);

        if (!ssidMatch.Success)
            return null;

        int? signalDbm = null;
        if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out var signal))
            signalDbm = signal;

        int? freqMhz = null;
        string? freqBand = null;
        if (freqMatch.Success && int.TryParse(freqMatch.Groups[1].Value, out var freq))
        {
            freqMhz = freq;
            freqBand = freq switch
            {
                >= 2400 and < 2500 => "2.4 GHz",
                >= 5000 and < 6000 => "5 GHz",
                >= 6000 => "6 GHz",
                _ => null
            };
        }

        int? linkSpeed = null;
        if (bitrateMatch.Success && int.TryParse(bitrateMatch.Groups[1].Value, out var bitrate))
            linkSpeed = bitrate;

        // Convert dBm to approximate percentage
        int signalPercent = signalDbm.HasValue
            ? Math.Clamp((signalDbm.Value + 100) * 2, 0, 100)
            : 0;

        return new WiFiInfo
        {
            Ssid = ssidMatch.Groups[1].Value,
            SignalDbm = signalDbm,
            SignalStrength = signalPercent,
            LinkSpeedMbps = linkSpeed,
            FrequencyMhz = freqMhz,
            Band = freqBand
        };
    }

    private static string? RunCommand(string command, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return output;
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"Interface\s+(\w+)")]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex(@"SSID:\s*(.+)")]
    private static partial Regex SsidRegex();

    [GeneratedRegex(@"signal:\s*(-?\d+)\s*dBm")]
    private static partial Regex SignalRegex();

    [GeneratedRegex(@"freq:\s*(\d+)")]
    private static partial Regex FreqRegex();

    [GeneratedRegex(@"tx bitrate:\s*(\d+)")]
    private static partial Regex BitrateRegex();
}
