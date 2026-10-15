using libCommon;
using libCommon.Vfs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace libSMB
{
    //The "bring your own VFS" HTTP service that portable-smb-server reads the share from: GET
    ///capabilities, /stat, /list and /read on a loopback port chosen by the OS. Writes are not
    //handled. The share declares itself read-only, which is what makes the server refuse a write,
    //a delete or a rename up front ("the media is write protected") rather than at the close of
    //the handle, where Windows ignores the answer and a delete would look successful. The files
    //themselves are not marked read-only (that is a separate, per-file flag). Every request must
    //carry the per-launch token in X-Vfs-Token, so nothing else on the machine can read the tree
    //through this port.
    public sealed class VfsEndpoint : IDisposable
    {
        public const string TokenHeader = "X-Vfs-Token";

        //the server asks for at most its 1 MiB read-ahead window; anything bigger is a bad request
        const int MaxReadLength = 8 * 1024 * 1024;

        static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        readonly WebApplication app;
        readonly SpoolCache spoolCache;
        readonly string shareName;
        volatile IVfsSource? source;

        //Different on every start
        public string Token { get; }

        public int Port { get; private set; }

        public string BaseUrl => $"http://127.0.0.1:{Port}";

        //The tree being served; null until the session has loaded it (an empty root meanwhile)
        public IVfsSource? Source
        {
            get => source;
            set => source = value;
        }

        VfsEndpoint(string shareName, SpoolCache spoolCache)
        {
            this.shareName = shareName;
            this.spoolCache = spoolCache;
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = "Mount3DX",
                EnvironmentName = "Production",
                ContentRootPath = AppContext.BaseDirectory,
            });

            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddProvider(new LogFileProvider());
            builder.WebHost.UseSetting(WebHostDefaults.SuppressStatusMessagesKey, "true");

            //the default lifetime listens for console signals, which a desktop app has no use for
            builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime, SilentLifetime>());

            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.AddServerHeader = false;
                kestrel.Listen(IPAddress.Loopback, 0);
            });

            app = builder.Build();
            app.Run(HandleAsync);
        }

        public static VfsEndpoint Start(string shareName, SpoolCache spoolCache)
        {
            var endpoint = new VfsEndpoint(shareName, spoolCache);
            try
            {
                //Start (not Run): returns once Kestrel is listening, and throws if it can't. Run on
                //the thread pool so a caller with a SynchronizationContext can't deadlock it.
                Task.Run(() => endpoint.app.StartAsync()).GetAwaiter().GetResult();

                var address = endpoint.app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                                ?? throw new InvalidOperationException("Kestrel reported no address for the VFS endpoint.");
                endpoint.Port = new Uri(address).Port;
            }
            catch
            {
                endpoint.Dispose();
                throw;
            }

            Log.WriteLine($"VFS endpoint listening on {endpoint.BaseUrl}");
            return endpoint;
        }

        public void Dispose()
        {
            try
            {
                Task.Run(async () =>
                {
                    await app.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await app.DisposeAsync().ConfigureAwait(false);
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while stopping the VFS endpoint: {ex.Message}");
            }
        }

        async Task HandleAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;

            if (!string.Equals(context.Request.Headers[TokenHeader].ToString(), Token, StringComparison.Ordinal))
            {
                await ErrorAsync(context, StatusCodes.Status401Unauthorized, $"missing or wrong {TokenHeader}", cancellationToken).ConfigureAwait(false);
                return;
            }

            var endpoint = (context.Request.Path.Value ?? "").Trim('/');
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                //the write endpoints (create, write, mkdir, rename, remove, truncate, chtimes),
                //which the server doesn't call on a read-only share; a backstop
                await ErrorAsync(context, StatusCodes.Status405MethodNotAllowed, "writes are not supported", cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                if (endpoint == "capabilities")
                {
                    await JsonAsync(context, new Capabilities(shareName, ReadOnly: true, CaseInsensitive: true), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (endpoint is not ("stat" or "list" or "read"))
                {
                    await ErrorAsync(context, StatusCodes.Status404NotFound, "no such endpoint", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!TryGetPath(context, out var path))
                {
                    await ErrorAsync(context, StatusCodes.Status400BadRequest, "bad path", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var tree = source ?? EmptySource.Instance;

                switch (endpoint)
                {
                    case "stat":
                        {
                            var entry = tree.Stat(path);
                            if (entry == null)
                            {
                                await ErrorAsync(context, StatusCodes.Status404NotFound, "not found", cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            await JsonAsync(context, ToFileInfo(entry), cancellationToken).ConfigureAwait(false);
                            return;
                        }

                    case "list":
                        {
                            var entries = tree.List(path);
                            if (entries == null)
                            {
                                await ErrorAsync(context, StatusCodes.Status404NotFound, "not a directory", cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            await JsonAsync(context, new DirEntries(entries.Select(ToFileInfo).ToList()), cancellationToken).ConfigureAwait(false);
                            return;
                        }

                    case "read":
                        await ReadAsync(context, tree, path, cancellationToken).ConfigureAwait(false);
                        return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                //the server gave up on the request (a client went away)
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                Log.WriteLine($"VFS endpoint: {context.Request.Method} /{endpoint} failed: {ex.Message}");
                await ErrorAsync(context, StatusCodes.Status500InternalServerError, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        async Task ReadAsync(HttpContext context, IVfsSource tree, string path, CancellationToken cancellationToken)
        {
            if (!long.TryParse(context.Request.Query["offset"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) || offset < 0
                || !int.TryParse(context.Request.Query["length"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length < 0 || length > MaxReadLength)
            {
                await ErrorAsync(context, StatusCodes.Status400BadRequest, "bad offset/length", cancellationToken).ConfigureAwait(false);
                return;
            }

            var entry = tree.Stat(path);
            if (entry == null)
            {
                await ErrorAsync(context, StatusCodes.Status404NotFound, "not found", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (entry.IsDirectory)
            {
                await ErrorAsync(context, StatusCodes.Status400BadRequest, "is a directory", cancellationToken).ConfigureAwait(false);
                return;
            }

            var size = (long)entry.Size;
            var want = (int)Math.Max(0, Math.Min(length, size - offset));

            context.Response.ContentType = "application/octet-stream";

            if (want == 0)
            {
                context.Response.ContentLength = 0;
                return;
            }

            //the path alone would serve a stale cache file after a refresh changed the document
            var key = $"{path}|{entry.ContentKey ?? ""}";

            var buffer = ArrayPool<byte>.Shared.Rent(want);
            try
            {
                var read = await spoolCache.ReadAsync(key, size, () => tree.OpenRead(path), offset, buffer.AsMemory(0, want), cancellationToken).ConfigureAwait(false);

                context.Response.ContentLength = read;
                if (read > 0)
                {
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        //The server sends share-relative paths it has already cleaned ('/'-separated, no leading
        //slash, no "." or ".."); anything else is refused rather than guessed at
        static bool TryGetPath(HttpContext context, out string path)
        {
            path = context.Request.Query["path"].ToString();

            if (path.Length == 0)
            {
                return true;
            }

            return !path.Contains('\\') && !path.Split('/').Any(segment => segment is "" or "." or "..");
        }

        static FileInfo ToFileInfo(VfsEntry entry)
        {
            //the WebDAV layer sends these values unconverted, as if UTC; do the same so both transports
            //show the same timestamps
            var modified = new DateTimeOffset(DateTime.SpecifyKind(entry.LastWriteTimeUtc, DateTimeKind.Utc));

            //ReadOnly here would put the read-only attribute on every file (and onto every copy made of one)
            return new FileInfo(entry.Name, entry.IsDirectory ? 0 : (long)entry.Size, modified, entry.IsDirectory, ReadOnly: false);
        }

        static async Task JsonAsync<T>(HttpContext context, T value, CancellationToken cancellationToken)
        {
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        static async Task ErrorAsync(HttpContext context, int status, string message, CancellationToken cancellationToken)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";

            var bytes = JsonSerializer.SerializeToUtf8Bytes(new ErrorBody(message), JsonOptions);
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        sealed record Capabilities(string Name, bool ReadOnly, bool CaseInsensitive);
        sealed record FileInfo(string Name, long Size, DateTimeOffset ModTime, bool IsDir, bool ReadOnly);
        sealed record DirEntries(List<FileInfo> Entries);
        sealed record ErrorBody(string Error);

        //Served while the session is still fetching the documents
        sealed class EmptySource : IVfsSource
        {
            public static readonly EmptySource Instance = new();

            static readonly VfsEntry Root = new() { Name = "", IsDirectory = true, LastWriteTimeUtc = DateTime.UtcNow };

            public VfsEntry? Stat(string path) => path.Length == 0 ? Root : null;
            public IReadOnlyList<VfsEntry>? List(string path) => path.Length == 0 ? [] : null;
            public Stream OpenRead(string path) => throw new FileNotFoundException(path);
        }

        //Kestrel's own warnings and errors go to the log file
        sealed class LogFileProvider : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new LogFileLogger(categoryName);
            public void Dispose() { }

            sealed class LogFileLogger(string categoryName) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    if (!IsEnabled(logLevel)) return;

                    var message = formatter(state, exception);
                    libCommon.Log.WriteLine($"VFS endpoint ({categoryName}): {message}{(exception != null ? $" {exception.Message}" : "")}");
                }
            }
        }

        sealed class SilentLifetime : IHostLifetime
        {
            public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
