using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Server
{
    public interface IPerformanceCounter
    {
        string CounterName { get; }

        long Decrement();

        long Increment();

        long IncrementBy(long value);

        long RawValue { get; set; }

        void Close();

        void RemoveInstance();
    }
}
