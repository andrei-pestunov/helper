#:package DotNetEnv@3.1.0

using System.Security.Cryptography;
using DotNetEnv;

Env.Load();

var apiKey = Environment.GetEnvironmentVariable("APPSFLYER_API_KEY")
    ?? throw new InvalidOperationException("APPSFLYER_API_KEY is not set in .env file");

// Configuration values matching production
const string WEB_TEMPLATE_ID = "Wdn3";
const string APP_TEMPLATE_ID = "EJvU";
const string BRAND_DOMAIN = "l.finom.co";
var DEEP_LINK_DOMAINS = new[] { "app.finom.dev", "app.finom.co" };

using var client = new HttpClient
{
    BaseAddress = new Uri("https://onelink.appsflyer.com/"),
    Timeout = TimeSpan.FromSeconds(30)
};

Console.WriteLine("=================================================");
Console.WriteLine("AppsFlyer Dynamic Links Service - Local Testing");
Console.WriteLine("=================================================\n");

// Test 1: Standard link (openInApp=false)
await RunTest(
    "Test 1: Standard Link (openInApp=false)",
    async () =>
    {
        var testUrl = "https://www.google.com";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Template: {WEB_TEMPLATE_ID} (Web)");
        Console.WriteLine($"OpenInApp: false");

        var isDeepLink = IsDeepLink(testUrl);
        Console.WriteLine($"Deep Link: {isDeepLink}");

        var result = await CreateShortLink(testUrl, WEB_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 2: Deep link (openInApp=true)
await RunTest(
    "Test 2: Deep Link (openInApp=true)",
    async () =>
    {
        var testUrl = "https://app.finom.dev/test-page";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Template: {APP_TEMPLATE_ID} (App)");
        Console.WriteLine($"OpenInApp: true");

        var isDeepLink = IsDeepLink(testUrl);
        Console.WriteLine($"Deep Link: {isDeepLink} (domain is in DeepLinkDomains)");

        var result = await CreateShortLink(testUrl, APP_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 3: CreateLinkForAuthViaTempToken
await RunTest(
    "Test 3: CreateLinkForAuthViaTempToken",
    async () =>
    {
        var tokenKey = "test-token-123";
        var type = "OnbVideoIdentification";
        var company = Guid.NewGuid().ToString();
        var sid = Guid.NewGuid().ToString();
        var id = "PersonVideoIdentification";

        Console.WriteLine($"Token Key: {tokenKey}");
        Console.WriteLine($"Parameters:");
        Console.WriteLine($"  type: {type}");
        Console.WriteLine($"  company: {company}");
        Console.WriteLine($"  sid: {sid}");
        Console.WriteLine($"  id: {id}");

        // Build URL with custom query parameters (matching service implementation)
        var customParams = $"%26type={type}%26company={company}%26sid={sid}%26id={id}";
        var longUrl = $"https://app.finom.co/api/tooling/mobiles/redirectToApp?tk={tokenKey}{customParams}";

        Console.WriteLine($"Built URL: {longUrl}");

        var isDeepLink = IsDeepLink(longUrl);
        var result = await CreateShortLink(longUrl, APP_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 4: Non-deep link with openInApp=true
await RunTest(
    "Test 4: Non-Deep Link with openInApp=true",
    async () =>
    {
        var testUrl = "https://www.example.com/page";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Template: {APP_TEMPLATE_ID} (App)");
        Console.WriteLine($"OpenInApp: true");

        var isDeepLink = IsDeepLink(testUrl);
        Console.WriteLine($"Deep Link: {isDeepLink} (domain not in DeepLinkDomains)");

        var result = await CreateShortLink(testUrl, APP_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 5: Already encoded URL - should NOT double encode
await RunTest(
    "Test 5: Already Encoded URL (should not double-encode)",
    async () =>
    {
        var testUrl = "https://example.com/path?name=John%20Doe";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Expected: URL should remain with %20, not become %2520");
        Console.WriteLine($"Template: {WEB_TEMPLATE_ID} (Web)");

        var isDeepLink = IsDeepLink(testUrl);
        var result = await CreateShortLink(testUrl, WEB_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 6: URL with query parameters - should preserve structure
await RunTest(
    "Test 6: URL with Query Parameters (should preserve &, =, ?)",
    async () =>
    {
        var testUrl = "https://app.finom.co/dashboard?userId=123&tab=overview";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Expected: Query params (?, &, =) should NOT be encoded");
        Console.WriteLine($"Template: {APP_TEMPLATE_ID} (App)");

        var isDeepLink = IsDeepLink(testUrl);
        Console.WriteLine($"Deep Link: {isDeepLink}");
        var result = await CreateShortLink(testUrl, APP_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 7: URL with fragment - should preserve #
await RunTest(
    "Test 7: URL with Fragment (should preserve #)",
    async () =>
    {
        var testUrl = "https://app.finom.co/help#section-billing";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Expected: Fragment (#) should NOT be encoded");
        Console.WriteLine($"Template: {APP_TEMPLATE_ID} (App)");

        var isDeepLink = IsDeepLink(testUrl);
        var result = await CreateShortLink(testUrl, APP_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 8: URL with authentication - should preserve @, :
await RunTest(
    "Test 8: URL with Authentication (should preserve @, :)",
    async () =>
    {
        var testUrl = "https://user:pass@api.finom.co/resource";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Expected: Auth credentials (@, :) should NOT be encoded");
        Console.WriteLine($"Template: {WEB_TEMPLATE_ID} (Web)");

        var isDeepLink = IsDeepLink(testUrl);
        var result = await CreateShortLink(testUrl, WEB_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 9: Deep link scheme - should preserve finom://
await RunTest(
    "Test 9: Deep Link Scheme (should preserve finom://)",
    async () =>
    {
        var testUrl = "finom://app.finom.co/transaction/12345?source=email";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Expected: Custom scheme should work using af_dp parameter");
        Console.WriteLine($"Template: {APP_TEMPLATE_ID} (App)");

        var isDeepLink = IsDeepLink(testUrl);
        Console.WriteLine($"Is Custom Scheme: True");
        var result = await CreateShortLink(testUrl, APP_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 10: URL with paths - should preserve /
await RunTest(
    "Test 10: URL with Paths (should preserve /)",
    async () =>
    {
        var testUrl = "https://app.finom.co/api/tooling/mobiles/redirectToApp?tk=abc123";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Expected: Path separators (/) should NOT be encoded");
        Console.WriteLine($"Template: {APP_TEMPLATE_ID} (App)");

        var isDeepLink = IsDeepLink(testUrl);
        var result = await CreateShortLink(testUrl, APP_TEMPLATE_ID, isDeepLink);
        Console.WriteLine($"Result: {result}");
    }
);

// Test 11: Malformed URL - should fail gracefully
await RunTest(
    "Test 11: Malformed URL (expected to fail)",
    async () =>
    {
        var testUrl = "not a valid url with spaces";
        Console.WriteLine($"Input URL: {testUrl}");
        Console.WriteLine($"Expected: API will reject this invalid URL");

        try
        {
            var isDeepLink = IsDeepLink(testUrl);
            var result = await CreateShortLink(testUrl, WEB_TEMPLATE_ID, isDeepLink);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Unexpected success: {result}");
            Console.ResetColor();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Expected failure: {ex.Message}");
        }
    }
);

Console.WriteLine("\n=================================================");
Console.WriteLine("All tests completed!");
Console.WriteLine("=================================================");

// Helper functions

async Task<string> CreateShortLink(string originalLink, string templateId, bool isDeepLink)
{
    var shortLinkId = CreateShortLinkId();

    var data = new Dictionary<string, string>
    {
        ["pid"] = "internal",
    };

    // Custom schemes (finom://, etc.) should ONLY use af_dp
    if (IsCustomScheme(originalLink))
    {
        data["af_dp"] = originalLink;
    }
    else
    {
        data["af_web_dp"] = originalLink;

        if (isDeepLink)
        {
            data["af_dp"] = originalLink;
        }
        else
        {
            data["af_android_url"] = originalLink;
            data["af_ios_url"] = originalLink;
        }
    }

    Console.WriteLine($"Request: POST /api/v2.0/shortlinks/{templateId}");
    Console.WriteLine($"Short Link ID: {shortLinkId}");

    // Build JSON manually to avoid reflection issues in .NET 9/10
    var dataJson = string.Join(",", data.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\""));
    var jsonBody = $$"""
    {
        "shortlink_id": "{{shortLinkId}}",
        "brand_domain": "{{BRAND_DOMAIN}}",
        "data": {
            {{dataJson}}
        }
    }
    """;

    using var request = new HttpRequestMessage
    {
        Method = HttpMethod.Post,
        RequestUri = new Uri($"/api/v2.0/shortlinks/{templateId}", UriKind.Relative),
        Headers =
        {
            { "accept", "application/json" },
            { "authorization", apiKey },
        },
        Content = new StringContent(jsonBody)
        {
            Headers = { ContentType = new("application/json") }
        }
    };

    using var response = await client.SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new HttpRequestException(
            $"API Error: {response.StatusCode} - {content}");
    }

    return content;
}

bool IsDeepLink(string link)
{
    if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
        return false;

    return DEEP_LINK_DOMAINS.Any(domain =>
        string.Equals(uri.Host, domain, StringComparison.OrdinalIgnoreCase));
}

bool IsCustomScheme(string link)
{
    if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
        return false;

    return !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
}

string CreateShortLinkId()
{
    const string CHARACTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    const int ID_LENGTH = 4;

    var bytes = new byte[ID_LENGTH];
    var result = new char[ID_LENGTH];

    RandomNumberGenerator.Fill(bytes);

    for (var i = 0; i < ID_LENGTH; i++)
    {
        result[i] = CHARACTERS[bytes[i] % CHARACTERS.Length];
    }

    return new string(result);
}

async Task RunTest(string testName, Func<Task> testAction)
{
    Console.WriteLine($"\n=== {testName} ===");
    try
    {
        await testAction();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Success");
        Console.ResetColor();
    }
    catch (HttpRequestException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ Failed: {ex.Message}");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ Unexpected Error: {ex.Message}");
        Console.WriteLine($"  Type: {ex.GetType().Name}");
        if (ex.StackTrace != null)
        {
            Console.WriteLine($"  Stack: {ex.StackTrace}");
        }
        Console.ResetColor();
    }
}
