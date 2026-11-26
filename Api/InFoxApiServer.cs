using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private WebApplication? _app;
        private CancellationTokenSource? _cts;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private volatile bool _isRunning = false;

        public bool IsRunning => _isRunning;
        public string? LastError { get; private set; }
        public event Action<bool, string?>? StatusChanged;

        private InFoxApiServer() { }

        public async Task StartAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StartCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task StopAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task RestartAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
                await StartCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private async Task StartCoreAsync()
        {
            if (_isRunning && _app != null) return;

            var config = ConfigManager.Instance.Config;
            if (!config.EnableApi)
            {
                LastError = null;
                LoggingService.Instance.Info("ApiServer", "Open API is disabled in configuration.");
                NotifyStatusChanged();
                return;
            }

            WebApplication? app = null;
            CancellationTokenSource? cts = null;
            string url = string.Empty;

            try
            {
                cts = new CancellationTokenSource();
                int port = config.ApiPort > 0 ? config.ApiPort : 8765;
                string host = config.ApiBindLocalhostOnly ? "127.0.0.1" : "*";
                url = $"http://{host}:{port}";

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

                app = builder.Build();

                app.UseCors();
                app.UseWebSockets();

                ConfigureEndpoints(app, cts.Token);

                LoggingService.Instance.Info("ApiServer", "Starting API server...");
                await app.StartAsync(cts.Token).ConfigureAwait(false);

                _app = app;
                _cts = cts;
                _isRunning = true;
                LastError = null;

                LoggingService.Instance.Info("ApiServer", $"inFaux Open API is live at {url}");
                NotifyStatusChanged();
            }
            catch (Exception ex)
            {
                _isRunning = false;
                LastError = $"Failed to start API at {(string.IsNullOrEmpty(url) ? "configured port" : url)}: {ex.Message}";
                LoggingService.Instance.Error("ApiServer", LastError, ex);

                if (app != null)
                {
                    try { await app.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                try { cts?.Dispose(); } catch { }

                NotifyStatusChanged();
            }
        }

        private async Task StopCoreAsync()
        {
            var app = _app;
            var cts = _cts;
            _app = null;
            _cts = null;
            _isRunning = false;

            if (app == null)
            {
                try { cts?.Dispose(); } catch { }
                return;
            }

            try
            {
                // Signal active WebSocket loops to close immediately so Kestrel doesn't stall on shutdown
                try { cts?.Cancel(); } catch { }

                using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await app.StopAsync(shutdownTimeout.Token).ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
                LoggingService.Instance.Info("ApiServer", "API server stopped.");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ApiServer", "Error stopping API server", ex);
                try { await app.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            finally
            {
                try { cts?.Dispose(); } catch { }
                NotifyStatusChanged();
            }
        }

        private void NotifyStatusChanged()
        {
            try
            {
                StatusChanged?.Invoke(_isRunning, LastError);
            }
            catch { }
        }

        private void ConfigureEndpoints(WebApplication app, CancellationToken serverShutdownToken)
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
            }, _jsonOptions));

            // Health check
            app.MapGet("/api/v1/health", () => Results.Json(new
            {
                status = "ok",
                uptimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds,
                timestamp = DateTime.UtcNow
            }, _jsonOptions));

            // Curated Human-first Summary (CPU, GPU, RAM, Storage, Battery, Network)
            app.MapGet("/api/v1/summary", () =>
            {
                try
                {
                    var summary = TelemetryEngine.Instance.CurrentSummary;
                    return Results.Json(summary, _jsonOptions);
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("ApiServer", "Error serializing /api/v1/summary", ex);
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            });

            // Full raw sensors collection
            app.MapGet("/api/v1/sensors", () =>
            {
                try
                {
                    var metrics = TelemetryEngine.Instance.CurrentMetrics;
                    return Results.Json(metrics, _jsonOptions);
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("ApiServer", "Error serializing /api/v1/sensors", ex);
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            });

            // Filtered sensors by category (e.g. /api/v1/sensors/cpu)
            app.MapGet("/api/v1/sensors/{category}", (string category) =>
            {
                try
                {
                    if (Enum.TryParse<HardwareCategory>(category, true, out var cat))
                    {
                        var filtered = new System.Collections.Generic.List<SensorMetric>();
                        foreach (var m in TelemetryEngine.Instance.CurrentMetrics)
                        {
                            if (m.Category == cat) filtered.Add(m);
                        }
                        return Results.Json(filtered, _jsonOptions);
                    }
                    return Results.BadRequest(new { error = $"Unknown category '{category}'" });
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("ApiServer", $"Error serializing /api/v1/sensors/{category}", ex);
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            });

            // Metric history for sparklines
            app.MapGet("/api/v1/history/{metricId}", (string metricId) =>
            {
                try
                {
                    var history = TelemetryEngine.Instance.GetHistory(metricId);
                    return Results.Json(new
                    {
                        metricId,
                        count = history.Length,
                        values = history
                    }, _jsonOptions);
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("ApiServer", $"Error serializing /api/v1/history/{metricId}", ex);
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            });

            // Real-time WebSocket stream (Supports optional ?channel=cpu|gpu|memory|storage|network filter)
            app.Map("/api/v1/stream", async (HttpContext context) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    string? channel = context.Request.Query["channel"].ToString();
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, serverShutdownToken);
                    await HandleWebSocketStreamAsync(webSocket, channel, linkedCts.Token);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });
        }

        private static async Task HandleWebSocketStreamAsync(WebSocket ws, string? channel, CancellationToken ct)
        {
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

                    string json = JsonSerializer.Serialize(payload, _jsonOptions);
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
                var app = _app;
                var cts = _cts;
                _app = null;
                _cts = null;
                _isRunning = false;

                try { cts?.Cancel(); } catch { }
                app?.DisposeAsync().AsTask().Wait(500);
                try { cts?.Dispose(); } catch { }
            }
            catch { }
        }
    }
}
