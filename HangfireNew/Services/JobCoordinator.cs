using Hangfire;

namespace HangfireNew.Services
{
    public class JobCoordinator
    {
        private readonly IAutoDownloadService _autoDownloadService;
        private readonly ISubmissionService _submissionService;
        private readonly IPostingService _postingService;
        private readonly IMarkNoShowAppointmentsService _markNoShowAppointmentsService;

        public JobCoordinator(
            IAutoDownloadService autoDownloadService,
            ISubmissionService submissionService,
            IPostingService postingService,
            IMarkNoShowAppointmentsService markNoShowAppointmentsService)
        {
            _autoDownloadService = autoDownloadService;
            _submissionService = submissionService;
            _postingService = postingService;
            _markNoShowAppointmentsService = markNoShowAppointmentsService;
        }

        public void JobsChain()
        {
           // _autoDownloadService.AutoDownloadJob();
           // BackgroundJob.Schedule(
           //    () => _postingService.PostingJob(),
           //    TimeSpan.FromMinutes(30)
           //);
           // BackgroundJob.Schedule(
           //     () => _submissionService.SubmissionJob(),
           //     TimeSpan.FromMinutes(60)
           // );
           // BackgroundJob.Schedule(
           //     () => _markNoShowAppointmentsService.MarkNoShowAppointmentsJob(),
           //     TimeSpan.FromMinutes(75)
           // );
        }
    }
}
