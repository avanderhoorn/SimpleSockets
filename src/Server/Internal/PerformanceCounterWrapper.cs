﻿#if DNX451
using System;
using System.Diagnostics;

namespace Server
{
    public class PerformanceCounterWrapper : IPerformanceCounter
    {
        private readonly PerformanceCounter _counter;

        public PerformanceCounterWrapper(PerformanceCounter counter)
        {
            _counter = counter;
        }

        public string CounterName
        {
            get
            {
                return _counter.CounterName;
            }
        }

        public long RawValue
        {
            get { return _counter.RawValue; }
            set { _counter.RawValue = value; }
        }

        public long Decrement()
        {
            return _counter.Decrement();
        }

        public long Increment()
        {
            return _counter.Increment();
        }

        public long IncrementBy(long value)
        {
            return _counter.IncrementBy(value);
        }

        public void Close()
        {
            _counter.Close();
        }

        public void RemoveInstance()
        {
            try
            {
                _counter.RemoveInstance();
            }
            catch (NotImplementedException)
            {
                // This happens on mono
            }
        }
    }
}
#endif
