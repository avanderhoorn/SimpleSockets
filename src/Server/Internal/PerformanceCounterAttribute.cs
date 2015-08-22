using System;

namespace Server
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class PerformanceCounterAttribute : Attribute
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public PerformanceCounterType CounterType { get; set; }
    }

    public enum PerformanceCounterType
    {
        NumberOfItems32,
        NumberOfItems64,
        RateOfCountsPerSecond32,
        RateOfCountsPerSecond64
    }
}