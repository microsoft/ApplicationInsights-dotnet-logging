namespace Microsoft.ApplicationInsights.ILogger
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.ApplicationInsights.Implementation;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// <see cref="ILogger"/> implementation that forwards log messages as Application Insight trace events.
    /// </summary>
    public class ApplicationInsightsLogger : ILogger
    {
        private readonly string categoryName;
        private readonly TelemetryClient telemetryClient;
        private readonly Func<string, LogLevel, bool> filter;
        private readonly ApplicationInsightsLoggerOptions options;

        /// <summary>
        /// Creates a new instance of <see cref="ApplicationInsightsLogger"/>
        /// </summary>
        public ApplicationInsightsLogger(string name, 
            TelemetryConfiguration telemetryConfiguration, 
            Func<string, LogLevel, bool> filter, 
            ApplicationInsightsLoggerOptions options)
        {
            this.categoryName = name;
            this.telemetryClient = new TelemetryClient(telemetryConfiguration);
            this.telemetryClient.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("il:");
            this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return this.filter != null 
                && this.filter(this.categoryName, logLevel) 
                && this.telemetryClient.IsEnabled();
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (this.IsEnabled(logLevel))
            {
                IReadOnlyList<KeyValuePair<string, object>> stateDictionary = state as IReadOnlyList<KeyValuePair<string, object>>;
                if (exception == null || this.options?.TrackExceptionsAsExceptionTelemetry == false)
                {
                    TraceTelemetry traceTelemetry = new TraceTelemetry(formatter(state, exception), this.GetSeverityLevel(logLevel));
                    this.PopulateTelemetry(traceTelemetry, stateDictionary, eventId);
                    this.telemetryClient.TrackTrace(traceTelemetry);
                }
                else
                {
                    ExceptionTelemetry exceptionTelemetry = new ExceptionTelemetry(exception)
                    {
                        Message = formatter(state, exception),
                        SeverityLevel = this.GetSeverityLevel(logLevel)
                    };
                    exceptionTelemetry.Context.Properties["Exception"] = exception.ToString();
                    this.PopulateTelemetry(exceptionTelemetry, stateDictionary, eventId);
                    this.telemetryClient.TrackException(exceptionTelemetry);
                }
            }
        }

        private void PopulateTelemetry(ITelemetry telemetry, IReadOnlyList<KeyValuePair<string, object>> stateDictionary, EventId eventId)
        {
            IDictionary<string, string> dict = telemetry.Context.Properties;
            dict["CategoryName"] = this.categoryName;

            if (this.options?.IncludeEventId ?? false)
            {
                if (eventId.Id != 0)
                {
                    dict["EventId"] = eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrEmpty(eventId.Name))
                {
                    dict["EventName"] = eventId.Name;
                }
            }

            if (stateDictionary != null)
            {
                foreach (KeyValuePair<string, object> item in stateDictionary)
                {
                    dict[item.Key] = Convert.ToString(item.Value, CultureInfo.InvariantCulture);
                }
            }
        }

        private SeverityLevel GetSeverityLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Critical:
                    return SeverityLevel.Critical;
                case LogLevel.Error:
                    return SeverityLevel.Error;
                case LogLevel.Warning:
                    return SeverityLevel.Warning;
                case LogLevel.Information:
                    return SeverityLevel.Information;
                case LogLevel.Debug:
                case LogLevel.Trace:
                default:
                    return SeverityLevel.Verbose;
            }
        }
    }
}
