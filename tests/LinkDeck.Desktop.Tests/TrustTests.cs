using Avalonia.Headless.XUnit;
using LinkDeck.Core.Config;
using LinkDeck.Core.Import;
using LinkDeck.Core.Logging;
using LinkDeck.Core.Models;
using LinkDeck.Core.Tests;
using LinkDeck.Desktop.Services;
using LinkDeck.Desktop.ViewModels;
using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Tests;

/// <summary>A DeckService with a trust store, to test what happens when config.json is changed from outside.</summary>
public sealed class TrustFixture : IDisposable
{
    public TempDir Dir { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public FakeNotifier Notifier { get; } = new();

    public ConfigStore Store { get; private set; } = null!;

    public TrustStore Trust { get; private set; } = null!;

    public DeckService Deck { get; private set; } = null!;

    public string ConfigPath => Dir.File("config.json");

    public DeckService Start()
    {
        Store?.Dispose();
        Store = new ConfigStore(ConfigPath);
        Trust = new TrustStore(Dir.File("trust.dat"), new FakeProtector());
        Deck = new DeckService(Store, new UsageStore(Dir.File("state.json")), Launcher, Notifier, NullLog.Instance, a => a(), trust: Trust);
        Deck.Load();
        return Deck;
    }

    /// <summary>Another program rewrites config.json.</summary>
    public void EditFromOutside(Func<AppConfig, AppConfig> change)
    {
        File.WriteAllText(ConfigPath, ConfigStore.Serialize(change(Store.Current)));
        Store.ReloadIfChanged();
    }

    public static AppConfig WithOutlook() => new()
    {
        Groups =
        [
            new Group
            {
                Id = "apps",
                Name = "Apps",
                Items = [new PathItem { Id = "outlook", Name = "Outlook", Path = @"C:\Office\OUTLOOK.EXE" }],
            },
        ],
    };

    public static AppConfig ReplaceOutlook(AppConfig config, Func<PathItem, PathItem> change) =>
        config.WithGroups(config.Groups.Select(g => g with
        {
            Items = [.. g.Items.Select(i => i is PathItem { Id: "outlook" } p ? change(p) : i)],
        }));

    public void Dispose()
    {
        Store?.Dispose();
        Dir.Dispose();
    }
}

public class TrustTests : IDisposable
{
    private readonly TrustFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void First_run_trusts_the_default_configuration()
    {
        var deck = _f.Start();

        Assert.All(deck.Config.AllItems, i => Assert.False(deck.NeedsConfirmation(i)));
    }

    [Fact]
    public void Items_added_in_LinkDeck_are_trusted()
    {
        var deck = _f.Start();
        var item = new PathItem { Id = "n", Name = "Notepad", Path = @"C:\Windows\notepad.exe" };

        Assert.True(deck.AddItem(null, item));

        Assert.False(deck.NeedsConfirmation(item));
        Assert.True(deck.Launch(item).Success);
        Assert.Single(_f.Launcher.Launched);
    }

    [Fact]
    public void Item_retargeted_from_outside_needs_confirmation_and_is_announced()
    {
        var deck = _f.Start();
        deck.TrySave(TrustFixture.WithOutlook());

        _f.EditFromOutside(c => TrustFixture.ReplaceOutlook(c, p => p with { Path = @"C:\Users\me\AppData\Roaming\x\outlook.exe" }));

        var outlook = deck.Config.AllItems.Single(i => i.Id == "outlook");
        Assert.True(deck.NeedsConfirmation(outlook));
        Assert.Contains(_f.Notifier.Shown, n => n.Level == NoticeLevel.Warning && n.Message.Contains("Outlook", StringComparison.Ordinal));

        var blocked = deck.Launch(outlook);
        Assert.False(blocked.Success);
        Assert.Empty(_f.Launcher.Launched);

        Assert.True(deck.Launch(outlook, confirmed: true).Success);
        Assert.Single(_f.Launcher.Launched);
        Assert.False(deck.NeedsConfirmation(outlook));
    }

    [Fact]
    public void Hidden_arguments_added_from_outside_need_confirmation()
    {
        var deck = _f.Start();
        deck.TrySave(TrustFixture.WithOutlook());

        _f.EditFromOutside(c => TrustFixture.ReplaceOutlook(c, p => p with { Args = "/c start evil.exe" }));

        Assert.True(deck.NeedsConfirmation(deck.Config.AllItems.Single(i => i.Id == "outlook")));
    }

