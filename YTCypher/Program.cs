using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

public class YouTubeStreamDecoder
{
    private readonly HttpClient _httpClient;
    private const string API_KEY = "AIzaSyDlsvIcFuIzpy1ZdJK2ZlS2tIc7Bn9G9ks";
    private string _playerJsUrl;
    private string _playerJsCode;
    private List<Func<char[], char[]>> _decipherOperations;

    public YouTubeStreamDecoder()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        _decipherOperations = new List<Func<char[], char[]>>();
    }

    public async Task<string> GetDecipheredStreamUrlAsync(string videoId)
    {
        try
        {
            var videoInfo = await GetVideoInfoAsync(videoId);
            var streamInfo = ExtractStreamInfo(videoInfo);

            if (streamInfo == null) throw new Exception("No suitable stream found");

            if (string.IsNullOrEmpty(streamInfo.Signature))
                return streamInfo.Url;

            if (_decipherOperations.Count == 0)
            {
                await InitializeDecipherAsync();
            }

            streamInfo.Signature = DecipherSignature(streamInfo.Signature);

            return BuildStreamUrl(streamInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
    }

    private async Task<string> GetVideoInfoAsync(string videoId)
    {
        var payload = new
        {
            videoId,
            context = new
            {
                client = new
                {
                    clientName = "ANDROID",
                    clientVersion = "19.09.37",
                    androidSdkVersion = 30,
                    hl = "en",
                    gl = "US",
                    utcOffsetMinutes = 0
                }
            },
            playbackContext = new
            {
                contentPlaybackContext = new
                {
                    html5Preference = "HTML5_PREF_WANTS"
                }
            },
            contentCheckOk = true,
            racyCheckOk = true
        };

        var response = await _httpClient.PostAsync(
            $"https://www.youtube.com/youtubei/v1/player?key={API_KEY}",
            new StringContent(JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json"));

        return await response.Content.ReadAsStringAsync();
    }

    private YouTubeStreamInfo ExtractStreamInfo(string jsonResponse)
    {
        var json = JsonNode.Parse(jsonResponse);
        var formats = json?["streamingData"]?["adaptiveFormats"]?.AsArray();

        foreach (var format in formats)
        {
            var mimeType = format?["mimeType"]?.ToString();
            if (mimeType != null && mimeType.Contains("audio/mp4"))
            {
                if (format["url"] != null)
                {
                    return new YouTubeStreamInfo { Url = format["url"].ToString() };
                }
                else if (format["signatureCipher"] != null)
                {
                    return ParseCipher(format["signatureCipher"].ToString());
                }
            }
        }
        return null;
    }

    private YouTubeStreamInfo ParseCipher(string cipher)
    {
        var info = new YouTubeStreamInfo();
        var parameters = cipher.Split('&');

        foreach (var param in parameters)
        {
            if (param.StartsWith("s="))
            {
                info.Signature = Uri.UnescapeDataString(param[2..]);
            }
            else if (param.StartsWith("sp="))
            {
                info.SignatureParameter = Uri.UnescapeDataString(param[3..]);
            }
            else if (param.StartsWith("url="))
            {
                info.Url = Uri.UnescapeDataString(param[4..]);
            }
        }

        return info;
    }

    private async Task InitializeDecipherAsync()
    {
        _playerJsUrl = await FindPlayerJsUrlAsync();
        _playerJsCode = await _httpClient.GetStringAsync(_playerJsUrl);
        ExtractDecipherOperations(_playerJsCode);
    }

    private async Task<string> FindPlayerJsUrlAsync()
    {
        return "https://www.youtube.com/s/player/xxxxxxxx/player_ias.vflset/en_US/base.js";
    }

    private void ExtractDecipherOperations(string jsCode)
    {
        var regex = new Regex(@"function\(a\)\{a=a\.split\(""""""\);(.*?);return a\.join\(""""""\)\}");
        var match = regex.Match(jsCode);

        if (match.Success)
        {
            var operations = match.Groups[1].Value.Split(';');

            foreach (var op in operations)
            {
                if (op.Contains("reverse()"))
                {
                    _decipherOperations.Add(arr => { Array.Reverse(arr); return arr; });
                }
                else if (op.Contains("slice("))
                {
                    var num = int.Parse(Regex.Match(op, @"slice\((\d+)\)").Groups[1].Value);
                    _decipherOperations.Add(arr => arr.Skip(num).ToArray());
                }
                else if (op.Contains("splice("))
                {
                    var num = int.Parse(Regex.Match(op, @"splice\((\d+)\)").Groups[1].Value);
                    _decipherOperations.Add(arr => arr.Take(arr.Length - num).ToArray());
                }
            }
        }

        if (_decipherOperations.Count == 0)
        {
            _decipherOperations.Add(arr => { Array.Reverse(arr); return arr; });
            _decipherOperations.Add(arr => arr.Skip(2).ToArray());
        }
    }

    private string DecipherSignature(string encryptedSig)
    {
        char[] chars = encryptedSig.ToCharArray();

        foreach (var operation in _decipherOperations)
        {
            chars = operation(chars);
        }

        return new string(chars);
    }

    private string BuildStreamUrl(YouTubeStreamInfo streamInfo)
    {
        if (string.IsNullOrEmpty(streamInfo.Signature))
            return streamInfo.Url;

        string sigParam = string.IsNullOrEmpty(streamInfo.SignatureParameter)
            ? "sig"
            : streamInfo.SignatureParameter;

        return $"{streamInfo.Url}&{sigParam}={streamInfo.Signature}";
    }
}

public class YouTubeStreamInfo
{
    public string Url { get; set; }
    public string Signature { get; set; }
    public string SignatureParameter { get; set; }
}

class Program
{
    static async Task Main(string[] args)
    {
        var decoder = new YouTubeStreamDecoder();
        string videoId = "dQw4w9WgXcQ"; 

        Console.WriteLine("Fetching deciphered stream URL...");
        string streamUrl = await decoder.GetDecipheredStreamUrlAsync(videoId);

        if (streamUrl != null)
        {
            Console.WriteLine("\nDeciphered Stream URL:");
            Console.WriteLine(streamUrl);

        }
        else
        {
            Console.WriteLine("Failed to decipher stream URL");
        }
    }
}