#:package DotNetEnv@3.1.0

using DotNetEnv;

Env.Load();

var API_KEY = Environment.GetEnvironmentVariable("REFINER_API_KEY") ?? throw new InvalidOperationException("REFINER_API_KEY is not set");
const string USER_ID = "test-user-id";
const string EVENT_NAME = "Card ordering: New card created";

using var client = new HttpClient
{
    BaseAddress = new Uri("https://api.refiner.io"),
    Timeout = TimeSpan.FromSeconds(30)
};

client.DefaultRequestHeaders.Add("Authorization", $"Bearer {API_KEY}");

try
{
    Console.WriteLine($"Sending Refiner event:");
    Console.WriteLine($"  User ID: {USER_ID}");
    Console.WriteLine($"  Event: {EVENT_NAME}");
    Console.WriteLine($"  URL: {client.BaseAddress}/v1/track-event");
    Console.WriteLine();

    var response = await client.PostAsync("/v1/track-event", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        { "id", USER_ID },
        { "event", EVENT_NAME }
    }));
    
    var payload = await response.Content.ReadAsStringAsync();
    
    Console.WriteLine($"Status Code: {response.StatusCode}");
    Console.WriteLine($"Response: {payload}");

    if (response.IsSuccessStatusCode)
    {
        Console.WriteLine("✓ Event tracked successfully!");
    }
    else
    {
        Console.WriteLine($"✗ Failed to track event. Status: {response.StatusCode}");
    }
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"✗ HTTP Error: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
}
