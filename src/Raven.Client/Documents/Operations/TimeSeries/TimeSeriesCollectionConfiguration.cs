﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Raven.Client.Documents.Operations.TimeSeries
{
    public class TimeSeriesCollectionConfiguration
    {
        public SortedSet<TimeSeriesDownSamplePolicy> DownSamplePolicies = new SortedSet<TimeSeriesDownSamplePolicy>(TimeSeriesDownSamplePolicyComparer.Instance);

        public TimeSpan? DeleteAfter;

        public bool Disabled;
    }

    public class TimeSeriesDownSamplePolicy
    {
        /// <summary>
        /// Name of the time series policy, defined by the convention "Per{Amplitude}{DownSampleFrequency} (e.g. Per1Hour)"
        /// </summary>
        public string Name { get; } // defined by convention

        /// <summary>
        /// How far from UTC.Now this policy should be applied
        /// </summary>
        public TimeSpan TimeFromNow { get; }

        /// <summary>
        /// Define the down sample frequency of this policy
        /// </summary>
        public DownSampleFrequency DownSampleFrequency { get; }

        /// <summary>
        /// Define the magnitude of the down-sample frequency
        /// e.g. If the frequency is per minute, so setting amplitude to 5 will we be resulted to per 5 minutes policy  
        /// </summary>
        public int Amplitude { get; }

        /// <summary>
        /// Disable this policy
        /// </summary>
        public bool Disabled;


        public TimeSeriesDownSamplePolicy(TimeSpan timeFromNow, DownSampleFrequency downSampleFrequency, int amplitude = 1)
        {
            TimeFromNow = timeFromNow;
            DownSampleFrequency = downSampleFrequency;
            Amplitude = amplitude;

            var plural = amplitude == 1 ? "" : "s";
            Name = $"Per{amplitude}{downSampleFrequency}{plural}";
        }

        internal TimeSpan FrequencyToTimeSpan()
        {
            switch (DownSampleFrequency)
            {
                case DownSampleFrequency.Millisecond:
                    return new TimeSpan(0, 0, 0, 0, Amplitude);
                case DownSampleFrequency.Second:
                    return new TimeSpan(0, 0, 0, Amplitude);
                case DownSampleFrequency.Minute:
                    return new TimeSpan(0, 0, Amplitude,0);
                case DownSampleFrequency.Hour:
                    return new TimeSpan(Amplitude, 0, 0);
                case DownSampleFrequency.Day:
                    return new TimeSpan(Amplitude, 0, 0,0);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected bool Equals(TimeSeriesDownSamplePolicy other)
        {
            return Name == other.Name &&
                   TimeFromNow == other.TimeFromNow;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((TimeSeriesDownSamplePolicy)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Name.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)TimeFromNow.Ticks;
                return hashCode;
            }
        }
    }

    internal class TimeSeriesDownSamplePolicyComparer : IComparer<TimeSeriesDownSamplePolicy>
    {
        public static TimeSeriesDownSamplePolicyComparer Instance = new TimeSeriesDownSamplePolicyComparer();

        public int Compare(TimeSeriesDownSamplePolicy x, TimeSeriesDownSamplePolicy y)
        {
            if (x == null && y == null)
                return 0;
            if (x == null)
                return 1;
            if (y == null)
                return -1;

            var diff = x.FrequencyToTimeSpan().Ticks - y.FrequencyToTimeSpan().Ticks;
            
            if (diff > 0)
                return 1;
            if (diff < 0)
                return -1;
            return 0;
        }
    }

    public enum DownSampleFrequency
    {
        Millisecond,
        Second,
        Minute,
        Hour,
        Day
    }
}
