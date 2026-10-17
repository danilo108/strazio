using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Strazio.Application;
using Strazio.Cli;
using Strazio.Infrastructure.Excel;
using Strazio.Infrastructure.Http;
using Strazio.Infrastructure.Storage;
using Strazio.Infrastructure.Windows;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders(); // User-facing diagnostics are translated by the application.
builder.Services.AddSingleton<IItalianClock, ItalianClock>();
builder.Services.AddSingleton<ISettingsRepository, JsonSettingsStore>();
builder.Services.AddSingleton<IUserMessages, EmbeddedUserMessages>();
builder.Services.AddSingleton<IScheduledArchiveService, WindowsScheduleService>();
builder.Services.AddSingleton<IWindowsSession, WindowsSession>();
builder.Services.AddSingleton<IArchiveLock, ArchiveMutex>();
builder.Services.AddSingleton<IRequestPacer, RandomRequestPacer>();
builder.Services.AddSingleton<ISourceCooldown, FileSourceCooldown>();
builder.Services.AddSingleton<ClosedXmlArchive>();
builder.Services.AddSingleton<IArchiveWriter>(s => s.GetRequiredService<ClosedXmlArchive>());
builder.Services.AddSingleton<IArchiveInspector>(s => s.GetRequiredService<ClosedXmlArchive>());
builder.Services.AddHttpClient<IPageFetcher, HttpPageFetcher>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IRawExtractionParser, RawExtractionParser>();
builder.Services.AddSingleton<IDrawMapper, DrawMapper>();
builder.Services.AddSingleton<IExtractionSource, HtmlExtractionSource>();
builder.Services.AddSingleton<IExtractionSourceProvider, ExtractionSourceProvider>();
builder.Services.AddSingleton<ArchiveSyncService>();
builder.Services.AddSingleton<CliRunner>();
using var host = builder.Build();
return await host.Services.GetRequiredService<CliRunner>().RunAsync(args, cancellation.Token);
