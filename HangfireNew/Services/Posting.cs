using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using HangfireNew.Controllers;
using HangfireNew.VMModels;
using Hangfire;

namespace HangfireNew.Services
{
    public interface IPostingService
    {
        Task PostingJob();
    }
    public class PostingService : IPostingService
    {
        private readonly ApiSettings _apiSettings;
        private readonly Dictionary<string, UserCredentials> _userCredentials;
        private readonly HttpClient _httpClient;

        public PostingService(IOptions<ApiSettings> apiSettings, IOptions<CredentialsStore> credentialsStore)
        {
            _apiSettings = apiSettings.Value;
            _userCredentials = credentialsStore.Value.UserCredentials;
            _httpClient = new HttpClient();
        }
        public async Task<int> GetLastLogIDAsync()
        {
            using HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(15);
            var model = new
            {
                TableName = "POSTINGJOBLOGS"
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
            httpClient.Timeout = TimeSpan.FromMinutes(15);
            var model = new
            {
                TableName = "POSTINGJOBLOGS",
                LogsNature = "Posting",
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
        public async Task PostingJob()
        {
            int lastLogID = await GetLastLogIDAsync() + 1;
            if (lastLogID <= 0)
            {
                throw new Exception("Invalid lastLogID received. Aborting Posting job.");
            }
            HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(15);
            var loginModel = new
            {
                Email = _userCredentials["PostingJob"].Email,
                Password = _userCredentials["PostingJob"].Password
            };
            string ApiAddress = $"{_apiSettings.BaseAddress}";
            string loginURL = $"{ApiAddress}Login/Login";
            string WriteLogsURL = $"{ApiAddress}HangfireJobs/WritePostingJobLog";
            string payloadLogin = JsonConvert.SerializeObject(loginModel);

            var contentLogin = new StringContent(payloadLogin, Encoding.UTF8, "application/json");


            HttpResponseMessage responseLogin = await httpClient.PostAsync(loginURL, contentLogin);
            if (responseLogin.IsSuccessStatusCode)
            {
                var job_started_model = new
                {
                    TableName = "POSTINGJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", "Posting Job Started" },
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
                    Value = "PostedJobs"
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
                        TableName = "POSTINGJOBLOGS",
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
                        var practices_not_fetched_model = new
                        {
                            PracticeID = 0,
                            TableName = "POSTINGJOBLOGS",
                            Data = new Dictionary<string, string>
                            {
                                { "Message", "Practices not found" },
                                { "ExceptionMsg", $"{responseFetchedPractices.ReasonPhrase}" }
                            }
                        };
                        string payloadPracticesNotfetched = JsonConvert.SerializeObject(practices_not_fetched_model);
                        var contentPracticesNotfetched = new StringContent(payloadPracticesNotfetched, Encoding.UTF8, "application/json");
                        HttpResponseMessage responsePracticesNotfetched = await httpClient.PostAsync(WriteLogsURL, contentPracticesNotfetched);

                    }
                    else
                    {
                        //DataTable dataTable = ConvertListToDataTable(data);
                        //DataRow  dataRow= dataTable.Rows[0];
                        for (int i = 0; i < data.Count; i++)
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
                                ///POSTING LOGIC 
                                if (responseGetToken.IsSuccessStatusCode)
                                {
                                    string responseGetTokenContent = await responseGetToken.Content.ReadAsStringAsync();
                                    var GetTokenJsonObject = JObject.Parse(responseGetTokenContent);
                                    string gettoken = GetTokenJsonObject["token"].ToString();
                                    httpClient = new HttpClient();
                                    httpClient.Timeout = TimeSpan.FromMinutes(15);
                                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gettoken);
                                    var downloading_files_model = new
                                    {
                                        TableName = "POSTINGJOBLOGS",
                                        Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Posting Started for {data[i].PracticeName}" },
                                            { "ExceptionMsg", "" }
                                        }
                                    };
                                    string payloadDownloadingFiles = JsonConvert.SerializeObject(downloading_files_model);
                                    var contentDownloadingFiles = new StringContent(payloadDownloadingFiles, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseDownloadingFiles = await httpClient.PostAsync(WriteLogsURL, contentDownloadingFiles);
                                    var POSTINGMODEL = new
                                    {
                                        TableName = "CheckDetails"
                                    };
                                    string processFileJson = JsonConvert.SerializeObject(POSTINGMODEL);
                                    var processFileContent = new StringContent(processFileJson, Encoding.UTF8, "application/json");

                                    string processFileUrl = $"{ApiAddress}Payment/GetcheckforPosting";

                                    HttpResponseMessage response1 = await httpClient.PostAsync(processFileUrl, processFileContent);
                                    if (response1.IsSuccessStatusCode)
                                    {
                                        string Check = await response1.Content.ReadAsStringAsync();
                                        JArray CheckArray = JArray.Parse(Check);
                                        var CheckIds = JsonConvert.DeserializeObject<List<PostEra>>(Check);

                                        if (CheckIds == null && CheckIds!.Count == 0)
                                        {

                                        }
                                        else
                                        {
                                            JArray array = JArray.FromObject(CheckIds);

                                            foreach (JObject Header in array)
                                            {

                                                    string TableName = "Postera";
                                                    string HeaderID = Header["HeaderID"].ToString();
                                                    var Model = new
                                                    {
                                                        TableName = TableName,
                                                        Data = new Dictionary<string, string>
                                                    {
                                                        {"HeaderIDs", HeaderID}
                                                    }
                                                    };

                                                    string processFileJson1 = JsonConvert.SerializeObject(Model);
                                                    var processFileContent1 = new StringContent(processFileJson1, Encoding.UTF8, "application/json");

                                                    string processFileUrl1 = $"{ApiAddress}Payment/PostEra";

                                                    HttpResponseMessage response2 = await httpClient.PostAsync(processFileUrl1, processFileContent1);

                                                    if (response2.IsSuccessStatusCode)
                                                    {

                                                        var process_files_after_download_model = new
                                                        {
                                                            TableName = "POSTINGJOBLOGS",
                                                            Data = new Dictionary<string, string>
                                                        {
                                                            { "Message", $"{Header["PaymentType"].ToString()} for Check  {Header["TRNCHECKNUMBER"].ToString()}" },
                                                            { "ExceptionMsg", "" }
                                                        }
                                                        };
                                                        string payloadProcessFilesAfterDownload = JsonConvert.SerializeObject(process_files_after_download_model);
                                                        var contentProcessFilesAfterDownload = new StringContent(payloadProcessFilesAfterDownload, Encoding.UTF8, "application/json");
                                                        HttpResponseMessage responseProcessFilesAfterDownload = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownload);
                                                    }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                }
                            }
                            else
                            {
                                var switchPracticeFailedModel = new
                                {
                                    TableName = "POSTINGJOBLOGS",
                                    Data = new Dictionary<string, string>
                                    {
                                        { "Message", $"Could not switch to Practice: {data[i].PracticeName}" },
                                        { "ExceptionMsg", responseSwitchPractice.ReasonPhrase ?? "No additional details" }
                                    }
                                };

                                string payloadSwitchPracticeFailed = JsonConvert.SerializeObject(switchPracticeFailedModel);
                                var contentSwitchPracticeFailed = new StringContent(payloadSwitchPracticeFailed, Encoding.UTF8, "application/json");
                                HttpResponseMessage responseSwitchPracticeFailed = await httpClient.PostAsync(WriteLogsURL, contentSwitchPracticeFailed);


                            }
                        }


                    }
                }

            int lastLogID1 = await GetLastLogIDAsync();
            string response = await SendLogsEmail(lastLogID, lastLogID1);
            Console.WriteLine(response);
            }
            else
            {
                var login_failed_model = new
                {
                    PracticeID = 0,
                    TableName = "POSTINGJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", "Login Failed" },
                            { "ExceptionMsg", $"{responseLogin.ReasonPhrase}" }
                    }
                };
                string payloadLoginFailed = JsonConvert.SerializeObject(responseLogin);
                var contentLoginFailed = new StringContent(payloadLoginFailed, Encoding.UTF8, "application/json");
                HttpResponseMessage responseLoginFailed = await httpClient.PostAsync(WriteLogsURL, contentLoginFailed);

            }
        }
    }
}

