using System.Collections.Concurrent;
using System.Diagnostics;

namespace RecompOne.Runtime.Host;

public static class GpuJobs
{
    private sealed class Job
    {
        public Action? Work;
        public readonly ManualResetEventSlim Done = new(false);
        public Exception? Error;
    }
    
    private static readonly ConcurrentQueue<Job> _queue = new();
    private static readonly ConcurrentBag<Job> _spare = [];
    
    private static int _owner = -1;
    private static long _lastWaitBits;
    private static long _maxWaitBits;

    public static double LastWaitMs => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _lastWaitBits));
    public static double MaxWaitMs => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _maxWaitBits));
    
    public static bool Claimed => _owner >= 0;
    
    public static bool IsOwner => _owner == Environment.CurrentManagedThreadId;
    
    public static void Claim()
    {
        _owner = Environment.CurrentManagedThreadId;
    }
    
    public static void Run(Action work)
    {
        if (!Claimed || IsOwner)
        {
            work();
            return;
        }
        
        if (!_spare.TryTake(out var job)) job = new Job();
        
        job.Work = work;
        job.Error = null;
        job.Done.Reset();
        
        _queue.Enqueue(job);
        var waitStart = Stopwatch.GetTimestamp();
        job.Done.Wait();
        var waitMs = Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds;
        var waitBits = BitConverter.DoubleToInt64Bits(waitMs);
        Interlocked.Exchange(ref _lastWaitBits, waitBits);
        var maxBits = Interlocked.Read(ref _maxWaitBits);
        while (waitBits > maxBits)
        {
            var observed = Interlocked.CompareExchange(ref _maxWaitBits, waitBits, maxBits);
            if (observed == maxBits) break;
            maxBits = observed;
        }
        
        var error = job.Error;
        job.Work = null;
        _spare.Add(job);
        
        if (error != null) throw new InvalidOperationException("gpu job faild", error);
    }
    
    public static void Drain()
    {
        while (_queue.TryDequeue(out var job))
        {
            try
            {
                job.Work?.Invoke();
            }
            catch (Exception e)
            {
                job.Error = e;
            }
            
            job.Done.Set();
        }
    }
}
