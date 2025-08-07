using Hangfire;
using Hangfire.Dashboard;
using HangfireNew.Controllers;
using HangfireNew.Services;
using HangfireNew.VMModels;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection("ApiSettings"));
builder.Services.Configure<CredentialsStore>(builder.Configuration);

// Register services
builder.Services.AddScoped<JobCoordinator>();

builder.Services.AddScoped<IAutoDownloadService, AutoDownloadService>();
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<IPostingService, PostingService>();
builder.Services.AddScoped<IMarkNoShowAppointmentsService, MarkNoShowAppointmentsService>();
builder.Services.AddScoped<IReportsLogsService, ReportsLogsService>();

builder.Services.AddControllers();

// Hangfire setup
builder.Services.AddHangfire(x => x.UseSqlServerStorage(builder.Configuration.GetConnectionString("Audit")));
builder.Services.AddHangfireServer();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Hangfire Dashboard (testing only)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AllowAllDashboardAuthorizationFilter() }
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

    RecurringJob.RemoveIfExists("auto-download-job");
    RecurringJob.RemoveIfExists("submission-job");
    RecurringJob.RemoveIfExists("posting-job");
    RecurringJob.RemoveIfExists("mark-noshow-job");
    RecurringJob.RemoveIfExists("reports-logs-job");

    RecurringJob.AddOrUpdate(
        "auto-download-job",
        () => autoDownloadService.AutoDownloadJob(),
        "0 */2 * * *" // every 2 hours, at minute 0
    );

    RecurringJob.AddOrUpdate(
        "submission-job",
        () => submissionService.SubmissionJob(),
        "15 */2 * * *" // every 2 hours, at minute 15
    );

    RecurringJob.AddOrUpdate(
        "posting-job",
        () => postingService.PostingJob(),
        "30 */2 * * *" // every 2 hours, at minute 30
    );

    RecurringJob.AddOrUpdate(
        "mark-noshow-job",
        () => marknoshowService.MarkNoShowAppointmentsJob(),
        "45 */2 * * *" // every 2 hours, at minute 45
    );

    RecurringJob.AddOrUpdate(
        "reports-logs-job",
        () => reportsLogsService.GenerateAndRmailReportsLogs(),
        "0 13 * * *" // once a day at 1:00 PM
    );
}


app.MapControllers();
app.Run();

// Authorization filter
public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
