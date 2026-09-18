using Hangfire;
using Hangfire.Dashboard;
using HangfireNew.Controllers;
using HangfireNew.Security;
using HangfireNew.Services;
using HangfireNew.VMModels;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection("ApiSettings"));
builder.Services.Configure<CredentialsStore>(builder.Configuration);

builder.Services.Configure<ConnectionStrings>(builder.Configuration.GetSection("ConnectionStrings"));

// Login for the /hangfire dashboard. Credentials come from the environment
// (DashboardAuth__Username / DashboardAuth__Password, backed by a Key Vault or App
// Configuration reference in staging and production) - never from committed config.
builder.Services.AddHangfireDashboardLogin(builder.Configuration, builder.Environment);

//var jobSettings = builder.Configuration
//    .GetSection("JobSettings")
//    .Get<Dictionary<string, string>>();


var jobSettings = builder.Configuration.GetSection("JobSettings");


// Register services
builder.Services.AddScoped<JobCoordinator>();

builder.Services.AddScoped<IAutoDownloadService, AutoDownloadService>();
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<IPostingService, PostingService>();
builder.Services.AddScoped<IMarkNoShowAppointmentsService, MarkNoShowAppointmentsService>();
builder.Services.AddScoped<IReportsLogsService, ReportsLogsService>();
builder.Services.AddScoped<IAppointmentReminder, AppointmentReminder>();
builder.Services.AddScoped<IAutoEligibilityJobService, AutoEligibility>();

builder.Services.AddControllers();

// Hangfire setup
builder.Services.AddHangfire(x => x.UseSqlServerStorage(builder.Configuration.GetConnectionString("Audit")));
builder.Services.AddHangfireServer();

// --- One-at-a-time rule for the submission jobs ------------------------------------
// "submission-job" (Hourly) and "Daily-submission-job" (Daily) both call
// ISubmissionService.SubmissionJob, so they share one mutex: if one of them is already
// running - scheduled or triggered by hand from /hangfire/recurring - the other one
// waits for it to finish and then runs, instead of running at the same time.
//
// It is registered as a GLOBAL filter (not an attribute) because Hangfire resolves
// filter attributes from the job type/method stored in the Hangfire tables, which for
// "() => submissionService.SubmissionJob(...)" is the concrete SubmissionService - an
// attribute on ISubmissionService is silently ignored.
//
// To make another job wait for submission too, add its method name to the same filter,
// e.g. new JobMutexFilter("submission-jobs", nameof(ISubmissionService.SubmissionJob),
//                         nameof(IPostingService.PostingJob))
GlobalJobFilters.Filters.Add(new JobMutexFilter(
    "submission-jobs",
    nameof(ISubmissionService.SubmissionJob))
{
    WaitMinutes = 10,
    RetryDelayMinutes = 10
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Hangfire Dashboard - reachable only with a valid dashboard sign-in session.
// See HangfireNew.Security.HangfireDashboardAuth for how the login works.
var dashboardAuthOptions = app.Services.GetRequiredService<IOptionsMonitor<DashboardAuthOptions>>();

if (!dashboardAuthOptions.CurrentValue.IsConfigured)
{
    app.Logger.LogWarning(
        "DashboardAuth:Username / DashboardAuth:Password are not configured - nobody can sign in to " +
        "/hangfire until they are set. Background jobs are unaffected.");
}

app.UseHangfireDashboardLogin();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new DashboardAuthorizationFilter() }
});

//using (var scope = app.Services.CreateScope())
//{
//    var coordinator = scope.ServiceProvider.GetRequiredService<JobCoordinator>();

