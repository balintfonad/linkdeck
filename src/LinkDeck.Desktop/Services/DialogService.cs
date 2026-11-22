using LinkDeck.Desktop.Views;

namespace LinkDeck.Desktop.Services;

/// <summary>Small modal questions. Behind an interface so the popup logic can be tested without windows.</summary>
public interface IDialogService
{
    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initial = "");

    /// <summary>Asks for confirmation. Returns true when confirmed.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Shows a colour wheel. Returns null when cancelled; a choice with a null hex means "no colour".</summary>
    Task<ColorChoice?> PickColorAsync(string message, string? current);
}

public sealed class DialogService : IDialogService
{
    private readonly Action<Avalonia.Controls.Window>? _bringToFront;

    /// <param name="bringToFront">Forces the dialog into the foreground (LinkDeck usually runs in the background).</param>
    public DialogService(Action<Avalonia.Controls.Window>? bringToFront = null) => _bringToFront = bringToFront;

    public Task<string?> PromptAsync(string title, string label, string initial = "") =>
        PromptWindow.ShowAsync(title, label, initial, input: true, confirmText: "OK", _bringToFront);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText) =>
        await PromptWindow.ShowAsync(title, message, "", input: false, confirmText, _bringToFront) is not null;

    public Task<ColorChoice?> PickColorAsync(string message, string? current) =>
        ColorDialog.ShowAsync(message, current, _bringToFront);
}
