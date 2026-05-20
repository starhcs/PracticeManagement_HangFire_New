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

builder.Services.Configure<ConnectionStrings>(builder.Configuration.GetSection("ConnectionStrings"));

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
    var appointmentReminder = scope.ServiceProvider.GetRequiredService<IAppointmentReminder>();
    var AutoEligibilityJob = scope.ServiceProvider.GetRequiredService<IAutoEligibilityJobService>();


    RecurringJob.RemoveIfExists("auto-download-job");
    RecurringJob.RemoveIfExists("submission-job");
    RecurringJob.RemoveIfExists("posting-job"); //TV27424589,TV27364757
    RecurringJob.RemoveIfExists("mark-noshow-job");
    RecurringJob.RemoveIfExists("reports-logs-job");
    RecurringJob.RemoveIfExists("appointment-email-job");
    RecurringJob.RemoveIfExists("AppointmentEligibilityJob");
    RecurringJob.RemoveIfExists("AutoEligibilityJob");


    

    string aa = jobSettings["Downloading"];

    // --- 1. Auto Download Job ---
    if (jobSettings["Downloading"] == "1")
    {
        RecurringJob.AddOrUpdate("auto-download-job", () => autoDownloadService.AutoDownloadJob(), "0 */2 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("auto-download-job");
    }

    aa = jobSettings["Submission"];
    // --- 2. Submission Job ---
    if (jobSettings["Submission"] == "1")
    {
        RecurringJob.AddOrUpdate("submission-job", () => submissionService.SubmissionJob(), "15 */2 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("submission-job");
    }
    aa = jobSettings["Posting"];
    // --- 3. Posting Job ---
    if (jobSettings["Posting"] == "1")
    {
        RecurringJob.AddOrUpdate("posting-job", () => postingService.PostingJob(), "30 */2 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("posting-job");
    }
    aa = jobSettings["NoShow"];
    // --- 4. Mark NoShow Job ---
    if (jobSettings["NoShow"] == "1")
    {
        RecurringJob.AddOrUpdate("mark-noshow-job", () => marknoshowService.MarkNoShowAppointmentsJob(), "0 */23 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("mark-noshow-job");
    }
    aa = jobSettings["Reports"];
    // --- 5. Reports Logs Job ---
    if (jobSettings["Reports"] == "1")
    {
        RecurringJob.AddOrUpdate("reports-logs-job", () => reportsLogsService.GenerateAndRmailReportsLogs(), "0 13 * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("reports-logs-job");
    }
    aa = jobSettings["Reminders"];
    // --- 6. Appointment Email Job ---
    if (jobSettings["Reminders"] == "1")
    {
        RecurringJob.AddOrUpdate("appointment-email-job", () => appointmentReminder.AppointmentReminderJob(), "*/5 * * * *");
    }
    else
    {
        RecurringJob.RemoveIfExists("appointment-email-job");
    }
    aa = jobSettings["AppointmentEligibility"];
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


app.MapControllers();
app.Run();

// Authorization filter
public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}







//RecurringJob.AddOrUpdate(
//    "auto-download-job",
//    () => autoDownloadService.AutoDownloadJob(),
//    "0 */2 * * *" // every 2 hours, at minute 0
//);

//RecurringJob.AddOrUpdate(
//    "submission-job",
//    () => submissionService.SubmissionJob(),
//    "15 */2 * * *" // every 2 hours, at minute 15
//);

//RecurringJob.AddOrUpdate(
//    "posting-job",
//    () => postingService.PostingJob(),
//    "30 */2 * * *" // every 2 hours, at minute 30
//);

//RecurringJob.AddOrUpdate(
//    "mark-noshow-job",
//    () => marknoshowService.MarkNoShowAppointmentsJob(),
//    "45 */2 * * *" // every 2 hours, at minute 45
//);

//RecurringJob.AddOrUpdate(
//    "reports-logs-job",
//    () => reportsLogsService.GenerateAndRmailReportsLogs(),
//    "0 13 * * *" // once a day at 1:00 PM
//);

//RecurringJob.AddOrUpdate(
//"appointment-email-job",
//() => appointmentReminder.AppointmentReminderJob(),
//"*/15 * * * *"// every 15 mins //"0 0 * * 0" //Every Sunday 12 AM 
//);



//using Hangfire;
//using Hangfire.Dashboard;
//using HangfireNew.Controllers;
//using HangfireNew.Services;
//using HangfireNew.VMModels;
//using Microsoft.Data.SqlClient;
//using Microsoft.Extensions.Options;

//var builder = WebApplication.CreateBuilder(args);

//// Configuration
//builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection("ApiSettings"));
//builder.Services.Configure<CredentialsStore>(builder.Configuration);

//builder.Services.Configure<ConnectionStrings>(builder.Configuration.GetSection("ConnectionStrings"));

//// Register services
//builder.Services.AddScoped<JobCoordinator>();

//builder.Services.AddScoped<IAutoDownloadService, AutoDownloadService>();
//builder.Services.AddScoped<ISubmissionService, SubmissionService>();
//builder.Services.AddScoped<IPostingService, PostingService>();
//builder.Services.AddScoped<IMarkNoShowAppointmentsService, MarkNoShowAppointmentsService>();
//builder.Services.AddScoped<IReportsLogsService, ReportsLogsService>();
//builder.Services.AddScoped<IAppointmentReminder, AppointmentReminder>();
//builder.Services.AddScoped<IAppointmentEligibilityJobService, AppointmentEligibilityJobService>();


//builder.Services.AddControllers();

//// Hangfire setup
//builder.Services.AddHangfire(x => x.UseSqlServerStorage(builder.Configuration.GetConnectionString("Audit")));
//builder.Services.AddHangfireServer();

//// Swagger
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

//var app = builder.Build();

//app.UseDeveloperExceptionPage();
//app.UseHttpsRedirection();
//app.UseRouting();
//app.UseAuthentication();
//app.UseAuthorization();

//// Hangfire Dashboard (testing only)
//app.UseHangfireDashboard("/hangfire", new DashboardOptions
//{
//    Authorization = new[] { new AllowAllDashboardAuthorizationFilter() }
//});

////using (var scope = app.Services.CreateScope())
////{
////    var coordinator = scope.ServiceProvider.GetRequiredService<JobCoordinator>();

////    RecurringJob.AddOrUpdate(
////        "coordinated-autodownload",
////        () => coordinator.JobsChain(),
////        "0 */2 * * *"
////    );
////}
////RecurringJob.AddOrUpdate<JobCoordinator>(
////    "coordinated-autodownload",
////    coordinator => coordinator.JobsChain(),
////    "0 */2 * * *"
////);
////BackgroundJob.Enqueue<JobCoordinator>(coordinator => coordinator.JobsChain());
//using (var scope = app.Services.CreateScope())
//{
//    var autoDownloadService = scope.ServiceProvider.GetRequiredService<IAutoDownloadService>();
//    var postingService = scope.ServiceProvider.GetRequiredService<IPostingService>();
//    var submissionService = scope.ServiceProvider.GetRequiredService<ISubmissionService>();
//    var marknoshowService = scope.ServiceProvider.GetRequiredService<IMarkNoShowAppointmentsService>();
//    var reportsLogsService = scope.ServiceProvider.GetRequiredService<IReportsLogsService>();
//    var appointmentReminder = scope.ServiceProvider.GetRequiredService<IAppointmentReminder>();
//    var appointmentEligibilityService = scope.ServiceProvider.GetRequiredService<IAppointmentEligibilityJobService>();

//    RecurringJob.RemoveIfExists("auto-download-job");
//    RecurringJob.RemoveIfExists("submission-job");
//    RecurringJob.RemoveIfExists("posting-job");
//    RecurringJob.RemoveIfExists("mark-noshow-job");
//    RecurringJob.RemoveIfExists("reports-logs-job");
//    RecurringJob.RemoveIfExists("appointment-email-job");
//    RecurringJob.RemoveIfExists("AppointmentEligibilityJob");


//    RecurringJob.AddOrUpdate(
//        "auto-download-job",
//        () => autoDownloadService.AutoDownloadJob(),
//        "0 */2 * * *" // every 2 hours, at minute 0
//    );

//    RecurringJob.AddOrUpdate(
//        "submission-job",
//        () => submissionService.SubmissionJob(),
//        "15 */2 * * *" // every 2 hours, at minute 15
//    );

//    RecurringJob.AddOrUpdate(
//        "posting-job",
//        () => postingService.PostingJob(),
//        "30 */2 * * *" // every 2 hours, at minute 30
//    );

//    RecurringJob.AddOrUpdate(
//        "mark-noshow-job",
//        () => marknoshowService.MarkNoShowAppointmentsJob(),
//        //"45 */2 * * *" // every 2 hours, at minute 45
//        "0 */23 * * *" // every 2 hours, at minute 0
//    );

//    RecurringJob.AddOrUpdate(
//        "reports-logs-job",
//        () => reportsLogsService.GenerateAndRmailReportsLogs(),
//        "0 13 * * *" // once a day at 1:00 PM
//    );

//    RecurringJob.AddOrUpdate(
//    "appointment-email-job",
//    () => appointmentReminder.AppointmentReminderJob(),
//      "*/15 * * * *"  // "*/15 * * * *"  /// every 15 mins  //"0 */2 * * *"  
//    );

//    RecurringJob.AddOrUpdate<IAppointmentEligibilityJobService>(
//    "AppointmentEligibilityJob",x => x.AutoAppointmentEligibilityJob_AllPractices(),
//    "*/15 * * * *"

//);


//}


//app.MapControllers();
//app.Run();

//// Authorization filter
//public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
//{
//    public bool Authorize(DashboardContext context) => true;
//}
