using HangfireNew.Controllers;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HangfireNew.VMModels;
using Hangfire;

namespace HangfireNew.Services
{
    public interface ISubmissionService
    {
        Task SubmissionJob();
    }

    public class SubmissionService : ISubmissionService
    {
        private readonly ApiSettings _apiSettings;
        private readonly Dictionary<string, UserCredentials> _userCredentials;
        private readonly HttpClient _httpClient;

        public SubmissionService(IOptions<ApiSettings> apiSettings, IOptions<CredentialsStore> credentialsStore)
        {
            _apiSettings = apiSettings.Value;
            _userCredentials = credentialsStore.Value.UserCredentials;
            _httpClient = new HttpClient(); 
        }
        public async Task<int> GetLastLogIDAsync()
        {
            using HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var model = new
            {
                TableName = "SUBMISSIONJOBLOGS"
            };

            string apiUrl = $"{_apiSettings.BaseAddress}HangfireJobs/GetLastLogID";
            string payload = JsonConvert.SerializeObject(model);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                string jsonResult = await response.Content.ReadAsStringAsync();
                int logId = JsonConvert.DeserializeObject<int>(jsonResult);
                return logId;
            }
            else
            {

                throw new Exception($"API call failed: {response.StatusCode}");
            }
        }
        public async Task<string> SendLogsEmail(int initialLogID, int finalLogID)
        {
            using HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var model = new
            {
                TableName = "SUBMISSIONJOBLOGS",
                LogsNature = "Submission",
                Data = new
                {
                    InitialLogID = initialLogID.ToString(),
                    FinalLogID = finalLogID.ToString()
                }
            };


            string apiUrl = $"{_apiSettings.BaseAddress}HangfireJobs/SendLogsEmail";
            string payload = JsonConvert.SerializeObject(model);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                string jsonResult = await response.Content.ReadAsStringAsync();
                return jsonResult;
            }
            else
            {
                throw new Exception($"API call failed: {response.StatusCode}");
            }
        }
        [AutomaticRetry(Attempts = 0)]
        public async Task SubmissionJob()
        {
            int lastLogID = await GetLastLogIDAsync() + 1;
            if (lastLogID <= 0)
            {
                throw new Exception("Invalid lastLogID received. Aborting Submission job.");
            }
            HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var loginModel = new
            {
                Email = _userCredentials["SubmissionJob"].Email,
                Password = _userCredentials["SubmissionJob"].Password
            };
            string ApiAddress = $"{_apiSettings.BaseAddress}";
            string loginURL = $"{ApiAddress}Login/Login";
            string WriteLogsURL = $"{ApiAddress}HangfireJobs/WriteSubmissionJobLog";
            string payloadLogin = JsonConvert.SerializeObject(loginModel);

            var contentLogin = new StringContent(payloadLogin, Encoding.UTF8, "application/json");


            HttpResponseMessage responseLogin = await httpClient.PostAsync(loginURL, contentLogin);
            if (responseLogin.IsSuccessStatusCode)
            {
                var job_started_model = new
                {
                    TableName = "SUBMISSIONJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", "Submission Job Started" },
                            { "ExceptionMsg", "" }
                    }
                };
                string payloadJobStarted = JsonConvert.SerializeObject(job_started_model);
                var contentJobStarted = new StringContent(payloadJobStarted, Encoding.UTF8, "application/json");
                HttpResponseMessage responseJobStarted = await httpClient.PostAsync(WriteLogsURL, contentJobStarted);
                string responseContent = await responseLogin.Content.ReadAsStringAsync();
                var jsonObject = JObject.Parse(responseContent);
                string token = jsonObject["token"].ToString();
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                string PracticesUrl = $"{ApiAddress}General/GetAllPractices";
                var PracticesModel = new
                {
                    TableName = "Practice",
                    Value = "SubmissionJobs"
                };
                string PracticesPayloadJson = JsonConvert.SerializeObject(PracticesModel);

                var contentPractices = new StringContent(PracticesPayloadJson, Encoding.UTF8, "application/json");

                HttpResponseMessage responsePractices = await httpClient.PostAsync(PracticesUrl, contentPractices);
                if (responsePractices.IsSuccessStatusCode)
                {
                    string responsePracticesContent = await responsePractices.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<List<Practices>>(responsePracticesContent);
                    var fetched_practices_model = new
                    {
                        PracticeID = 0,
                        TableName = "SUBMISSIONJOBLOGS",
                        Data = new Dictionary<string, string>
                        {
                            { "Message", $"Practices Found : {data?.Count ?? 0}" },
                            { "ExceptionMsg", "" }
                        }
                    };
                    string payloadFetchedPractices = JsonConvert.SerializeObject(fetched_practices_model);
                    var contentFetchedPractices = new StringContent(payloadFetchedPractices, Encoding.UTF8, "application/json");
                    HttpResponseMessage responseFetchedPractices = await httpClient.PostAsync(WriteLogsURL, contentFetchedPractices);
                    if (data == null && data!.Count == 0)
                    {

                    }
                    else
                    {

                        for (int i = 0; i < data.Count; i++) /////////// practices loop iteration
                        {
                            string switchPracticeUrl = $"{ApiAddress}General/SwitchPractice";
                            var switchPracticeModel = new
                            {
                                TableName = "Practice",
                                Data = new
                                {
                                    CurrentPracticeID = $"{data[i].PracticeID}"
                                }
                            };

                            string SwitchPracticepayloadJson = JsonConvert.SerializeObject(switchPracticeModel);

                            var SwitchPracticeContent = new StringContent(SwitchPracticepayloadJson, Encoding.UTF8, "application/json");
                            HttpResponseMessage responseSwitchPractice = await httpClient.PostAsync(switchPracticeUrl, SwitchPracticeContent);
                            if (responseSwitchPractice.IsSuccessStatusCode)
                            {

                                

                                string GetTokenUrl = $"{ApiAddress}Login/GetFreshToken";
                                var GetTokenModel = new
                                {
                                    TableName = "User",

                                };

                                string GetTokenpayloadJson = JsonConvert.SerializeObject(GetTokenModel);

                                var GetTokenContent = new StringContent(GetTokenpayloadJson, Encoding.UTF8, "application/json");
                                HttpResponseMessage responseGetToken = await httpClient.PostAsync(GetTokenUrl, GetTokenContent);
                                if (responseGetToken.IsSuccessStatusCode)
                                {
                                    string responseGetTokenContent = await responseGetToken.Content.ReadAsStringAsync();
                                    var GetTokenJsonObject = JObject.Parse(responseGetTokenContent);
                                    string gettoken = GetTokenJsonObject["token"].ToString();
                                    httpClient = new HttpClient();
                                    httpClient.Timeout = TimeSpan.FromMinutes(5);
                                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gettoken);
                                    var downloading_files_model = new
                                    {
                                        TableName = "SUBMISSIONJOBLOGS",
                                        Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Submission Started for {data[i].PracticeName}" },
                                            { "ExceptionMsg", "" }
                                        }
                                    };
                                    string payloadDownloadingFiles = JsonConvert.SerializeObject(downloading_files_model);
                                    var contentDownloadingFiles = new StringContent(payloadDownloadingFiles, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseDownloadingFiles = await httpClient.PostAsync(WriteLogsURL, contentDownloadingFiles);
                                    var POSTINGMODEL = new
                                    {
                                        TableName = "SubmitterReceiver",
                                        Value = "SubmissionJobs"
                                    };



                                    string processFileJson = JsonConvert.SerializeObject(POSTINGMODEL);
                                    var processFileContent = new StringContent(processFileJson, Encoding.UTF8, "application/json");

                                    string processFileUrl = $"{ApiAddress}Download/SubmitterReceiverAutoDownload";

                                    HttpResponseMessage response1 = await httpClient.PostAsync(processFileUrl, processFileContent);
                                    if (response1.IsSuccessStatusCode)
                                    {
                                        string responseSubmitterReceiverContent = await response1.Content.ReadAsStringAsync();
                                        JArray submitterReceiverArray = JArray.Parse(responseSubmitterReceiverContent);
                                        var SubmitterReceiver = JsonConvert.DeserializeObject<List<SubmitterReceiverIds>>(responseSubmitterReceiverContent);
                                        if (SubmitterReceiver == null && SubmitterReceiver!.Count == 0)
                                        {

                                        }
                                        else
                                        {
                                            ////Professional Claim Submission
                                            for (int j = 0; j < SubmitterReceiver.Count; j++)
                                            {
                                                string SubmitterReceiverID = SubmitterReceiver[j].SubmitterReceiverID.ToString();

                                                var Model = new
                                                {
                                                    ID = SubmitterReceiverID,
                                                    TableName = "ElectronicSubmissionJobs",
                                                    Value = "P"
                                                };

                                                string processFileJson1 = JsonConvert.SerializeObject(Model);
                                                var processFileContent1 = new StringContent(processFileJson1, Encoding.UTF8, "application/json");

                                                string processFileUrl1 = $"{ApiAddress}ElectronicSubmission/GetClaimIDsAutoSubmission";

                                                HttpResponseMessage response2 = await httpClient.PostAsync(processFileUrl1, processFileContent1);

                                                if (response2.IsSuccessStatusCode)
                                                {

                                                    string resp = await response2.Content.ReadAsStringAsync();

                                                    var Model1 = new
                                                    {
                                                        TableName = "Generate837P",
                                                        ClaimIds = resp,
                                                        SubmitterReceiverID = SubmitterReceiverID,
                                                        CallingFrom = "JOB",
                                                    }
                                                ;
                                                    string processFileJson2 = JsonConvert.SerializeObject(Model1);
                                                    var processFileContent2 = new StringContent(processFileJson2, Encoding.UTF8, "application/json");
                                                    string processFileUrl2 = $"{ApiAddress}ElectronicSubmission/SubmitProfessionalClaims";
                                                    HttpResponseMessage response3 = await httpClient.PostAsync(processFileUrl2, processFileContent2);
                                                    if (response3.IsSuccessStatusCode)
                                                    {

                                                        string response3String = await response3.Content.ReadAsStringAsync();

                                                        var OBJ = JObject.Parse(response3String);
                                                        string ediClaimBatchID = OBJ["ediClaimBatchID"].ToString();



                                                        var Models = new
                                                        {
                                                            TableName = "EDIClaimBatch",
                                                            SearchCriteria = new { EDIClaimBatchID = ediClaimBatchID }
                                                        };

                                                        string FileJson1 = JsonConvert.SerializeObject(Models);
                                                        var FileContent1 = new StringContent(processFileJson1, Encoding.UTF8, "application/json");

                                                        string FileUrl1 = $"{ApiAddress}/EDI/UploadClaimFile";

                                                        HttpResponseMessage response2Trnsmit = await httpClient.PostAsync(FileUrl1, FileContent1);

                                                        if (response2Trnsmit.IsSuccessStatusCode)
                                                        {
                                                            var process_files_after_download_model = new
                                                            {
                                                                TableName = "SUBMISSIONJOBLOGS",
                                                                Data = new Dictionary<string, string>
                                                                {
                                                                    { "Message", $"Professional Submission processed for SubmitterReceiverID {Model.ID}" },
                                                                    { "ExceptionMsg", "" }
                                                                }
                                                            };
                                                            string payloadProcessFilesAfterDownload = JsonConvert.SerializeObject(process_files_after_download_model);
                                                            var contentProcessFilesAfterDownload = new StringContent(payloadProcessFilesAfterDownload, Encoding.UTF8, "application/json");
                                                            HttpResponseMessage responseProcessFilesAfterDownload = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownload);

                                                        }

                                                    }
                                                    else
                                                    {
                                                        var process_files_after_download_failed_model = new
                                                        {
                                                            TableName = "SUBMISSIONJOBLOGS",
                                                            Data = new Dictionary<string, string>
                                                        {
                                                            { "Message", $"Professional Submission failed for SubmitterReceiverID {Model.ID}" },
                                                            { "ExceptionMsg", $"{response3.ReasonPhrase}"}
                                                        }
                                                        };
                                                        string payloadProcessFilesAfterDownloadFailed = JsonConvert.SerializeObject(process_files_after_download_failed_model);
                                                        var contentProcessFilesAfterDownloadFailed = new StringContent(payloadProcessFilesAfterDownloadFailed, Encoding.UTF8, "application/json");
                                                        HttpResponseMessage responseProcessFilesAfterDownloadFailed = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownloadFailed);
                                                    }
                                                }
                                                else
                                                {

                                                    var downloading_failed_for_file_model = new
                                                    {
                                                        TableName = "SUBMISSIONJOBLOGS",
                                                        Data = new Dictionary<string, string>
                                                        {
                                                            { "Message", $"Professional Submission failed for SubmitterReceiverID {Model.ID}" },
                                                            { "ExceptionMsg", "An error occurred while creating the SFTP submission" }
                                                        }
                                                    };
                                                    string payloadFailedForFile = JsonConvert.SerializeObject(downloading_failed_for_file_model);
                                                    var contentFailedForFile = new StringContent(payloadFailedForFile, Encoding.UTF8, "application/json");
                                                    HttpResponseMessage responseFailedForFile = await httpClient.PostAsync(WriteLogsURL, contentFailedForFile);


                                                }

                                            }

                                            ////Institutional Claim Submission
                                            for (int j = 0; j < SubmitterReceiver.Count; j++)
                                            {
                                                string SubmitterReceiverID = SubmitterReceiver[j].SubmitterReceiverID.ToString();

                                                var Model = new
                                                {
                                                    ID = SubmitterReceiverID,
                                                    TableName = "ElectronicSubmissionJobs",
                                                    Value = "I"
                                                };

                                                string processFileJson1 = JsonConvert.SerializeObject(Model);
                                                var processFileContent1 = new StringContent(processFileJson1, Encoding.UTF8, "application/json");

                                                string processFileUrl1 = $"{ApiAddress}ElectronicSubmission/GetClaimIDsAutoSubmission";

                                                HttpResponseMessage response2 = await httpClient.PostAsync(processFileUrl1, processFileContent1);

                                                if (response2.IsSuccessStatusCode)
                                                {

                                                    string resp = await response2.Content.ReadAsStringAsync();

                                                    var Model1 = new
                                                    {
                                                        TableName = "Generate837I",
                                                        ClaimIds = resp,
                                                        SubmitterReceiverID = SubmitterReceiverID,
                                                        CallingFrom = "JOB",
                                                    };
                                                    string processFileJson2 = JsonConvert.SerializeObject(Model1);
                                                    var processFileContent2 = new StringContent(processFileJson2, Encoding.UTF8, "application/json");
                                                    string processFileUrl2 = $"{ApiAddress}ElectronicSubmission/SubmitInstitutionalClaims";
                                                    HttpResponseMessage response3 = await httpClient.PostAsync(processFileUrl2, processFileContent2);
                                                    if (response3.IsSuccessStatusCode)
                                                    {

                                                        string response3String = await response3.Content.ReadAsStringAsync();

                                                        var OBJ = JObject.Parse(response3String);
                                                        string ediClaimBatchID = OBJ["ediClaimBatchID"].ToString();

                                                        var Models = new
                                                        {
                                                            TableName = "EDIClaimBatch",
                                                            SearchCriteria = new { EDIClaimBatchID = ediClaimBatchID }
                                                        };

                                                        string FileJson1 = JsonConvert.SerializeObject(Models);
                                                        var FileContent1 = new StringContent(processFileJson1, Encoding.UTF8, "application/json");

                                                        string FileUrl1 = $"{ApiAddress}/EDI/UploadClaimFile";

                                                        HttpResponseMessage response2Trnsmit = await httpClient.PostAsync(FileUrl1, FileContent1);

                                                        if (response2Trnsmit.IsSuccessStatusCode)
                                                        {
                                                            var process_files_after_download_model = new
                                                            {
                                                                TableName = "SUBMISSIONJOBLOGS",
                                                                Data = new Dictionary<string, string>
                                                                {
                                                                    { "Message", $"Institutional Submission processed for SubmitterReceiverID {Model.ID}" },
                                                                    { "ExceptionMsg", "" }
                                                                }
                                                            };
                                                            string payloadProcessFilesAfterDownload = JsonConvert.SerializeObject(process_files_after_download_model);
                                                            var contentProcessFilesAfterDownload = new StringContent(payloadProcessFilesAfterDownload, Encoding.UTF8, "application/json");
                                                            HttpResponseMessage responseProcessFilesAfterDownload = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownload);

                                                        }

                                                    }
                                                    else
                                                    {
                                                        var process_files_after_download_failed_model = new
                                                        {
                                                            TableName = "SUBMISSIONJOBLOGS",
                                                            Data = new Dictionary<string, string>
                                                        {
                                                            { "Message", $"Institutional Submission failed for SubmitterReceiverID {Model.ID}" },
                                                            { "ExceptionMsg", $"{response3.ReasonPhrase}"}
                                                        }
                                                        };
                                                        string payloadProcessFilesAfterDownloadFailed = JsonConvert.SerializeObject(process_files_after_download_failed_model);
                                                        var contentProcessFilesAfterDownloadFailed = new StringContent(payloadProcessFilesAfterDownloadFailed, Encoding.UTF8, "application/json");
                                                        HttpResponseMessage responseProcessFilesAfterDownloadFailed = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownloadFailed);
                                                    }
                                                }
                                                else
                                                {

                                                    var downloading_failed_for_file_model = new
                                                    {
                                                        TableName = "SUBMISSIONJOBLOGS",
                                                        Data = new Dictionary<string, string>
                                                        {
                                                            { "Message", $"Institutional Submission failed for SubmitterReceiverID {Model.ID}" },
                                                            { "ExceptionMsg", "An error occurred while creating the SFTP submission" }
                                                        }
                                                    };
                                                    string payloadFailedForFile = JsonConvert.SerializeObject(downloading_failed_for_file_model);
                                                    var contentFailedForFile = new StringContent(payloadFailedForFile, Encoding.UTF8, "application/json");
                                                    HttpResponseMessage responseFailedForFile = await httpClient.PostAsync(WriteLogsURL, contentFailedForFile);


                                                }

                                            }
                                        }

                                    }
                                }
                                else
                                {
                                    ////Handle API error
                                }
                                var downloading_finished_for_practice_model = new
                                {
                                    TableName = "SUBMISSIONJOBLOGS",
                                    Data = new Dictionary<string, string>
                                                    {
                                                        { "Message", $"Submission Finished for {data[i].PracticeName}" },
                                                        { "ExceptionMsg", "" }
                                                    }
                                };
                                string payloadFinishedForPractice = JsonConvert.SerializeObject(downloading_finished_for_practice_model);
                                var contentFinishedForPractice = new StringContent(payloadFinishedForPractice, Encoding.UTF8, "application/json");
                                HttpResponseMessage responseFinishedForPractice = await httpClient.PostAsync(WriteLogsURL, contentFinishedForPractice);

                            }
                        }

                        var job_finished_model = new
                        {
                            PracticeID = 0,
                            TableName = "SUBMISSIONJOBLOGS",
                            Data = new Dictionary<string, string>
                            {
                                { "Message", $"Submission Job Ended" },
                                { "ExceptionMsg", "" }
                            }
                        };
                        string payloadJobFinished = JsonConvert.SerializeObject(job_finished_model);
                        var contentJobFinished = new StringContent(payloadJobFinished, Encoding.UTF8, "application/json");
                        HttpResponseMessage responseJobFinished = await httpClient.PostAsync(WriteLogsURL, contentJobFinished);

                    }
                }
                else if (!responsePractices.IsSuccessStatusCode)
                {
                    var didnot_fetch_practices_model = new
                    {
                        PracticeID = 0,
                        TableName = "SUBMISSIONJOBLOGS",
                        Data = new Dictionary<string, string>
                        {
                            { "Message", $"Practices not Found " },
                            { "ExceptionMsg", $"{responsePractices.ReasonPhrase}" }
                        }
                    };
                    string payloadDidnotFetchPractices = JsonConvert.SerializeObject(didnot_fetch_practices_model);
                    var contentDidnotFetchPractices = new StringContent(payloadDidnotFetchPractices, Encoding.UTF8, "application/json");
                    HttpResponseMessage responseDidnotFetchPractices = await httpClient.PostAsync(WriteLogsURL, contentDidnotFetchPractices);
                }

            int lastLogID1 = await GetLastLogIDAsync();
            string response = await SendLogsEmail(lastLogID, lastLogID1);
            Console.WriteLine(response);
            }
            else if (!responseLogin.IsSuccessStatusCode)
            {
                var job_didnot_start_model = new
                {
                    PracticeID = 0,
                    TableName = "SUBMISSIONJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", $"Submission Job could not be started " },
                            { "ExceptionMsg", $"{responseLogin.ReasonPhrase}" }
                    }
                };
                string payloadJobDidnotStart = JsonConvert.SerializeObject(job_didnot_start_model);
                var contentJobDidnotStart = new StringContent(payloadJobDidnotStart, Encoding.UTF8, "application/json");
                HttpResponseMessage responseJobDidnotStarted = await httpClient.PostAsync(WriteLogsURL, contentJobDidnotStart);
            }
        }
    }
}