    [Fact]
    public void Saving_from_settings_does_not_trust_items_added_from_outside()
    {
        var deck = _f.Start();
        deck.TrySave(TrustFixture.WithOutlook());
        _f.EditFromOutside(c => TrustFixture.ReplaceOutlook(c, p => p with { Path = @"C:\evil.exe" }));

        // Any later save (e.g. Settings → change the theme) writes the whole config, including the untrusted item.
        Assert.True(deck.TrySave(deck.Config with { Settings = deck.Config.Settings with { Theme = ThemeMode.Dark } }));

        Assert.True(deck.NeedsConfirmation(deck.Config.AllItems.Single(i => i.Id == "outlook")));
    }

    [Fact]
    public void Editing_an_item_in_LinkDeck_trusts_the_new_target()
    {
        var deck = _f.Start();
        deck.TrySave(TrustFixture.WithOutlook());

        Assert.True(deck.TrySave(TrustFixture.ReplaceOutlook(deck.Config, p => p with { Path = @"C:\Office16\OUTLOOK.EXE" })));

        Assert.False(deck.NeedsConfirmation(deck.Config.AllItems.Single(i => i.Id == "outlook")));
    }

    [Fact]
    public void Existing_config_without_trust_data_asks_once_for_every_item()
    {
        File.WriteAllText(_f.ConfigPath, ConfigStore.Serialize(TrustFixture.WithOutlook()));

        var deck = _f.Start();

        var outlook = deck.Config.AllItems.Single();
        Assert.True(deck.NeedsConfirmation(outlook));
        Assert.Contains(_f.Notifier.Shown, n => n.Level == NoticeLevel.Warning);
    }

    [Fact]
    public void Tampered_trust_data_trusts_nothing()
    {
        var deck = _f.Start();
        deck.TrySave(TrustFixture.WithOutlook());
        File.WriteAllBytes(_f.Dir.File("trust.dat"), [1, 2, 3]);

        deck = _f.Start();

        Assert.True(deck.NeedsConfirmation(deck.Config.AllItems.Single()));
        Assert.Contains(_f.Notifier.Shown, n => n.Message.Contains("could not be verified", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Launcher_asks_before_opening_an_untrusted_item_and_shows_what_it_runs()
    {
        var deck = _f.Start();
        deck.TrySave(TrustFixture.WithOutlook());
        _f.EditFromOutside(c => TrustFixture.ReplaceOutlook(c, p => p with { Path = @"C:\evil.exe", Args = "--quiet", WorkingDir = @"C:\Temp" }));
        var outlook = deck.Config.AllItems.Single();

        var vm = new PopupViewModel(deck, null);
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var controller = new PopupController(new PopupWindow { DataContext = vm }, vm, deck, new FakeHotkey(), null, new DropParser(null), null, cyclingShowDelay: TimeSpan.Zero)
        {
            Dialogs = dialogs,
        };

        await controller.LaunchAsync(outlook);

        var message = Assert.Single(dialogs.Messages);
        Assert.Contains(@"C:\evil.exe", message, StringComparison.Ordinal);
        Assert.Contains("--quiet", message, StringComparison.Ordinal);
        Assert.Contains(@"C:\Temp", message, StringComparison.Ordinal);
        Assert.Empty(_f.Launcher.Launched);

        dialogs.ConfirmResult = true;
        await controller.LaunchAsync(outlook);

        Assert.Single(_f.Launcher.Launched);
        Assert.False(deck.NeedsConfirmation(outlook));
    }

    [Fact]
    public void Confirmation_text_describes_every_item_type()
    {
        Assert.Contains("https://bank.example", PopupController.DescribeForConfirmation(new UrlItem { Name = "Bank", Url = "https://bank.example" }), StringComparison.Ordinal);
        var rdp = PopupController.DescribeForConfirmation(new RdpItem { Name = "Prod", Host = "prod.corp", Port = 3390, Username = "admin" });
        Assert.Contains("prod.corp:3390", rdp, StringComparison.Ordinal);
        Assert.Contains("admin", rdp, StringComparison.Ordinal);
    }
}
