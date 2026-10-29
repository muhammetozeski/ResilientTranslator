using Microsoft.AspNetCore.Components.Web;
using ResilientTranslator.MainProject.Infrastructure;

namespace ResilientTranslator.MainProject.UI.Layout.Architecture;

/// <summary>Contains renderer/event failures while keeping editor state in the existing workspace for explicit recovery.</summary>
public sealed class RecoverableErrorBoundary : ErrorBoundary
{
    const bool IsLogEnabled = true;
    public bool IsFaulted => CurrentException is not null;

    /// <summary>Records the original UI failure without starting an automatic render retry loop.</summary>
    /// <param name="exception">The render or event failure captured by Blazor.</param>
    /// <returns>A completed diagnostic task; recovery requires a new user command.</returns>
    protected override Task OnErrorAsync(Exception exception)
    {
        ExceptionBoundary.LogSafely($"Blazor view failed; error={exception}", IsLogEnabled);
        return Task.CompletedTask;
    }
}
