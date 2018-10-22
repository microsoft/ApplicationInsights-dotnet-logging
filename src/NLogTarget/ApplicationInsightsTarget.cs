// -----------------------------------------------------------------------
// <copyright file="ApplicationInsightsTarget.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. 
// All rights reserved.  2013
// </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.NLogTarget
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.ApplicationInsights.Implementation;

    using NLog;
    using NLog.Common;
    using NLog.Config;
    using NLog.Targets;

    /// <summary>
    /// NLog Target that routes all logging output to the Application Insights logging framework.
    /// The messages will be uploaded to the Application Insights cloud service.
    /// </summary>
    [Target("ApplicationInsightsTarget")]
    public sealed class ApplicationInsightsTarget : TargetWithLayout
    {
        private TelemetryClient telemetryClient;
        private DateTime lastLogEventTime;
        private NLog.Layouts.Layout instrumentationKeyLayout = string.Empty;

        /// <summary>
        /// Initializers a new instance of ApplicationInsightsTarget type.
        /// </summary>
        public ApplicationInsightsTarget()
        {
            this.Layout = @"${message}";
        }

        /// <summary>
        /// Gets or sets the Application Insights instrumentationKey for your application. 
        /// </summary>
        public string InstrumentationKey
        {
            get => (this.instrumentationKeyLayout as NLog.Layouts.SimpleLayout)?.Text ?? null;
            set => this.instrumentationKeyLayout = value ?? string.Empty;
        }

        /// <summary>
        /// Gets the array of custom attributes to be passed into the logevent context
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "contextproperty")]
        public IList<TargetPropertyWithContext> ContextProperties { get; } = new List<TargetPropertyWithContext>();

        /// <summary>
        /// Gets the logging controller we will be using.
        /// </summary>
        internal TelemetryClient TelemetryClient
        {
            get { return this.telemetryClient; }
        }

        internal void BuildPropertyBag(LogEventInfo logEvent, ITelemetry telemetryItem)
        {
            telemetryItem.Timestamp = logEvent.TimeStamp;
            telemetryItem.Sequence = logEvent.SequenceID.ToString(CultureInfo.InvariantCulture);

            var telemetryWithProperties = telemetryItem as ISupportProperties;
            IDictionary<string, string> propertyBag = telemetryWithProperties.Properties;
            IDictionary<string, string> contextGlobalPropertyBag = telemetryItem.Context.GlobalProperties;

            // Populate LogEvent Properties
            PopulateLogEventProperties(propertyBag, logEvent);

            // Populate Global Properties
            foreach (var contextProperty in this.ContextProperties)
            {
                PopulateGlobalPropertyBag(contextGlobalPropertyBag, contextProperty, logEvent);
            }

            // Populate Individual Properties from LogEvent
            if (logEvent.HasProperties && logEvent.Properties?.Count > 0)
            {
                foreach (var keyValuePair in logEvent.Properties)
                {
                    PopulatePropertyBag(propertyBag, keyValuePair);
                }
            }
        }

        /// <summary>
        /// Initializes the Target and perform instrumentationKey validation.
        /// </summary>
        /// <exception cref="NLogConfigurationException">Will throw when <see cref="InstrumentationKey"/> is not set.</exception>
        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            this.telemetryClient = new TelemetryClient();

            string instrumentationKey = this.instrumentationKeyLayout.Render(LogEventInfo.CreateNullEvent());
            if (!string.IsNullOrWhiteSpace(instrumentationKey))
            {
                this.telemetryClient.Context.InstrumentationKey = instrumentationKey;
            }

            this.telemetryClient.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("nlog:");
        }

        /// <summary>
        /// Send the log message to Application Insights.
        /// </summary>
        /// <exception cref="ArgumentNullException">If <paramref name="logEvent"/> is null.</exception>
        protected override void Write(LogEventInfo logEvent)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            this.lastLogEventTime = DateTime.UtcNow;

            if (logEvent.Exception != null)
            {
                this.SendException(logEvent);
            }
            else
            {
                this.SendTrace(logEvent);
            }
        }

        /// <summary>
        /// Flush any pending log messages
        /// </summary>
        /// <param name="asyncContinuation">The asynchronous continuation</param>
        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            try
            {
                this.TelemetryClient.Flush();
                if (DateTime.UtcNow.AddSeconds(-30) > this.lastLogEventTime)
                {
                    // Nothing has been written, so nothing to wait for
                    asyncContinuation(null);
                }
                else
                {
                    // Documentation says it is important to wait after flush, else nothing will happen
                    // https://docs.microsoft.com/azure/application-insights/app-insights-api-custom-events-metrics#flushing-data
                    System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(500)).ContinueWith((task) => asyncContinuation(null));
                }
            }
            catch (Exception ex)
            {
                asyncContinuation(ex);
            }
        }

        private static void PopulateLogEventProperties(IDictionary<string, string> propertyBag, LogEventInfo logEvent)
        {
            if (!string.IsNullOrEmpty(logEvent.LoggerName))
            {
                propertyBag.Add("LoggerName", logEvent.LoggerName);
            }

            if (logEvent.UserStackFrame != null)
            {
                propertyBag.Add("UserStackFrame", logEvent.UserStackFrame.ToString());
                propertyBag.Add("UserStackFrameNumber", logEvent.UserStackFrameNumber.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void PopulatePropertyBag(IDictionary<string, string> propertyBag, KeyValuePair<object, object> keyValuePair)
        {
            string key = keyValuePair.Key.ToString();
            object valueObj = keyValuePair.Value;

            if (valueObj == null)
            {
                return;
            }

            string value = Convert.ToString(valueObj, CultureInfo.InvariantCulture);
            if (propertyBag.ContainsKey(key))
            {
                if (string.Equals(value, propertyBag[key], StringComparison.Ordinal))
                {
                    return;
                }

                key += "_1";
            }

            propertyBag.Add(key, value);
        }

        private static void PopulateGlobalPropertyBag(IDictionary<string, string> globalPropertyBag, TargetPropertyWithContext contextProperty, LogEventInfo logEvent)
        {
            string key = contextProperty.Name;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            string value = contextProperty.Layout?.Render(logEvent);
            if (value == null)
            {
                return;
            }

            globalPropertyBag.Add(key, value);
        }

        private static SeverityLevel? GetSeverityLevel(LogLevel logEventLevel)
        {
            if (logEventLevel == null)
            {
                return null;
            }

            if (logEventLevel.Ordinal == LogLevel.Trace.Ordinal ||
                logEventLevel.Ordinal == LogLevel.Debug.Ordinal)
            {
                return SeverityLevel.Verbose;
            }

            if (logEventLevel.Ordinal == LogLevel.Info.Ordinal)
            {
                return SeverityLevel.Information;
            }

            if (logEventLevel.Ordinal == LogLevel.Warn.Ordinal)
            {
                return SeverityLevel.Warning;
            }

            if (logEventLevel.Ordinal == LogLevel.Error.Ordinal)
            {
                return SeverityLevel.Error;
            }

            if (logEventLevel.Ordinal == LogLevel.Fatal.Ordinal)
            {
                return SeverityLevel.Critical;
            }

            // The only possible value left if OFF but we should never get here in this case
            return null;
        }

        private void SendException(LogEventInfo logEvent)
        {
            var exceptionTelemetry = new ExceptionTelemetry(logEvent.Exception)
            {
                SeverityLevel = GetSeverityLevel(logEvent.Level)
            };

            string logMessage = this.Layout.Render(logEvent);
            exceptionTelemetry.Properties.Add("Message", logMessage);

            this.BuildPropertyBag(logEvent, exceptionTelemetry);
            this.telemetryClient.Track(exceptionTelemetry);
        }

        private void SendTrace(LogEventInfo logEvent)
        {
            string logMessage = this.Layout.Render(logEvent);

            var trace = new TraceTelemetry(logMessage)
            {
                SeverityLevel = GetSeverityLevel(logEvent.Level)
            };

            this.BuildPropertyBag(logEvent, trace);
            this.telemetryClient.Track(trace);
        }
    }
}