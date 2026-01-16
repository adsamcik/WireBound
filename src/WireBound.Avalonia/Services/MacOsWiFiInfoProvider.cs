using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WireBound.Core.Models;

namespace WireBound.Avalonia.Services;

/// <summary>
/// macOS implementation using airport command
/// </summary>
internal partial class MacOsWiFiInfoProvider : IWiFiInfoProvider
{
    private readonly ILogger _logger;
    private const string AirportPath = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";
    
    public MacOsWiFiInfoProvider(ILogger logger)
    {
        _logger = logger;
    }
    
    public bool IsSupported => File.Exists(AirportPath);
    
    public WiFiInfo? GetWiFiInfo(string adapterId)
    {
        // macOS typically has one WiFi interface (en0 or en1)
        var allInfo = GetAllWiFiInfo();
        return allInfo.Values.FirstOrDefault();
    }
    
    public Dictionary<string, WiFiInfo> GetAllWiFiInfo()
    {
        var result = new Dictionary<string, WiFiInfo>();
        
        try
        {
            var output = RunCommand(AirportPath, "-I");
            if (!string.IsNullOrEmpty(output))
            {
                var info = ParseAirportOutput(output);
                if (info != null)
                {
                    // Default interface is typically en0 for WiFi on Mac
                    result["en0"] = info;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get WiFi info on macOS");
        }
        
        return result;
    }
    
    private WiFiInfo? ParseAirportOutput(string output)
    {
        // Parse airport -I output format:
        //      agrCtlRSSI: -45
        //      agrExtRSSI: 0
        //      agrCtlNoise: -90
        //      state: running
        //      op mode: station
        //      lastTxRate: 400
        //      maxRate: 400
        //      lastAssocStatus: 0
        //      SSID: MyNetwork
        //      BSSID: aa:bb:cc:dd:ee:ff
        //      channel: 44
        
        var ssidMatch = SsidRegex().Match(output);
        if (!ssidMatch.Success)
            return null;
        
        var rssiMatch = RssiRegex().Match(output);
        var noiseMatch = NoiseRegex().Match(output);
        var rateMatch = TxRateRegex().Match(output);
        var channelMatch = ChannelRegex().Match(output);
        
        int? signalDbm = null;
        if (rssiMatch.Success && int.TryParse(rssiMatch.Groups[1].Value, out var rssi))
            signalDbm = rssi;
        
        int? linkSpeed = null;
        if (rateMatch.Success && int.TryParse(rateMatch.Groups[1].Value, out var rate))
            linkSpeed = rate;
        
        int? channel = null;
        string? freqBand = null;
        if (channelMatch.Success)
        {
            // Channel format can be "44" or "44,1" (channel,width)
            var channelStr = channelMatch.Groups[1].Value.Split(',')[0];
            if (int.TryParse(channelStr, out var ch))
            {
                channel = ch;
                freqBand = ch switch
                {
                    >= 1 and <= 14 => "2.4 GHz",
                    >= 32 and <= 177 => "5 GHz",
                    > 177 => "6 GHz",
                    _ => null
                };
            }
        }
        
        return new WiFiInfo
        {
            Ssid = ssidMatch.Groups[1].Value.Trim(),
            SignalStrengthDbm = signalDbm,
            LinkSpeedMbps = linkSpeed,
            Channel = channel,
            FrequencyBand = freqBand
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

    [GeneratedRegex(@"^\s*SSID:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex SsidRegex();
    
    [GeneratedRegex(@"agrCtlRSSI:\s*(-?\d+)")]
    private static partial Regex RssiRegex();
    
    [GeneratedRegex(@"agrCtlNoise:\s*(-?\d+)")]
    private static partial Regex NoiseRegex();
    
    [GeneratedRegex(@"lastTxRate:\s*(\d+)")]
    private static partial Regex TxRateRegex();
    
    [GeneratedRegex(@"channel:\s*(\d+)")]
    private static partial Regex ChannelRegex();
}
