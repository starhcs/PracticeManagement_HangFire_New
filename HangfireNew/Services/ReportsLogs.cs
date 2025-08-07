using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using HangfireNew.VMModels;
using System.Net.Http;
using Hangfire;

namespace HangfireNew.Services
{
    public interface IReportsLogsService
    {
        Task GenerateAndRmailReportsLogs();
    }
    public class ReportsLogsService : IReportsLogsService
    {
        private readonly ApiSettings _apiSettings;
        private readonly Dictionary<string, UserCredentials> _userCredentials;
        private readonly HttpClient _httpClient;

        public ReportsLogsService(IOptions<ApiSettings> apiSettings, IOptions<CredentialsStore> credentialsStore)
        {
            _apiSettings = apiSettings.Value;
            _userCredentials = credentialsStore.Value.UserCredentials;
            _httpClient = new HttpClient();
        }
        [AutomaticRetry(Attempts = 0)]
        public async Task GenerateAndRmailReportsLogs()
        {
            HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var reportsLogsModel = new
            {
            };

            string ApiAddress = $"{_apiSettings.BaseAddress}";
            string reportsLogsURL = $"{ApiAddress}HangfireJobs/GenerateAndRmailReportsLogs";
            string payloadreportsLogs = JsonConvert.SerializeObject(reportsLogsModel);
            var contentreportsLogs = new StringContent(payloadreportsLogs, Encoding.UTF8, "application/json");
            HttpResponseMessage reportsLogsLogin = await httpClient.PostAsync(reportsLogsURL, contentreportsLogs);
        }
    }
}
