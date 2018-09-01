namespace Microsoft.ApplicationInsights.ILogger
{
    using System;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// <see cref="ILoggerProvider"/> implementation that creates returns instances of <see cref="ApplicationInsightsLogger"/>
    /// </summary>
    [ProviderAlias("ApplicationInsights")]
    public sealed class ApplicationInsightsLoggerProvider : ILoggerProvider
    {
        private readonly TelemetryConfiguration telemetryConfiguration;
        private readonly Func<string, LogLevel, bool> filter;
        private readonly ApplicationInsightsLoggerOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInsightsLoggerProvider"/> class.
        /// </summary>
        public ApplicationInsightsLoggerProvider(TelemetryConfiguration telemetryConfiguration, Func<string, LogLevel, bool> filter, IOptions<ApplicationInsightsLoggerOptions> options)
        {
            this.telemetryConfiguration = telemetryConfiguration;
            this.filter = filter;
            this.options = options.Value;
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new ApplicationInsightsLogger(categoryName, this.telemetryConfiguration, this.filter, this.options);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
        }
    }
}