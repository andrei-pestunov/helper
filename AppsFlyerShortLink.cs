#:package DotNetEnv@3.1.0

using System.Security.Cryptography;
using DotNetEnv;

Env.Load();

var API_KEY = Environment.GetEnvironmentVariable("APPSFLYER_API_KEY") ?? throw new InvalidOperationException("APPSFLYER_API_KEY is not set");

using var client = new HttpClient
{
    BaseAddress = new Uri("https://onelink.appsflyer.com/"),
    Timeout = TimeSpan.FromSeconds(30)
};

var link = Uri.EscapeDataString("https://www.google.com");

var request = new HttpRequestMessage
{
    Method = HttpMethod.Post,
    RequestUri = new Uri($"/shortlink/v1/hkS0?id={new ShortLinkId(4)}", UriKind.Relative),
    Headers =
    {
        { "accept", "application/json" },
        { "authorization", API_KEY },
    },
    Content = new StringContent($$"""
    {
        "brand_domain": "l.finom.dev",
        "data": {
            "af_web_dp": "{{link}}",
            "af_android_url": "{{link}}",
            "af_ios_url": "{{link}}",
            "pid": "internal"
        }
    }
    """)
    {
        Headers = { ContentType = new("application/json") }
    }
};

try
{
    Console.WriteLine($"Requested URL: {request.RequestUri}");
    using var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var shortUrl = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Response URL: {shortUrl}");
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

record ShortLinkId
{
    private const string CHARACTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"; // 62 ^ length combinations
    private readonly int _length;
    public ShortLinkId(int length)
    {
        if (length < 2)
            throw new ArgumentOutOfRangeException(nameof(length), "ShortLinkId length must be at least 2");
        _length = length;
    }

    public override string ToString()
    {
        var bytes = new byte[_length];
        var result = new char[_length];

        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < _length; i++)
        {
            result[i] = CHARACTERS[bytes[i] % CHARACTERS.Length];
        }

        return new string(result);
    }
}