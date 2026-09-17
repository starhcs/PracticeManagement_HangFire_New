using Hangfire;
using HangfireNew.VMModels;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace HangfireNew.Services
{
    public interface ISubmissionService
    {
        // NOTE: the "one at a time" rule for the Hourly / Daily submission runs is NOT
        // applied here. Hangfire ignores filter attributes placed on an interface when
        // the job was registered through an interface-typed variable, so it is applied
        // as a global filter (JobMutexFilter) in Program.cs instead.
        Task SubmissionJob(string jobType);
    }

    public class SubmissionService : ISubmissionService
    {
        #region Constants

        private const string LogsTableName = "SUBMISSIONJOBLOGS";
        private const string LogsNature = "Submission Job Logs";
        private const string CredentialsKey = "SubmissionJob";

        private static readonly TimeSpan HttpTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The two claim flavours the job submits. Everything that differs between
        /// Professional and Institutional submission lives here, so the submission
        /// logic below is written only once.
        /// </summary>
        private static readonly ClaimTypeSetting[] ClaimTypes =
        {
            new("P", "Professional",  "Generate837P", "ElectronicSubmission/SubmitProfessionalClaims"),
            new("I", "Institutional", "Generate837I", "ElectronicSubmission/SubmitInstitutionalClaims")
        };

        private sealed record ClaimTypeSetting(
            string ExportFormat,
            string Label,
            string GenerateTableName,
            string SubmitEndpoint);

        #endregion

        #region Fields / ctor

        private readonly ApiSettings _apiSettings;
        private readonly Dictionary<string, UserCredentials> _userCredentials;
        private readonly HttpClient _httpClient;

        public SubmissionService(IOptions<ApiSettings> apiSettings, IOptions<CredentialsStore> credentialsStore)
        {
            _apiSettings = apiSettings.Value;
            _userCredentials = credentialsStore.Value.UserCredentials;
            _httpClient = new HttpClient { Timeout = HttpTimeout };
        }

        #endregion

        #region Endpoints

        private string Url(string relativePath) => $"{_apiSettings.BaseAddress}{relativePath}";

        private string LoginUrl => Url("Login/Login");
        private string FreshTokenUrl => Url("Login/GetFreshToken");
        private string WriteLogsUrl => Url("HangfireJobs/WriteSubmissionJobLog");
        private string LastLogIdUrl => Url("HangfireJobs/GetLastLogID");
        private string SendLogsEmailUrl => Url("HangfireJobs/SendLogsEmail");
        private string PracticesUrl => Url("General/GetAllPractices");
        private string SwitchPracticeUrl => Url("General/SwitchPractice");
        private string SubmitterReceiverUrl => Url("Download/SubmitterReceiverAutoDownload");
        private string ClaimIdsUrl => Url("ElectronicSubmission/GetClaimIDsAutoSubmission");
        private string UploadClaimFileUrl => Url("EDI/UploadClaimFile");

        private string Jobtype = string.Empty; 
        #endregion

        #region Job entry point

        [AutomaticRetry(Attempts = 0)]
        public async Task SubmissionJob(string jobType)
        {
            Jobtype = jobType;
            int initialLogId = await GetLastLogIDAsync() + 1;
            if (initialLogId <= 0)
            {
                throw new Exception("Invalid lastLogID received. Aborting Submission job.");
            }

            if (!await LoginAsync())
            {
                return;
            }

            await WriteLogAsync($"{Jobtype} : Submission Job Started");

            await ProcessAllPracticesAsync();

            int finalLogId = await GetLastLogIDAsync();
            string emailResponse = await SendLogsEmail(initialLogId, finalLogId);
            Console.WriteLine(emailResponse);
        }

        #endregion

        #region Practices

        private async Task ProcessAllPracticesAsync()
        {
            HttpResponseMessage response = await PostAsync(PracticesUrl, new
            {
                TableName = "Practice",
                Value = "SubmissionJobs"
            });

            if (!response.IsSuccessStatusCode)
            {
                await WriteLogAsync($"{Jobtype} : Practices not Found ", response.ReasonPhrase);
                return;
            }

            var practices = JsonConvert.DeserializeObject<List<Practices>>(
                await response.Content.ReadAsStringAsync());

            await WriteLogAsync($"{Jobtype} : Practices Found : {practices?.Count ?? 0}");
            if (practices == null)
            {
                return;
            }

            foreach (Practices practice in practices)
            {
                await ProcessPracticeAsync(practice);
            }

            await WriteLogAsync($"{Jobtype} : Submission Job Ended");
        }

        private async Task ProcessPracticeAsync(Practices practice)
        {
            if (await SwitchPracticeAsync(practice.PracticeID))
            {
                string? token = await GetFreshTokenAsync();

                if (!string.IsNullOrWhiteSpace(token))
                {
                    UseBearerToken(token);
                    await WriteLogAsync($"{Jobtype} : Submission Started for {practice.PracticeName}");
                    await SubmitPracticeClaimsAsync();
                }
            }

            await WriteLogAsync($"{Jobtype} : Submission Finished for {practice.PracticeName}");
        }

        private async Task<bool> SwitchPracticeAsync(int practiceId)
        {
            HttpResponseMessage response = await PostAsync(SwitchPracticeUrl, new
            {
                TableName = "Practice",
                Data = new { CurrentPracticeID = practiceId.ToString() }
            });

            return response.IsSuccessStatusCode;
        }

        #endregion

        #region Claim submission

        private async Task SubmitPracticeClaimsAsync()
        {
            List<SubmitterReceiverIds>? submitterReceivers = await GetSubmitterReceiversAsync();
            if (submitterReceivers == null || submitterReceivers.Count == 0)
            {
                return;
            }

            // Professional first, then Institutional (same order as before).
            foreach (ClaimTypeSetting claimType in ClaimTypes)
            {
                foreach (SubmitterReceiverIds submitterReceiver in submitterReceivers)
                {
                    if (IsClaimType(submitterReceiver, claimType))
                    {
                        await SubmitBatchAsync(claimType, submitterReceiver.SubmitterReceiverID.ToString());
                    }
                }
            }
        }

        private async Task<List<SubmitterReceiverIds>?> GetSubmitterReceiversAsync()
        {
            HttpResponseMessage response = await PostAsync(SubmitterReceiverUrl, new
            {
                TableName = "SubmitterReceiver",
                Value = "SubmissionJobs"
            });

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return JsonConvert.DeserializeObject<List<SubmitterReceiverIds>>(
                await response.Content.ReadAsStringAsync());
        }

        private static bool IsClaimType(SubmitterReceiverIds submitterReceiver, ClaimTypeSetting claimType) =>
            string.Equals(submitterReceiver.ExportFormat?.Trim(), claimType.ExportFormat,
                StringComparison.OrdinalIgnoreCase);

        private async Task SubmitBatchAsync(ClaimTypeSetting claimType, string submitterReceiverId)
        {
            // 1. Which claims are waiting for this submitter/receiver?
            HttpResponseMessage claimIdsResponse = await PostAsync(ClaimIdsUrl, new
            {
                ID = submitterReceiverId,
                TableName = "ElectronicSubmissionJobs",
                Value = claimType.ExportFormat,
                Data = new Dictionary<string, string>
                {
                    ["Jobtype"] = Jobtype
                }
            });

            if (!claimIdsResponse.IsSuccessStatusCode)
            {
                await WriteLogAsync(
                    $"{Jobtype} : {claimType.Label} Submission failed | for SubmitterReceiverID {submitterReceiverId} " +
                    "and No Claim Found For Auto Submission and  apis Not Get Data " +
                    "ElectronicSubmission/GetClaimIDsAutoSubmission");
                return;
            }

            string claimIds = await claimIdsResponse.Content.ReadAsStringAsync();

            // 2. Generate the 837 batch.
            HttpResponseMessage submitResponse = await PostAsync(Url(claimType.SubmitEndpoint), new
            {
                TableName = claimType.GenerateTableName,
                ClaimIds = claimIds,
                SubmitterReceiverID = submitterReceiverId,
                CallingFrom = $"JOB : {Jobtype}",

            });

            if (!submitResponse.IsSuccessStatusCode)
            {
                await WriteLogAsync(
                    $"{Jobtype} : {claimType.Label} Submission failed | for SubmitterReceiverID {submitterReceiverId} | and  CLAIMIDS :  {claimIds}",
                    submitResponse.ReasonPhrase);
                return;
            }

            // 3. Transmit the generated batch file.
            string batchId = await ReadEdiClaimBatchIdAsync(submitResponse);
            bool transmitted = await TryTransmitBatchAsync(batchId);

            await WriteLogAsync(transmitted
                ? $"{Jobtype} : {claimType.Label} Submission processed and File Transmit Batch {batchId}| for SubmitterReceiverID {submitterReceiverId} and  CLAIMIDS :  {claimIds} "
                : $"{Jobtype} : {claimType.Label} Submission processed and No  File Transmit  Batch {batchId}| for SubmitterReceiverID {submitterReceiverId} and  CLAIMIDS :  {claimIds}");
        }
        private async Task<bool> TryTransmitBatchAsync(string ediClaimBatchId)
        {
            if (!int.TryParse(ediClaimBatchId, out int batchId) || batchId <= 0)
            {
                return false;
            }

            HttpResponseMessage response = await PostAsync(UploadClaimFileUrl, new
            {
                TableName = "EDIClaimBatch",
                ID = ediClaimBatchId,
                SearchCriteria = new Dictionary<string, string>
                {
                    { "EDIClaimBatchID", ediClaimBatchId }
                }
            });

            return response.IsSuccessStatusCode;
        }

        private static async Task<string> ReadEdiClaimBatchIdAsync(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();
            return JObject.Parse(body)["ediClaimBatchID"]?.ToString() ?? string.Empty;
        }

        #endregion

        #region Authentication

        private async Task<bool> LoginAsync()
        {
            UserCredentials credentials = _userCredentials[CredentialsKey];

            HttpResponseMessage response = await PostAsync(LoginUrl, new
            {
                credentials.Email,
                credentials.Password
            });

            if (!response.IsSuccessStatusCode)
            {
                await WriteLogAsync($"{Jobtype} : Submission Job could not be started ", response.ReasonPhrase);
                return false;
            }

            string? token = ReadToken(await response.Content.ReadAsStringAsync());
            if (string.IsNullOrWhiteSpace(token))
            {
                await WriteLogAsync($"{Jobtype} : Submission Job could not be started ", "No token returned by Login/Login");
                return false;
            }

            UseBearerToken(token);
            return true;
        }

        private async Task<string?> GetFreshTokenAsync()
        {
            HttpResponseMessage response = await PostAsync(FreshTokenUrl, new { TableName = "User" });

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return ReadToken(await response.Content.ReadAsStringAsync());
        }

        private static string? ReadToken(string responseBody) =>
            JObject.Parse(responseBody)["token"]?.ToString();

        private void UseBearerToken(string token) =>
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        #endregion

        #region Logging

        private async Task WriteLogAsync(string message, string? exceptionMessage = null)
        {
            try
            {
                await PostAsync(WriteLogsUrl, new
                {
                    TableName = LogsTableName,
                    Data = new Dictionary<string, string>
                    {
                        { "Message", message },
                        { "ExceptionMsg", exceptionMessage ?? string.Empty }
                    }
                });
            }
            catch (Exception ex)
            {
                // A failed log write must never take the whole job down.
                //Console.WriteLine($"{Jobtype} :  [submission-job] Could not write log \"{message}\": {ex.Message}");
            }
        }

        public async Task<int> GetLastLogIDAsync()
        {
            HttpResponseMessage response = await PostAsync(LastLogIdUrl, new { TableName = LogsTableName });
            string json = await ReadSuccessBodyAsync(response);

            return JsonConvert.DeserializeObject<int>(json);
        }

        public async Task<string> SendLogsEmail(int initialLogID, int finalLogID)
        {
            HttpResponseMessage response = await PostAsync(SendLogsEmailUrl, new
            {
                TableName = LogsTableName,
                LogsNature = Jobtype +" "+LogsNature,
                Data = new
                {
                    InitialLogID = initialLogID.ToString(),
                    FinalLogID = finalLogID.ToString()
                }
            });

            return await ReadSuccessBodyAsync(response);
        }

        #endregion

        #region HTTP helpers

        private Task<HttpResponseMessage> PostAsync(string url, object payload)
        {
            string json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            return _httpClient.PostAsync(url, content);
        }

        private static async Task<string> ReadSuccessBodyAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API call failed: {response.StatusCode}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        #endregion
    }
}
