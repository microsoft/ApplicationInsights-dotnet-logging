namespace Microsoft.ApplicationInsights.ILogger
{
    using System;

    /// <summary>
    /// Class to provide ApplicationInsights logger events
    /// </summary>
    internal class ApplicationInsightsLoggerEvents
    {
        /// <summary>
        /// Event that is fired when new ApplicationInsights logger is added.
        /// </summary>
        public event Action LoggerAddedEventHandler;

        /// <summary>
        /// Invokes LoggerAdded event.
        /// </summary>
        public void OnLoggerAdded()
        {
            this.LoggerAddedEventHandler?.Invoke();
        }
    }
}