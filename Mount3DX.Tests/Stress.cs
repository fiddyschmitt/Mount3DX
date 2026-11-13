using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mount3DX.Tests
{
    //Runs readers and enumerators at once, each on a thread of its own, all starting together. A
    //reader does its read a set number of times; an enumerator lists its folder until every reader
    //is done (pausing between listings, and up to a number, if asked). An operation that throws,
    //including one that finds the wrong bytes or names, counts as an error and the worker goes on.
    public static class Stress
    {
        public static StressResult Run(IReadOnlyList<Func<long>> readers, IReadOnlyList<Action> enumerators, int readsEach, TimeSpan listingPause, int listingsEachAtMost, TimeSpan timeout)
        {
            var reads = new StressTally();
            var listings = new StressTally();
            var listingsByEnumerator = new int[enumerators.Count];
            var stopwatch = new Stopwatch();
            TimeSpan readersDone = default;

            using var go = new ManualResetEventSlim();
            using var readersLeft = new CountdownEvent(readers.Count);
            var threads = new List<Thread>();

            foreach (var read in readers)
            {
                threads.Add(new Thread(() =>
                {
                    go.Wait();
                    try
                    {
                        for (var i = 0; i < readsEach; i++)
                        {
                            Time(read, reads);
                        }
                    }
                    finally
                    {
                        if (readersLeft.Signal())
                        {
                            readersDone = stopwatch.Elapsed;
                        }
                    }
                })
                { IsBackground = true, Name = "reader" });
            }

            for (var e = 0; e < enumerators.Count; e++)
            {
                var index = e;
                var list = enumerators[e];

                threads.Add(new Thread(() =>
                {
                    go.Wait();
                    do
                    {
                        Time(() => { list(); return 0; }, listings);
                        listingsByEnumerator[index]++;

                        if (listingPause > TimeSpan.Zero)
                        {
                            Thread.Sleep(listingPause);
                        }
                    }
                    while (!readersLeft.IsSet && listingsByEnumerator[index] < listingsEachAtMost);
                })
                { IsBackground = true, Name = "enumerator" });
            }

            threads.ForEach(thread => thread.Start());
            stopwatch.Start();
            go.Set();

            var hung = threads
                        .Where(thread => !thread.Join(Max(TimeSpan.Zero, timeout - stopwatch.Elapsed)))
                        .Select(thread => thread.Name!)
                        .ToList();

            return new StressResult(reads, listings, stopwatch.Elapsed, readersDone, listingsByEnumerator, hung);
        }

        static void Time(Func<long> operation, StressTally tally)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var bytes = operation();
                tally.Add(stopwatch.Elapsed.TotalMilliseconds, bytes);
            }
            catch (Exception ex)
            {
                tally.Errors.Enqueue($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    }

    public sealed class StressTally
    {
        readonly object gate = new();
        readonly List<double> milliseconds = [];
        long bytes;

        public ConcurrentQueue<string> Errors { get; } = new();

        public int Operations { get { lock (gate) { return milliseconds.Count; } } }

        public long Bytes { get { lock (gate) { return bytes; } } }

        public void Add(double operationMilliseconds, long operationBytes)
        {
            lock (gate)
            {
                milliseconds.Add(operationMilliseconds);
                bytes += operationBytes;
            }
        }

        //The time within which the given share of the operations finished, in milliseconds
        public double Percentile(double share)
        {
            lock (gate)
            {
                if (milliseconds.Count == 0) return 0;
                var sorted = milliseconds.Order().ToList();
                return sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Count) - 1, 0, sorted.Count - 1)];
            }
        }
    }

    public sealed record StressResult(StressTally Reads, StressTally Listings, TimeSpan Elapsed, TimeSpan ReadersDone, int[] ListingsByEnumerator, IReadOnlyList<string> Hung)
    {
        public string Summary(string what)
        {
            static string Ms(double ms) => ms >= 1000 ? $"{ms / 1000:N1} s" : $"{ms:N0} ms";

            var mib = Reads.Bytes / (1024.0 * 1024);
            return $"""
                {what}: readers and {ListingsByEnumerator.Length} enumerators at once, {Elapsed.TotalSeconds:N1} s in all
                  reads:    {Reads.Operations:N0} ({mib:N0} MiB) in {ReadersDone.TotalSeconds:N1} s, {mib / Math.Max(0.001, ReadersDone.TotalSeconds):N0} MiB/s; each took {Ms(Reads.Percentile(0.5))} (median), {Ms(Reads.Percentile(0.95))} (95%), {Ms(Reads.Percentile(1))} (slowest); {Reads.Errors.Count} errors
                  listings: {Listings.Operations:N0} ({ListingsByEnumerator.Min():N0} to {ListingsByEnumerator.Max():N0} per enumerator); each took {Ms(Listings.Percentile(0.5))} (median), {Ms(Listings.Percentile(0.95))} (95%), {Ms(Listings.Percentile(1))} (slowest); {Listings.Errors.Count} errors
                """;
        }

        public void AssertClean(int expectedReads)
        {
            Assert.IsEmpty(Hung, $"{Hung.Count} workers were still running at the timeout");
            Assert.IsEmpty(Reads.Errors, $"read errors, the first: {Reads.Errors.FirstOrDefault()}");
            Assert.IsEmpty(Listings.Errors, $"listing errors, the first: {Listings.Errors.FirstOrDefault()}");
            Assert.AreEqual(expectedReads, Reads.Operations, "reads done");
            Assert.IsTrue(ListingsByEnumerator.All(count => count >= 2), "an enumerator listed its folder fewer than twice while the readers read");
        }
    }
}
