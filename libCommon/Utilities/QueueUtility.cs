using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon.Utilities
{
    public static class QueueUtility
    {
        public static void Recurse2<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> childSelector, int maxThreads, CancellationToken ct)
        {
            var collection = new BlockingCollection<T>();
            foreach (var item in source)
            {
                collection.Add(item, ct);
            }

            var marshall = new ManualResetEvent(true);
            var activeThreadsNumber = 0;
            var stop = false;

            var tasks = new List<Thread>();
            for (int i = 0; i < maxThreads; i++)
            {
                var newThread = new Thread(() =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        marshall.WaitOne();
                        if (stop) break;
                        Interlocked.Increment(ref activeThreadsNumber);
                        while (collection.TryTake(out T? item, 100))
                        {
                            var subItems = childSelector(item);
                            foreach (var subItem in subItems)
                            {
                                collection.Add(subItem);
                            }
                        }
                        Interlocked.Decrement(ref activeThreadsNumber);
                    }
                })
                {
                    IsBackground = true
                };
                newThread.Start();

                tasks.Add(newThread);
            }

            //once in a while, take a survey to determine if we've finished
            var check = new Thread(new ThreadStart(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    if (collection.Count == 0)
                    {
                        //hold all the workers up to do a survey
                        marshall.Reset();
                        Thread.Sleep(1000);
                        //Debug.WriteLine($"activeThreadsNumber: {activeThreadsNumber}, collection.Count(): {collection.Count}");
                        //final survey
                        if (activeThreadsNumber == 0 && collection.Count == 0)
                        {
                            //finished
                            stop = true;
                            marshall.Set();
                            break;
                        }
                        marshall.Set();
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                }
            }))
            {
                IsBackground = true
            };
            check.Start();

            tasks.ToList().ForEach(t => t.Join());
        }
    }
}
