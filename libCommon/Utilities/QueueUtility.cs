using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon.Utilities
{
    public static class QueueUtility
    {
        public static Task Process<T>(IProducerConsumerCollection<T> collection, Func<T, IEnumerable<T>> processItem, int maxDegreeOfParallelism, CancellationToken ct)
        {
            var tasks = new Task[maxDegreeOfParallelism];
            int activeThreadsNumber = 0;
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    while (true)
                    {
                        Interlocked.Increment(ref activeThreadsNumber);

                        while (collection.TryTake(out T item))
                        {
                            var nextItems = processItem(item);
                            foreach (var nextItem in nextItems)
                            {
                                collection.TryAdd(nextItem);
                            }
                        }

                        Interlocked.Decrement(ref activeThreadsNumber);
                        if (activeThreadsNumber == 0) //all tasks finished
                            return;
                    }
                }, ct);
            }

            return Task.WhenAll(tasks);
        }
    }
}
