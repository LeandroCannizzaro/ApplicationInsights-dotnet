﻿namespace Microsoft.ApplicationInsights.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights.Metrics.Extensibility;

    /// <summary>ToDo: Complete documentation before stable release.</summary>
    public sealed class MetricManager
    {
        private readonly MetricAggregationManager _aggregationManager;
        private readonly DefaultAggregationPeriodCycle _aggregationCycle;
        private readonly IMetricTelemetryPipeline _telemetryPipeline;
        private readonly MetricsCollection _metrics;

        /// <summary>ToDo: Complete documentation before stable release.</summary>
        /// <param name="telemetryPipeline">ToDo: Complete documentation before stable release.</param>
        public MetricManager(IMetricTelemetryPipeline telemetryPipeline)
        {
            Util.ValidateNotNull(telemetryPipeline, nameof(telemetryPipeline));

            this._telemetryPipeline = telemetryPipeline;
            this._aggregationManager = new MetricAggregationManager();
            this._aggregationCycle = new DefaultAggregationPeriodCycle(this._aggregationManager, this);

            this._metrics = new MetricsCollection(this);

            this._aggregationCycle.Start();
        }

        /// <summary>ToDo: Complete documentation before stable release.</summary>
        ~MetricManager()
        {
            DefaultAggregationPeriodCycle aggregationCycle = this._aggregationCycle;
            if (aggregationCycle != null)
            {
                Task fireAndForget = this._aggregationCycle.StopAsync();
            }
        }

        /// <summary>ToDo: Complete documentation before stable release.</summary>
        public MetricsCollection Metrics
        {
            get { return this._metrics; }
        }

        internal MetricAggregationManager AggregationManager
        {
            get { return this._aggregationManager; }
        }

        internal DefaultAggregationPeriodCycle AggregationCycle
        {
            get { return this._aggregationCycle; }
        }

        /// <summary>ToDo: Complete documentation before stable release.</summary>
        /// <param name="metricNamespace">ToDo: Complete documentation before stable release.</param>
        /// <param name="metricId">ToDo: Complete documentation before stable release.</param>
        /// <param name="config">ToDo: Complete documentation before stable release.</param>
        /// <returns>ToDo: Complete documentation before stable release.</returns>
        public MetricSeries CreateNewSeries(string metricNamespace, string metricId, IMetricSeriesConfiguration config)
        {
            return this.CreateNewSeries(
                            metricNamespace,
                            metricId,
                            dimensionNamesAndValues: null,
                            config: config);
        }

        /// <summary>ToDo: Complete documentation before stable release.</summary>
        /// <param name="metricNamespace">ToDo: Complete documentation before stable release.</param>
        /// <param name="metricId">ToDo: Complete documentation before stable release.</param>
        /// <param name="dimensionNamesAndValues">ToDo: Complete documentation before stable release.</param>
        /// <param name="config">ToDo: Complete documentation before stable release.</param>
        /// <returns>ToDo: Complete documentation before stable release.</returns>
        public MetricSeries CreateNewSeries(
                                    string metricNamespace, 
                                    string metricId, 
                                    IEnumerable<KeyValuePair<string, string>> dimensionNamesAndValues, 
                                    IMetricSeriesConfiguration config)
        {
            // Create MetricIdentifier (it will also validate metricNamespace and metricId):
            List<string> dimNames = null;
            if (dimensionNamesAndValues != null)
            {
                dimNames = new List<string>();
                foreach (KeyValuePair<string, string> dimNameVal in dimensionNamesAndValues)
                {
                    dimNames.Add(dimNameVal.Key);
                }
            }

            var metricIdentifier = new MetricIdentifier(metricNamespace, metricId, dimNames);

            // Create series:
            return this.CreateNewSeries(metricIdentifier, dimensionNamesAndValues, config);
        }

        /// <summary>ToDo: Complete documentation before stable release.</summary>
        /// <param name="metricIdentifier">ToDo: Complete documentation before stable release.</param>
        /// <param name="dimensionNamesAndValues">ToDo: Complete documentation before stable release.</param>
        /// <param name="config">ToDo: Complete documentation before stable release.</param>
        /// <returns>ToDo: Complete documentation before stable release.</returns>
        public MetricSeries CreateNewSeries(MetricIdentifier metricIdentifier, IEnumerable<KeyValuePair<string, string>> dimensionNamesAndValues, IMetricSeriesConfiguration config)
        {
            Util.ValidateNotNull(metricIdentifier, nameof(metricIdentifier));
            Util.ValidateNotNull(config, nameof(config));

            var dataSeries = new MetricSeries(this._aggregationManager, metricIdentifier, dimensionNamesAndValues, config);
            return dataSeries;
        }

        /// <summary>ToDo: Complete documentation before stable release.</summary>
        public void Flush()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            AggregationPeriodSummary aggregates = this._aggregationManager.StartOrCycleAggregators(MetricAggregationCycleKind.Default, futureFilter: null, tactTimestamp: now);
            this.TrackMetricAggregates(aggregates, flush: true);
        }

        internal void TrackMetricAggregates(AggregationPeriodSummary aggregates, bool flush)
        {
            int nonpersistentAggregatesCount = (aggregates?.NonpersistentAggregates == null)
                                                    ? 0
                                                    : aggregates.NonpersistentAggregates.Count;

            int persistentAggregatesCount = (aggregates?.PersistentAggregates == null)
                                                    ? 0
                                                    : aggregates.PersistentAggregates.Count;

            int totalAggregatesCount = nonpersistentAggregatesCount + persistentAggregatesCount;
            if (totalAggregatesCount == 0)
            {
                return;
            }

            Task[] trackTasks = new Task[totalAggregatesCount];
            int taskIndex = 0;

            if (nonpersistentAggregatesCount != 0)
            {
                foreach (MetricAggregate telemetryItem in aggregates.NonpersistentAggregates)
                {
                    if (telemetryItem != null)
                    {
                        Task trackTask = this._telemetryPipeline.TrackAsync(telemetryItem, CancellationToken.None);
                        trackTasks[taskIndex++] = trackTask;
                    }
                }
            }

            if (aggregates.PersistentAggregates != null && aggregates.PersistentAggregates.Count != 0)
            {
                foreach (MetricAggregate telemetryItem in aggregates.PersistentAggregates)
                {
                    if (telemetryItem != null)
                    {
                        Task trackTask = this._telemetryPipeline.TrackAsync(telemetryItem, CancellationToken.None);
                        trackTasks[taskIndex++] = trackTask;
                    }
                }
            }

            Task.WaitAll(trackTasks);

            if (flush)
            {
                Task flushTask = this._telemetryPipeline.FlushAsync(CancellationToken.None);
                flushTask.Wait();
            }
        }
    }        
}
