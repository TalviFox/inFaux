using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InFox.Engine;
using InFox.Models.Sensors;
using InFox.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InFox.Api
{
    public class InFoxApiServer : IDisposable
    {
        private static readonly Lazy<InFoxApiServer> _instance = new(() => new InFoxApiServer());
        public static InFoxApiServer Instance => _instance.Value;

        private WebApplication? _app;
        private CancellationTokenSource? _cts;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private bool _isRunning = false;

        public bool IsRunning => _isRunning;

        private InFoxApiServer() { }

        public async Task StartAsync()
        {
            if (_isRunning) return;

            var config = ConfigManager.Instance.Config;
            if (!config.EnableApi)
            {
                LoggingService.Instance.Info("ApiServer", "Open API is disabled in configuration.");
                return;
            }

            try
            {
                _cts = new CancellationTokenSource();
                int port = config.ApiPort > 0 ? config.ApiPort : 8765;
                string host = config.ApiBindLocalhostOnly ? "127.0.0.1" : "*";
                string url = $"http://{host}:{port}";

                LoggingService.Instance.Info("ApiServer", $"Configuring embedded Kestrel server at {url}...");

                var builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders(); // Keep console/debug clean
                builder.WebHost.UseUrls(url);

                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy =>
                    {
                        policy.AllowAnyOrigin().AllowAnyHeader().WithMethods("GET");
                    });
                });

                _app = builder.Build();

                _app.UseCors();
                _app.UseWebSockets();

                ConfigureEndpoints(_app);

                LoggingService.Instance.Info("ApiServer", "Starting API server...");
                await _app.StartAsync(_cts.Token);
                _isRunning = true;
                LoggingService.Instance.Info("ApiServer", $"inFaux Open API is live at {url}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ApiServer", "Failed to start API server", ex);
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning || _app == null) return;
            _isRunning = false;

            try
            {
                _cts?.Cancel();
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
                LoggingService.Instance.Info("ApiServer", "API server stopped.");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ApiServer", "Error stopping API server", ex);
            }
        }

        private void ConfigureEndpoints(WebApplication app)
        {
            // Root overview
            app.MapGet("/", () => Results.Json(new
            {
                name = "inFaux Telemetry Server",
                author = "FoxDen Software",
                version = UpdateService.Instance.GetCurrentVersionString(),
                endpoints = new[]
                {
                    "/api/v1/summary",
                    "/api/v1/sensors",
                    "/api/v1/sensors/{category}",
                    "/api/v1/history/{metricId}",
                    "/api/v1/health",
                    "/api/v1/stream (WebSocket)"
                }
            }));

            // Health check
            app.MapGet("/api/v1/health", () => Results.Json(new
            {
                status = "ok",
                uptimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds,
                timestamp = DateTime.UtcNow
            }));

            // Curated Human-first Summary (CPU, GPU, RAM, Storage, Battery, Network)
            app.MapGet("/api/v1/summary", () =>
            {
                var summary = TelemetryEngine.Instance.CurrentSummary;
                return Results.Json(summary);
            });

            // Full raw sensors collection
            app.MapGet("/api/v1/sensors", () =>
            {
                var metrics = TelemetryEngine.Instance.CurrentMetrics;
                return Results.Json(metrics);
            });

            // Filtered sensors by category (e.g. /api/v1/sensors/cpu)
            app.MapGet("/api/v1/sensors/{category}", (string category) =>
            {
                if (Enum.TryParse<HardwareCategory>(category, true, out var cat))
                {
                    var filtered = new System.Collections.Generic.List<SensorMetric>();
                    foreach (var m in TelemetryEngine.Instance.CurrentMetrics)
                    {
                        if (m.Category == cat) filtered.Add(m);
                    }
                    return Results.Json(filtered);
                }
                return Results.BadRequest(new { error = $"Unknown category '{category}'" });
            });

            // Metric history for sparklines
            app.MapGet("/api/v1/history/{metricId}", (string metricId) =>
            {
                var history = TelemetryEngine.Instance.GetHistory(metricId);
                return Results.Json(new
                {
                    metricId,
                    count = history.Length,
                    values = history
                });
            });

            // Real-time WebSocket stream (Supports optional ?channel=cpu|gpu|memory|storage|network filter)
            app.Map("/api/v1/stream", async (HttpContext context) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    string? channel = context.Request.Query["channel"].ToString();
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await HandleWebSocketStreamAsync(webSocket, channel, context.RequestAborted);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });
        }

        private static async Task HandleWebSocketStreamAsync(WebSocket ws, string? channel, CancellationToken ct)
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            string normalizedChannel = (channel ?? "").Trim().ToLowerInvariant();

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var summary = TelemetryEngine.Instance.CurrentSummary;
                    object payload = normalizedChannel switch
                    {
                        "cpu" => new { timestamp = summary.Timestamp, cpu = summary.Cpu },
                        "gpu" or "gpus" => new { timestamp = summary.Timestamp, gpus = summary.Gpus, primaryGpu = summary.PrimaryGpu },
                        "ram" or "memory" => new { timestamp = summary.Timestamp, memory = summary.Memory },
                        "storage" or "disk" => new { timestamp = summary.Timestamp, storage = summary.Storage },
                        "network" or "net" => new { timestamp = summary.Timestamp, network = summary.Network },
                        "battery" or "power" => new { timestamp = summary.Timestamp, battery = summary.Battery },
                        "chassis" => new { timestamp = summary.Timestamp, chassis = summary.Chassis },
                        "bluetooth" or "ble" => new { timestamp = summary.Timestamp, bluetooth = summary.Bluetooth, chassis = summary.Chassis },
                        _ => summary
                    };

                    string json = JsonSerializer.Serialize(payload, options);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);

                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

                    // Client streaming interval default 500ms
                    await Task.Delay(500, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
                _isRunning = false;
                _app?.DisposeAsync().AsTask().Wait(500);
            }
            catch { }
        }
    }
}
