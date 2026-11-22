using Avalonia;
using Avalonia.Headless;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Config;
using LinkDeck.Core.Logging;
using LinkDeck.Core.Models;
using LinkDeck.Core.Tests;
using LinkDeck.Desktop.Services;

[assembly: AvaloniaTestApplication(typeof(LinkDeck.Desktop.Tests.TestAppBuilder))]

namespace LinkDeck.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>The real App resources and styles, without starting services.</summary>
public sealed class TestApp : App
{
    public override void OnFrameworkInitializationCompleted()
    {
    }
}

public sealed class FakeLauncher : ILauncher
{
    public List<Item> Launched { get; } = [];

    public LaunchResult Next { get; set; } = LaunchResult.Ok;

    public LaunchResult Launch(Item item)
    {
        Launched.Add(item);
        return Next;
    }

    public void CleanupTemporaryFiles()
    {
    }
}

public sealed class FakeNotifier : INotifier
{
    public List<(NoticeLevel Level, string Message)> Shown { get; } = [];

    public void Show(NoticeLevel level, string message) => Shown.Add((level, message));
}

/// <summary>A DeckService over real files in a temporary directory.</summary>
public sealed class DeckFixture : IDisposable
{
    public DeckFixture(AppConfig? config = null)
    {
        Store = new ConfigStore(Dir.File("config.json"));
        Usage = new UsageStore(Dir.File("state.json"), Time);
        Deck = new DeckService(Store, Usage, Launcher, Notifier, NullLog.Instance, a => a(), Time);
        Deck.Load();
        if (config is not null)
        {
            Deck.TrySave(config);
        }
    }

    public TempDir Dir { get; } = new();

    public FakeTime Time { get; } = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

    public ConfigStore Store { get; }

    public UsageStore Usage { get; }

    public FakeLauncher Launcher { get; } = new();

    public FakeNotifier Notifier { get; } = new();

    public DeckService Deck { get; }

    public static AppConfig Sample(SortMode sort = SortMode.Frecency) => new()
    {
        Settings = new AppSettings { SortMode = sort },
        Groups =
        [
            new Group
            {
                Id = "work",
                Name = "Work",
                Color = "#3B82F6",
                Items =
                [
                    new UrlItem { Id = "jira", Name = "Jira", Url = "https://jira.example.com" },
                    new UrlItem { Id = "wiki", Name = "Wiki", Url = "https://wiki.example.com" },
                    new RdpItem { Id = "prod", Name = "Prod server", Host = "10.0.0.15" },
                ],
            },
            new Group
            {
                Id = "home",
                Name = "Home",
                Items =
                [
                    new PathItem { Id = "docs", Name = "Documents", Path = "C:\\Docs" },
                ],
            },
        ],
    };

    public void Dispose()
    {
        Store.Dispose();
        Dir.Dispose();
    }
}
