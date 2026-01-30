#:package Microsoft.Extensions.Http@9.0.0
#:package Microsoft.Extensions.Http.Resilience@9.0.0
#:package Microsoft.Extensions.DependencyInjection@9.0.0
#:package Polly@8.4.1

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

const string BASE_URL = "https://api.refiner.io";
const string API_KEY = "96051c73-4374-414b-9a63-733ce7a72ecd";

const int TOTAL_REQUESTS = 2000;
const int PARALLELISM = 50;
const string EVENT_NAME = "LoadTest Event";

// ---------- DI + CLIENTS ----------
var services = new ServiceCollection();

// OLD CLIENT (baseline)
services.AddHttpClient("old", c =>
{
    c.BaseAddress = new Uri(BASE_URL);
    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", API_KEY);
})
.AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.Delay = TimeSpan.FromSeconds(2);
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
});

// NEW CLIENT (fixed)
services.AddHttpClient("new", c =>
{
    c.BaseAddress = new Uri(BASE_URL);
    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", API_KEY);
})
.AddStandardResilienceHandler(o =>
{
    // Client-side throttling (per instance)
    o.RateLimiter.DefaultRateLimiterOptions.PermitLimit = 5;
    o.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 150;

    // Retry transient failures + 429
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.Delay = TimeSpan.FromSeconds(3);
    o.Retry.BackoffType = DelayBackoffType.Exponential;
    o.Retry.UseJitter = true;
    o.Retry.ShouldHandle = a =>
        ValueTask.FromResult(
            HttpClientResiliencePredicates.IsTransient(a.Outcome) ||
            a.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests);

    // Circuit breaker ignores 429
    o.CircuitBreaker.ShouldHandle = a =>
        ValueTask.FromResult(
            a.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests &&
            HttpClientResiliencePredicates.IsTransient(a.Outcome));

    o.CircuitBreaker.FailureRatio = 0.7;
    o.CircuitBreaker.MinimumThroughput = 20;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
});

using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<IHttpClientFactory>();

// ---------- RUN ----------
var oldStats = await RunTest(factory.CreateClient("old"), clientLabel: "OLD");
await Task.Delay(3000);
var newStats = await RunTest(factory.CreateClient("new"), clientLabel: "NEW");

// ---------- PRINT ONE SUMMARY ----------
PrintStats(oldStats);
Console.WriteLine();
PrintStats(newStats);

// ---------------- HELPERS ----------------

static async Task<TestStats> RunTest(HttpClient client, string clientLabel)
{
    var stats = new TestStats(clientLabel, TOTAL_REQUESTS, PARALLELISM);
    var semaphore = new SemaphoreSlim(PARALLELISM);

    var tasks = new List<Task>(TOTAL_REQUESTS);
    var totalSw = Stopwatch.StartNew();

    for (int i = 0; i < TOTAL_REQUESTS; i++)
    {
        await semaphore.WaitAsync();

        var index = i;
        tasks.Add(Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();

            try
            {
                using var resp = await client.PostAsync(
                    "/v1/track-event",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["id"] = $"test-user-{index}",
                        ["event"] = EVENT_NAME
                    }));

                sw.Stop();
                stats.RecordResponse(resp.StatusCode, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                stats.RecordException(ex, sw.ElapsedMilliseconds);
            }
            finally
            {
                semaphore.Release();
            }
        }));
    }

    await Task.WhenAll(tasks);
    totalSw.Stop();
    stats.TotalElapsedMs = totalSw.ElapsedMilliseconds;

    return stats;
}

static void PrintStats(TestStats s)
{
    Console.WriteLine($"========== {s.Label} CLIENT SUMMARY ==========");
    Console.WriteLine($"Total requests: {s.TotalRequests}");
    Console.WriteLine($"Parallelism:    {s.Parallelism}");
    Console.WriteLine($"Duration:       {s.TotalElapsedMs} ms");

    Console.WriteLine($"2xx:            {s.Success2xx}");
    Console.WriteLine($"202:            {s.Accepted202}");
    Console.WriteLine($"429:            {s.TooManyRequests429}");
    Console.WriteLine($"Other non-2xx:  {s.OtherNon2xx}");
    Console.WriteLine($"Exceptions:     {s.Exceptions}");

    Console.WriteLine($"Latency (ms):   avg={s.AvgLatencyMs:F1}  p50={s.P50LatencyMs:F1}  p95={s.P95LatencyMs:F1}  p99={s.P99LatencyMs:F1}  max={s.MaxLatencyMs}");
    Console.WriteLine($"Throughput:     {s.RequestsPerSecond:F1} req/s");
}

sealed class TestStats
{
    public string Label { get; }
    public int TotalRequests { get; }
    public int Parallelism { get; }
    public long TotalElapsedMs { get; set; }

    private long _latencySum;
    private long _latencyCount;
    public long MaxLatencyMs { get; private set; }

    // We keep latencies to compute percentiles once at the end.
    // 2000 items is small; if you increase a lot, consider reservoir sampling.
    private readonly long[] _latencies;
    private int _latIdx;

    public long Success2xx;
    public long Accepted202;
    public long TooManyRequests429;
    public long OtherNon2xx;
    public long Exceptions;

    public TestStats(string label, int totalRequests, int parallelism)
    {
        Label = label;
        TotalRequests = totalRequests;
        Parallelism = parallelism;
        _latencies = new long[totalRequests];
    }

    public void RecordResponse(HttpStatusCode code, long latencyMs)
    {
        RecordLatency(latencyMs);

        var c = (int)code;
        if (c >= 200 && c <= 299)
        {
            System.Threading.Interlocked.Increment(ref Success2xx);
            if (code == HttpStatusCode.Accepted)
                System.Threading.Interlocked.Increment(ref Accepted202);
            return;
        }

        if (code == HttpStatusCode.TooManyRequests)
        {
            System.Threading.Interlocked.Increment(ref TooManyRequests429);
            return;
        }

        System.Threading.Interlocked.Increment(ref OtherNon2xx);
    }

    public void RecordException(Exception _, long latencyMs)
    {
        RecordLatency(latencyMs);
        System.Threading.Interlocked.Increment(ref Exceptions);
    }

    private void RecordLatency(long latencyMs)
    {
        System.Threading.Interlocked.Add(ref _latencySum, latencyMs);
        System.Threading.Interlocked.Increment(ref _latencyCount);

        // Max latency (race is fine; eventual max is good enough)
        var currentMax = MaxLatencyMs;
        if (latencyMs > currentMax)
            MaxLatencyMs = latencyMs;

        // Store latency (lock-free index increment)
        var idx = System.Threading.Interlocked.Increment(ref _latIdx) - 1;
        if ((uint)idx < (uint)_latencies.Length)
            _latencies[idx] = latencyMs;
    }

    public double AvgLatencyMs => _latencyCount == 0 ? 0 : (double)_latencySum / _latencyCount;

    public double P50LatencyMs => Percentile(0.50);
    public double P95LatencyMs => Percentile(0.95);
    public double P99LatencyMs => Percentile(0.99);

    public double RequestsPerSecond =>
        TotalElapsedMs <= 0 ? 0 : TotalRequests / (TotalElapsedMs / 1000.0);

    private double Percentile(double p)
    {
        var n = Math.Min(_latIdx, _latencies.Length);
        if (n <= 0) return 0;

        var copy = new long[n];
        Array.Copy(_latencies, copy, n);
        Array.Sort(copy);

        var rank = (int)Math.Ceiling(p * n) - 1;
        if (rank < 0) rank = 0;
        if (rank >= n) rank = n - 1;

        return copy[rank];
    }
}
