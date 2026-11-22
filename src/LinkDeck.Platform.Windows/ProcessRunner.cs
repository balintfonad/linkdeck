using System.Diagnostics;
using LinkDeck.Core.Abstractions;

namespace LinkDeck.Platform.Windows;

public sealed class ProcessRunner : IProcessRunner
{
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo)?.Dispose();
}
