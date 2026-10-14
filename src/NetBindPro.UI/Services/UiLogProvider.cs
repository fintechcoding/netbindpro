// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using Microsoft.Extensions.Logging;
using NetBindPro.UI.ViewModels;

namespace NetBindPro.UI.Services;

/// <summary>
/// Forwards log events to the in-app Log page (<see cref="LogViewModel"/>). Without this,
/// the Log page is permanently empty because the only configured provider is Debug output.
/// </summary>
public sealed class UiLogProvider(LogViewModel vm) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new UiLogger(vm, categoryName);
    public void Dispose() { }

    private sealed class UiLogger(LogViewModel vm, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (exception != null) message += " — " + exception.Message;
            // Trim the category down to the class name for readability.
            var shortCat = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
            vm.Append(logLevel, $"[{shortCat}] {message}");
        }
    }
}