//    RecurringJob.AddOrUpdate(
//        "coordinated-autodownload",
//        () => coordinator.JobsChain(),
//        "0 */2 * * *"
//    );
//}
//RecurringJob.AddOrUpdate<JobCoordinator>(
//    "coordinated-autodownload",
//    coordinator => coordinator.JobsChain(),
//    "0 */2 * * *"
//);
//BackgroundJob.Enqueue<JobCoordinator>(coordinator => coordinator.JobsChain());
using (var scope = app.Services.CreateScope())
{
    var autoDownloadService = scope.ServiceProvider.GetRequiredService<IAutoDownloadService>();
    var postingService = scope.ServiceProvider.GetRequiredService<IPostingService>();
    var submissionService = scope.ServiceProvider.GetRequiredService<ISubmissionService>();
    var marknoshowService = scope.ServiceProvider.GetRequiredService<IMarkNoShowAppointmentsService>();
    var reportsLogsService = scope.ServiceProvider.GetRequiredService<IReportsLogsService>();
    var appointmentReminder = scope.ServiceProvider.GetRequiredService<IAppointmentReminder>();
    var AutoEligibilityJob = scope.ServiceProvider.GetRequiredService<IAutoEligibilityJobService>();


    RecurringJob.RemoveIfExists("auto-download-job");
    RecurringJob.RemoveIfExists("submission-job");
    RecurringJob.RemoveIfExists("Daily-submission-job");
    RecurringJob.RemoveIfExists("posting-job");
    RecurringJob.RemoveIfExists("mark-noshow-job");
    RecurringJob.RemoveIfExists("reports-logs-job");
    RecurringJob.RemoveIfExists("appointment-email-job");
    RecurringJob.RemoveIfExists("AppointmentEligibilityJob");
    RecurringJob.RemoveIfExists("AutoEligibilityJob");


    // --- 1. Auto Download Job ---
    if (jobSettings["Downloading"] == "1")
    {
        RecurringJob.AddOrUpdate("auto-download-job", () => autoDownloadService.AutoDownloadJob(), "0 */2 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("auto-download-job");
    }
    // --- 2. Submission Job ---
    if (jobSettings["Submission"] == "1")
    {
        RecurringJob.AddOrUpdate("submission-job", () => submissionService.SubmissionJob("Hourly"), "15 */2 * * *");
        RecurringJob.AddOrUpdate("Daily-submission-job", () => submissionService.SubmissionJob("Daily"), "0 22 * * *", new RecurringJobOptions { TimeZone = EasternTimeZone() });
    }
    else
    {
        RecurringJob.RemoveIfExists("submission-job");
        RecurringJob.RemoveIfExists("Daily-submission-job");    
    }
    // --- 3. Posting Job ---
    if (jobSettings["Posting"] == "1")
    {
        RecurringJob.AddOrUpdate("posting-job", () => postingService.PostingJob(), "30 */2 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("posting-job");
    }
    // --- 4. Mark NoShow Job ---
    if (jobSettings["NoShow"] == "1")
    {
        RecurringJob.AddOrUpdate("mark-noshow-job", () => marknoshowService.MarkNoShowAppointmentsJob(), "0 */23 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("mark-noshow-job");
    }
    // --- 5. Reports Logs Job ---
    if (jobSettings["Reports"] == "1")
    {
        RecurringJob.AddOrUpdate("reports-logs-job", () => reportsLogsService.GenerateAndRmailReportsLogs(), "0 13 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("reports-logs-job");
    }
    // --- 6. Appointment Email Job ---
    if (jobSettings["Reminders"] == "1")
    {
        RecurringJob.AddOrUpdate("appointment-email-job", () => appointmentReminder.AppointmentReminderJob(), "*/5 * * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("appointment-email-job");
    }
    // --- 6. Appointment Email Job ---
    if (jobSettings["AppointmentEligibility"] == "1")
    {
        RecurringJob.AddOrUpdate(
        "AppointmentEligibilityJob",
        () => AutoEligibilityJob.AutoAppointmentEligibilityJob(),
         "0 0 * * 0"
            );
    }
    else
    {
        RecurringJob.RemoveIfExists("AppointmentEligibilityJob");
    }
}
static TimeZoneInfo EasternTimeZone()
{
    foreach (string id in new[] { "Eastern Standard Time", "America/New_York" })
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
        }
    }

    throw new TimeZoneNotFoundException("Eastern time zone not found on this machine.");
}

app.MapControllers();
app.Run();