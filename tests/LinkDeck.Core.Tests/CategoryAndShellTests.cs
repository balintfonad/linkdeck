using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Import;
using LinkDeck.Core.Models;
using LinkDeck.Core.Popup;

namespace LinkDeck.Core.Tests;

public class ItemCategoryTests
{
    [Theory]
    [InlineData(@"C:\Apps\code.exe", ItemCategory.App)]
    [InlineData(@"C:\Start\Word.lnk", ItemCategory.App)]
    [InlineData(@"C:\tools\build.cmd", ItemCategory.App)]
    [InlineData("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ItemCategory.App)]
    [InlineData(@"%USERPROFILE%\Projects", ItemCategory.FileOrFolder)]
    [InlineData(@"C:\Docs\report.pdf", ItemCategory.FileOrFolder)]
    public void Path_items_are_apps_or_files(string path, ItemCategory expected) =>
        Assert.Equal(expected, ItemCategories.Of(new PathItem { Id = "1", Name = "X", Path = path }));

    [Fact]
    public void Urls_are_web_and_rdp_is_remote_desktop()
    {
        Assert.Equal(ItemCategory.Web, ItemCategories.Of(new UrlItem { Id = "1", Name = "X", Url = "https://x.example" }));
        Assert.Equal(ItemCategory.RemoteDesktop, ItemCategories.Of(new RdpItem { Id = "1", Name = "X", Host = "h" }));
    }
}

public class GestureTabTests
{
    [Fact]
    public void Tab_while_holding_is_swallowed_so_alt_tab_does_not_open()
    {
        var d = new GestureDetector(Hotkey.Default);
        d.KeyDown(GestureKey.LeftAlt);
        d.KeyDown(GestureKey.LeftCtrl);

        Assert.Equal(new GestureResult(true, GestureSignal.Tab), d.KeyDown(GestureKey.Tab));
        Assert.True(d.KeyUp(GestureKey.Tab).Suppress);
        d.KeyDown(GestureKey.LeftShift);
        Assert.Equal(new GestureResult(true, GestureSignal.TabBack), d.KeyDown(GestureKey.Tab));
        Assert.True(d.IsActive);
    }

    [Fact]
    public void Tab_without_the_gesture_passes_through()
    {
        var d = new GestureDetector(Hotkey.Default);
        d.KeyDown(GestureKey.LeftAlt);

        Assert.Equal(GestureResult.None, d.KeyDown(GestureKey.Tab)); // plain Alt+Tab untouched
    }
}

public class DropShellItemTests
{
    [Fact]
    public void Start_menu_app_becomes_a_shell_target_path_item()
    {
        var data = new DropData { ShellItems = [new ShellItemInfo("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator")] };

        var draft = Assert.Single(new DropParser(null).Parse(data).Drafts);

        Assert.Equal((ItemType.Path, "Calculator", "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), (draft.Type, draft.Name, draft.Target));
        Assert.Null(draft.ToItem().Error);
    }

    [Fact]
    public void Shell_item_with_a_missing_file_is_ignored()
    {
        var data = new DropData { ShellItems = [new ShellItemInfo(@"C:\definitely\missing.exe", "Missing")] };

        var result = new DropParser(null).Parse(data);

        Assert.True(result.IsEmpty);
        Assert.Equal(["Missing"], result.Ignored);
    }
}
