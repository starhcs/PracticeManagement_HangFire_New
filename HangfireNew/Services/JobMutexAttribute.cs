using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;

namespace HangfireNew.Services
{
    /// <summary>
    /// Shared locking logic used by <see cref="JobMutexFilter"/> and <see cref="JobMutexAttribute"/>.
    ///
    /// Every job that uses the same mutex name is serialised: while one of them is
    /// running, the others wait for it to finish - they do NOT run side by side and
    /// they are NOT thrown away.
    ///
    ///   1. The waiting run blocks on a SQL Server distributed lock (sp_getapplock on
    ///      the Hangfire storage) for up to WaitMinutes, and starts the moment the
    ///      running one ends.
    ///   2. If the running one is still going after that wait, the waiting run is put
    ///      back on the schedule RetryDelayMinutes later and tries again, so no run is lost.
    ///
    /// The lock is session scoped, so it works across multiple Hangfire servers /
    /// app instances and is released automatically if a process dies.
    /// </summary>
    internal static class JobMutex
    {
        public const string LockHandleKey = "JobMutex.LockHandle";

        public static void Acquire(PerformingContext context, string mutexName, int waitMinutes, int retryDelayMinutes)
        {
            string resource = $"job-mutex:{mutexName}";
            string jobId = context.BackgroundJob.Id;

            Console.WriteLine($"[JobMutex:{mutexName}] Job {jobId} ({Describe(context)}) is acquiring the mutex...");

            var startedWaitingAt = DateTime.UtcNow;

            try
            {
                context.Items[LockHandleKey] = context.Connection.AcquireDistributedLock(
                    resource,
                    TimeSpan.FromMinutes(waitMinutes));

                Console.WriteLine(
                    $"[JobMutex:{mutexName}] Job {jobId} got the mutex after " +
                    $"{(DateTime.UtcNow - startedWaitingAt).TotalSeconds:N0}s - running now.");
            }
            catch (DistributedLockTimeoutException)
            {
                // Another job holding the same mutex is still running. Do not fail this
                // run and do not run it in parallel - push it forward instead.
                try
                {
                    new BackgroundJobClient().Create(
                        context.BackgroundJob.Job,
                        new ScheduledState(TimeSpan.FromMinutes(retryDelayMinutes)));

                    Console.WriteLine(
                        $"[JobMutex:{mutexName}] Job {jobId} could not get the mutex within {waitMinutes} minute(s); " +
                        $"rescheduled to run in {retryDelayMinutes} minute(s).");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[JobMutex:{mutexName}] Could not reschedule job {jobId}: {ex.Message}");
                }

                // Skip this execution - the rescheduled copy will do the work.
                context.Canceled = true;
            }
        }

        public static void Release(PerformedContext context, string mutexName)
        {
            if (!context.Items.TryGetValue(LockHandleKey, out var handle))
            {
                return;
            }

            context.Items.Remove(LockHandleKey);
            (handle as IDisposable)?.Dispose();

            Console.WriteLine($"[JobMutex:{mutexName}] Job {context.BackgroundJob.Id} released the mutex.");
        }

        private static string Describe(PerformContext context)
        {
            Job job = context.BackgroundJob.Job;
            string args = string.Join(", ", job.Args ?? new List<object>());
            return $"{job.Type.Name}.{job.Method.Name}({args})";
        }
    }

    /// <summary>
    /// Global (non attribute) version of the mutex - register it once in Program.cs:
    ///
    ///     GlobalJobFilters.Filters.Add(new JobMutexFilter(
    ///         "submission-jobs",
    ///         nameof(ISubmissionService.SubmissionJob)) { WaitMinutes = 45, RetryDelayMinutes = 10 });
    ///
    /// Any job whose method name is in <paramref name="jobMethodNames"/> shares the mutex.
    ///
    /// Why a global filter instead of an attribute: Hangfire resolves filter attributes
    /// from the job type/method that is stored in the Hangfire tables. Because the
    /// recurring jobs are registered as "() => submissionService.SubmissionJob(...)",
    /// Hangfire stores the CONCRETE type it found at registration time and resolves the
    /// method on it, so an attribute placed on the interface is silently ignored.
    /// A global filter always runs, no matter how the job was registered.
    /// </summary>
    public sealed class JobMutexFilter : IServerFilter
    {
        private readonly string _mutexName;
        private readonly HashSet<string> _jobMethodNames;

        public JobMutexFilter(string mutexName, params string[] jobMethodNames)
        {
            if (string.IsNullOrWhiteSpace(mutexName))
            {
                throw new ArgumentException("Mutex name is required.", nameof(mutexName));
            }

            if (jobMethodNames == null || jobMethodNames.Length == 0)
            {
                throw new ArgumentException("At least one job method name is required.", nameof(jobMethodNames));
            }

            _mutexName = mutexName;
            _jobMethodNames = new HashSet<string>(jobMethodNames, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>How long a waiting run may block while the other run finishes.</summary>
        public int WaitMinutes { get; set; } = 45;

        /// <summary>If the wait above expires, the run is rescheduled this many minutes later.</summary>
        public int RetryDelayMinutes { get; set; } = 10;

        private bool Covers(PerformContext context) =>
            _jobMethodNames.Contains(context.BackgroundJob.Job.Method.Name);

        public void OnPerforming(PerformingContext context)
        {
            if (!Covers(context))
            {
                return;
            }

            JobMutex.Acquire(context, _mutexName, WaitMinutes, RetryDelayMinutes);
        }

        public void OnPerformed(PerformedContext context)
        {
            if (!Covers(context))
            {
                return;
            }

            JobMutex.Release(context, _mutexName);
        }
    }

    /// <summary>
    /// Attribute version of the same mutex. Only works when the job is registered
    /// against the type that carries the attribute (for example
    /// RecurringJob.AddOrUpdate&lt;SubmissionService&gt;(...)), so the submission jobs use
    /// <see cref="JobMutexFilter"/> in Program.cs instead. Kept here for future jobs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class JobMutexAttribute : JobFilterAttribute, IServerFilter
    {
        private readonly string _mutexName;

        public JobMutexAttribute(string mutexName)
        {
            if (string.IsNullOrWhiteSpace(mutexName))
            {
                throw new ArgumentException("Mutex name is required.", nameof(mutexName));
            }

            _mutexName = mutexName;
        }

        public int WaitMinutes { get; set; } = 45;

        public int RetryDelayMinutes { get; set; } = 10;

        public void OnPerforming(PerformingContext context) =>
            JobMutex.Acquire(context, _mutexName, WaitMinutes, RetryDelayMinutes);

        public void OnPerformed(PerformedContext context) =>
            JobMutex.Release(context, _mutexName);
    }
}
