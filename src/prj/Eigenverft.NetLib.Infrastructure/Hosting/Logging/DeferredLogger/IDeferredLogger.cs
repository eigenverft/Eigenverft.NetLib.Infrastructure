// File: IDeferredLogger.cs
using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Logging.DeferredLogger
{
    /// <summary>
    /// Provides lazy, level-aware logging on top of an underlying logger instance.
    /// </summary>
    /// <typeparam name="TCategoryName">The logging category type.</typeparam>
    public interface IDeferredLogger<TCategoryName>
    {
        /// <summary>
        /// Determines whether logging is enabled for the specified <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level to test.</param>
        /// <returns>
        /// <c>true</c> if the underlying logger is enabled for <paramref name="level"/>; otherwise <c>false</c>.
        /// </returns>
        bool IsEnabled(LogLevel level);

        // -------- GENERIC --------

        /// <summary>
        /// Logs a message using a deferred message factory at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void Log(LogLevel level, Func<string> messageFactory);

        /// <summary>
        /// Logs a message using a structured template and deferred arguments at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void Log(LogLevel level, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a message using a structured template and eagerly evaluated arguments at the given <paramref name="level"/>.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void Log(LogLevel level, string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a message with an exception using a deferred message factory at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void Log(LogLevel level, Exception exception, Func<string> messageFactory);

        /// <summary>
        /// Logs a message with an exception and deferred structured arguments at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void Log(LogLevel level, Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a message with an exception and eagerly evaluated arguments at the given <paramref name="level"/>.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void Log(LogLevel level, Exception exception, string messageTemplate, params object?[] arguments);

        // -------- TRACE --------

        /// <summary>
        /// Logs a trace message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogTrace(Func<string> messageFactory);

        /// <summary>
        /// Logs a trace message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogTrace(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a trace message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogTrace(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a trace message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogTrace(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a trace message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogTrace(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- DEBUG --------

        /// <summary>
        /// Logs a debug message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogDebug(Func<string> messageFactory);

        /// <summary>
        /// Logs a debug message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogDebug(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a debug message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogDebug(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a debug message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogDebug(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a debug message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogDebug(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- INFORMATION --------

        /// <summary>
        /// Logs an informational message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogInformation(Func<string> messageFactory);

        /// <summary>
        /// Logs an informational message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogInformation(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an informational message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogInformation(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs an informational message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogInformation(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an informational message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogInformation(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- WARNING --------

        /// <summary>
        /// Logs a warning message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogWarning(Func<string> messageFactory);

        /// <summary>
        /// Logs a warning message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogWarning(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a warning message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogWarning(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a warning message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogWarning(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a warning message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogWarning(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- ERROR --------

        /// <summary>
        /// Logs an error message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogError(Func<string> messageFactory);

        /// <summary>
        /// Logs an error message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogError(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an error message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogError(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs an error message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogError(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an error message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogError(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- CRITICAL --------

        /// <summary>
        /// Logs a critical message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogCritical(Func<string> messageFactory);

        /// <summary>
        /// Logs a critical message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogCritical(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a critical message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogCritical(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a critical message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogCritical(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a critical message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogCritical(Exception exception, string messageTemplate, params object?[] arguments);
    }

    /// <summary>
    /// Provides lazy, level-aware logging on top of an underlying logger instance,
    /// without requiring a category type parameter.
    /// </summary>
    /// <remarks>
    /// This is the non-generic companion to <see cref="IDeferredLogger{TCategoryName}"/>.
    /// Use this when you do not want to flow a category type through DI.
    /// </remarks>
    public interface IDeferredLogger
    {
        /// <summary>
        /// Determines whether logging is enabled for the specified <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level to test.</param>
        /// <returns>
        /// <c>true</c> if the underlying logger is enabled for <paramref name="level"/>; otherwise <c>false</c>.
        /// </returns>
        bool IsEnabled(LogLevel level);

        // -------- GENERIC --------

        /// <summary>
        /// Logs a message using a deferred message factory at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void Log(LogLevel level, Func<string> messageFactory);

        /// <summary>
        /// Logs a message using a structured template and deferred arguments at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void Log(LogLevel level, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a message using a structured template and eagerly evaluated arguments at the given <paramref name="level"/>.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void Log(LogLevel level, string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a message with an exception using a deferred message factory at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void Log(LogLevel level, Exception exception, Func<string> messageFactory);

        /// <summary>
        /// Logs a message with an exception and deferred structured arguments at the given <paramref name="level"/>.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void Log(LogLevel level, Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a message with an exception and eagerly evaluated arguments at the given <paramref name="level"/>.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void Log(LogLevel level, Exception exception, string messageTemplate, params object?[] arguments);

        // -------- TRACE --------

        /// <summary>
        /// Logs a trace message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogTrace(Func<string> messageFactory);

        /// <summary>
        /// Logs a trace message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogTrace(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a trace message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogTrace(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a trace message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogTrace(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a trace message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogTrace(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- DEBUG --------

        /// <summary>
        /// Logs a debug message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogDebug(Func<string> messageFactory);

        /// <summary>
        /// Logs a debug message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogDebug(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a debug message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogDebug(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a debug message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogDebug(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a debug message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogDebug(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- INFORMATION --------

        /// <summary>
        /// Logs an informational message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogInformation(Func<string> messageFactory);

        /// <summary>
        /// Logs an informational message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogInformation(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an informational message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogInformation(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs an informational message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogInformation(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an informational message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogInformation(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- WARNING --------

        /// <summary>
        /// Logs a warning message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogWarning(Func<string> messageFactory);

        /// <summary>
        /// Logs a warning message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogWarning(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a warning message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogWarning(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a warning message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogWarning(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a warning message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogWarning(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- ERROR --------

        /// <summary>
        /// Logs an error message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogError(Func<string> messageFactory);

        /// <summary>
        /// Logs an error message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogError(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an error message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogError(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs an error message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogError(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs an error message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogError(Exception exception, string messageTemplate, params object?[] arguments);

        // -------- CRITICAL --------

        /// <summary>
        /// Logs a critical message using a deferred message factory.
        /// </summary>
        /// <param name="messageFactory">Factory that builds the log message.</param>
        void LogCritical(Func<string> messageFactory);

        /// <summary>
        /// Logs a critical message using a structured template and deferred arguments.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogCritical(string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a critical message using a structured template and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogCritical(string messageTemplate, params object?[] arguments);

        /// <summary>
        /// Logs a critical message with an exception and deferred structured arguments.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="argumentFactories">Factories for the structured arguments.</param>
        void LogCritical(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories);

        /// <summary>
        /// Logs a critical message with an exception and eagerly evaluated arguments.
        /// Intended for inexpensive argument values.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="arguments">The arguments for the message template.</param>
        void LogCritical(Exception exception, string messageTemplate, params object?[] arguments);
    }
}
