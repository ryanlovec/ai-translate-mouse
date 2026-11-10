using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AIMouse.Core;

public sealed class Config
{
    public string ApiKeyProtected { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    /// <summary>"deepseek" (cloud) or "local" (Ollama / LM Studio / any OpenAI-compatible server on this PC).</summary>
    public string Provider { get; set; } = "deepseek";
    public string LocalUrl { get; set; } = "http://127.0.0.1:11434/v1";
    public string LocalModel { get; set; } = "";
    public int HoldDelayMs { get; set; } = 180;
    public bool StartWithWindows { get; set; }
    /// <summary>Read the word aloud as soon as its card appears (Ctrl + point).</summary>
    public bool AutoSpeakWord { get; set; }
    /// <summary>How box / full-screen translations are shown: "side" (panel beside), "under" (bilingual over the text), "replace" (Chinese in place).</summary>
    public string BoxView { get; set; } = "side";
    public string FullView { get; set; } = "replace";
    public bool Mock { get; set; }

    [JsonIgnore]
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIMouse");

    static string FilePath => Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch (Exception ex) { Log.Write("Config load failed: " + ex.Message); }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string GetApiKey()
    {
        if (!string.IsNullOrEmpty(ApiKeyProtected))
        {
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ApiKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) { Log.Write("Key decrypt failed: " + ex.Message); }
        }
        return Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
    }

    [JsonIgnore] public bool UseLocal => string.Equals(Provider, "local", StringComparison.OrdinalIgnoreCase);

    public bool HasSavedKey => !string.IsNullOrEmpty(ApiKeyProtected);

    public void SetApiKey(string key)
    {
        key = key.Trim();
        ApiKeyProtected = key.Length == 0
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }

    public static void ApplyStartWithWindows(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            if (on) k.SetValue("AIMouse", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("AIMouse", false);
        }
        catch (Exception ex) { Log.Write("Autostart failed: " + ex.Message); }
    }
}
