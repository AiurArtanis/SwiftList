using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.Translation;

public class TranslationInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("Translation_ProviderName");

    public string Description => TranslationService.Get("Plugin_Comp_Desc_TranslationInstantProvider");

    // Falls back to the default even if an empty string was already persisted before RequireNonEmpty
    // started enforcing this at save time -- an empty keyword should never silently make this
    // unreachable.
    private static string GetTriggerKeyword()
    {
        var value = PluginSettingsService.GetSetting("SwiftList.Plugins.Translation", "TriggerKeyword", "tr");
        return string.IsNullOrWhiteSpace(value) ? "tr" : value;
    }

    private static readonly HttpClient HttpClient = new HttpClient();
    private static readonly HashSet<string> PendingRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim TokenSemaphore = new SemaphoreSlim(1, 1);

    private static string? _tokenCache;
    private static DateTime _tokenExpireTime = DateTime.MinValue;
    private static readonly Dictionary<string, string> TranslationCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DetectedLanguageCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    static TranslationInstantProvider()
    {
        try
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");
        }
        catch { }
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrEmpty(query))
            yield break;

        var keyword = GetTriggerKeyword();
        if (string.IsNullOrWhiteSpace(keyword))
            yield break;

        if (!query.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
            yield break;

        var textToTranslate = query.Substring(keyword.Length + 1).Trim();
        if (string.IsNullOrEmpty(textToTranslate))
        {
            yield return new InstantResultItem
            {
                Title = TranslationService.Format("Translation_PlaceholderTitle", keyword),
                Description = TranslationService.Get("Translation_PlaceholderDesc"),
                IconData = "M12.87 15.07l-2.54-2.51.03-.03c1.74-1.94 2.98-4.17 3.71-6.53H17V4h-7V2H8v2H1v2h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z",
                IconColor = "#3399FF",
                ActionType = "None"
            };
            yield break;
        }

        string? resultText = null;
        var detectedLang = "Unknown";

        lock (TranslationCache)
        {
            if (TranslationCache.TryGetValue(textToTranslate, out var cachedResult))
            {
                resultText = cachedResult;
                if (DetectedLanguageCache.TryGetValue(textToTranslate, out var cachedLang))
                {
                    detectedLang = cachedLang;
                }
            }
        }

        if (resultText != null)
        {
            yield return new InstantResultItem
            {
                Title = resultText,
                Description = TranslationService.Format("Translation_ResultDesc", detectedLang.ToUpper()),
                IconData = "M12.87 15.07l-2.54-2.51.03-.03c1.74-1.94 2.98-4.17 3.71-6.53H17V4h-7V2H8v2H1v2h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z",
                IconColor = "#3399FF",
                ActionType = "Copy",
                ActionArgument = resultText,
                TabCompletion = resultText
            };
        }
        else
        {
            var shouldTrigger = false;
            lock (PendingRequests)
            {
                if (!PendingRequests.Contains(textToTranslate))
                {
                    PendingRequests.Add(textToTranslate);
                    shouldTrigger = true;
                }
            }

            if (shouldTrigger)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var (translated, lang) = await TranslateTextAsync(textToTranslate);
                        lock (TranslationCache)
                        {
                            TranslationCache[textToTranslate] = translated;
                            DetectedLanguageCache[textToTranslate] = lang;
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (TranslationCache)
                        {
                            TranslationCache[textToTranslate] = $"Translation error: {ex.Message}";
                            DetectedLanguageCache[textToTranslate] = "Unknown";
                        }
                    }
                    finally
                    {
                        lock (PendingRequests)
                        {
                            PendingRequests.Remove(textToTranslate);
                        }
                    }

                    // Re-trigger any active search whose current query is still this same "<keyword> <text>" request.
                    SearchRefreshService.RefreshIfMatches(currentQueryText =>
                        currentQueryText.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(currentQueryText.Substring(keyword.Length + 1).Trim(), textToTranslate, StringComparison.OrdinalIgnoreCase));
                });
            }

            yield return new InstantResultItem
            {
                Title = TranslationService.Format("Translation_PlaceholderTitle", keyword),
                Description = TranslationService.Get("Translation_PlaceholderDesc"),
                IconData = "M12.87 15.07l-2.54-2.51.03-.03c1.74-1.94 2.98-4.17 3.71-6.53H17V4h-7V2H8v2H1v2h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z",
                IconColor = "#3399FF",
                ActionType = "None"
            };
        }
    }

    private static async Task<(string Translation, string DetectedLang)> TranslateTextAsync(string text)
    {
        var token = await GetAuthTokenAsync();

        var appCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var targetLang = "en";
        if (appCulture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            targetLang = appCulture.Contains("TW") || appCulture.Contains("HK") || appCulture.Contains("Hant") ? "zh-Hant" : "zh-Hans";
        }
        else
        {
            var dashIdx = appCulture.IndexOf('-');
            targetLang = dashIdx > 0 ? appCulture.Substring(0, dashIdx) : appCulture;
        }

        // Translate to both targetLang and English to determine auto-reversing
        var url = $"https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to={targetLang}&to=en";

        using (var request = new HttpRequestMessage(HttpMethod.Post, url))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var body = new[] { new { Text = text } };
            var jsonBody = JsonSerializer.Serialize(body);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var jsonResponse = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var element = root[0];
                var detected = "unknown";
                if (element.TryGetProperty("detectedLanguage", out var detProperty) &&
                    detProperty.TryGetProperty("language", out var langProperty))
                {
                    detected = langProperty.GetString() ?? "unknown";
                }

                var translatedText = string.Empty;
                var fallbackText = string.Empty;

                if (element.TryGetProperty("translations", out var transProperty) &&
                    transProperty.ValueKind == JsonValueKind.Array)
                {
                    foreach (var trans in transProperty.EnumerateArray())
                    {
                        var to = trans.TryGetProperty("to", out var toProp) ? toProp.GetString() ?? "" : "";
                        var textVal = trans.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";

                        if (to == targetLang)
                        {
                            translatedText = textVal;
                        }
                        else if (to == "en")
                        {
                            fallbackText = textVal;
                        }
                    }
                }

                // If the detected language matches the app language, show the English version instead
                if (detected.StartsWith(targetLang, StringComparison.OrdinalIgnoreCase) ||
                    (targetLang == "zh-Hans" && detected.StartsWith("zh", StringComparison.OrdinalIgnoreCase)))
                {
                    return (fallbackText, detected);
                }

                return (translatedText, detected);
            }
        }

        return ("Translation failed", "unknown");
    }

    private static async Task<string> GetAuthTokenAsync()
    {
        if (_tokenCache != null && DateTime.UtcNow < _tokenExpireTime)
        {
            return _tokenCache;
        }

        await TokenSemaphore.WaitAsync();
        try
        {
            if (_tokenCache != null && DateTime.UtcNow < _tokenExpireTime)
            {
                return _tokenCache;
            }

            // Fetch Edge translate token (returns plain text JWT)
            var token = await HttpClient.GetStringAsync("https://edge.microsoft.com/translate/auth");
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new Exception("Empty token returned");
            }
            _tokenCache = token.Trim();
            _tokenExpireTime = DateTime.UtcNow.AddMinutes(5);
            return _tokenCache;
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to retrieve translator credentials: " + ex.Message);
        }
        finally
        {
            TokenSemaphore.Release();
        }
    }

    public bool[]? GetHighlightMask(string text, string query) =>
        // Translation text does not share characters with query, return empty mask to prevent fuzzy matching
        new bool[text.Length];
}
