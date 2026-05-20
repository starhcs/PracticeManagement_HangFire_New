using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using HangfireNew.VMModels;
using Azure;
using Hangfire;

namespace HangfireNew.Services
{
    public interface IMarkNoShowAppointmentsService
    {
        Task MarkNoShowAppointmentsJob();
    }
    public class MarkNoShowAppointmentsService : IMarkNoShowAppointmentsService
    {
        private readonly ApiSettings _apiSettings;
        private readonly Dictionary<string, UserCredentials> _userCredentials;
        private readonly HttpClient _httpClient;

        public MarkNoShowAppointmentsService(IOptions<ApiSettings> apiSettings, IOptions<CredentialsStore> credentialsStore)
        {
            _apiSettings = apiSettings.Value;
            _userCredentials = credentialsStore.Value.UserCredentials;
            _httpClient = new HttpClient();
        }
        [AutomaticRetry(Attempts = 0)]
        public async Task MarkNoShowAppointmentsJob()
        {
            HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var loginModel = new
            {
                Email = _userCredentials["MarkNoShowAppointmentsJob"].Email,
                Password = _userCredentials["MarkNoShowAppointmentsJob"].Password
            };
            string ApiAddress = $"{_apiSettings.BaseAddress}";
            string loginURL = $"{ApiAddress}Login/Login";
            string WriteLogsURL = $"{ApiAddress}HangfireJobs/WriteMarkNoShowAppointmentsJobLog";
            string payloadLogin = JsonConvert.SerializeObject(loginModel);

            var contentLogin = new StringContent(payloadLogin, Encoding.UTF8, "application/json");


            HttpResponseMessage responseLogin = await httpClient.PostAsync(loginURL, contentLogin);
            if (responseLogin.IsSuccessStatusCode)
            {
                var job_started_model = new
                {
                    TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", "MarkNoShowAppointments Job Started" },
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
                    Value = "MarkNoShowAppointmentsJobs"
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
                        TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
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
                                if (responseGetToken.IsSuccessStatusCode)
                                {
                                    string responseGetTokenContent = await responseGetToken.Content.ReadAsStringAsync();
                                    var GetTokenJsonObject = JObject.Parse(responseGetTokenContent);
                                    string gettoken = GetTokenJsonObject["token"].ToString();
                                    httpClient = new HttpClient();
                                    httpClient.Timeout = TimeSpan.FromMinutes(5);
                                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gettoken);

                                    var mark_noshow_appointments_model = new
                                    {
                                        PracticeID = data[i].PracticeID,
                                        TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                                        Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Marking noshow appointments Started for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                            { "ExceptionMsg", "" }
                                        }
                                    };
                                    string payloadMarkNoshowAppointments = JsonConvert.SerializeObject(mark_noshow_appointments_model);
                                    var contentMarkNoshowAppointments = new StringContent(payloadMarkNoshowAppointments, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseMarkNoshowAppointments = await httpClient.PostAsync(WriteLogsURL, contentMarkNoshowAppointments);

                                    var MARKNOSHOWAPPOINTMENTMODEL = new
                                    {
                                        TableName = "ChangeStatusInAppointment"

                                    };
                                    string markNoshowAppointmentsJson = JsonConvert.SerializeObject(MARKNOSHOWAPPOINTMENTMODEL);
                                    var markNoshowAppointmentsContent = new StringContent(markNoshowAppointmentsJson, Encoding.UTF8, "application/json");

                                    string markNoshowAppointmentsUrl = $"{ApiAddress}General/CreateClaimsFromAppointments";

                                    HttpResponseMessage response1 = await httpClient.PostAsync(markNoshowAppointmentsUrl, markNoshowAppointmentsContent);
                                    if (response1.IsSuccessStatusCode) 
                                    {
                                        var mark_noshow_appointments_model_success = new
                                        {
                                            PracticeID = data[i].PracticeID,
                                            TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                                            Data = new Dictionary<string, string>
                                            {
                                                { "Message", $"Marking noshow appointments Completed for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                                { "ExceptionMsg", "" }
                                            }
                                        };
                                        string payloadMarkNoshowAppointmentsSuccess = JsonConvert.SerializeObject(mark_noshow_appointments_model_success);
                                        var contentMarkNoshowAppointmentsSuccess = new StringContent(payloadMarkNoshowAppointmentsSuccess, Encoding.UTF8, "application/json");
                                        HttpResponseMessage responseMarkNoshowAppointmentsSuccess = await httpClient.PostAsync(WriteLogsURL, contentMarkNoshowAppointmentsSuccess);
                                    }
                                    else 
                                    {
                                        var mark_noshow_appointments_model_failed = new
                                        {
                                            PracticeID = data[i].PracticeID,
                                            TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                                            Data = new Dictionary<string, string>
                                            {
                                                { "Message", $"Marking noshow appointments Failed for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                                { "ExceptionMsg", $"{response1.ReasonPhrase}" }
                                            }
                                        };
                                        string payloadMarkNoshowAppointmentsFailed = JsonConvert.SerializeObject(mark_noshow_appointments_model_failed);
                                        var contentMarkNoshowAppointmentsFailed = new StringContent(payloadMarkNoshowAppointmentsFailed, Encoding.UTF8, "application/json");
                                        HttpResponseMessage responseMarkNoshowAppointmentsFailed = await httpClient.PostAsync(WriteLogsURL, contentMarkNoshowAppointmentsFailed);
                                    }
                                }
                                else
                                {
                                    var switching_practice_failed = new
                                    {
                                        PracticeID = data[i].PracticeID,
                                        TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                                        Data = new Dictionary<string, string>
                                            {
                                                { "Message", $"Switching Practice Failed for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                                { "ExceptionMsg", $"{responseGetToken.ReasonPhrase}" }
                                            }
                                    };
                                    string payloadSwitchingPracticeFailed = JsonConvert.SerializeObject(switching_practice_failed);
                                    var contentSwitchingPracticeFailed = new StringContent(payloadSwitchingPracticeFailed, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responsecontentSwitchingPracticeFailed = await httpClient.PostAsync(WriteLogsURL, contentSwitchingPracticeFailed);
                                }

                                var marking_noshow_appointments_finshed_for_practice_model = new
                                {
                                    TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                                    Data = new Dictionary<string, string>
                                                    {
                                                        { "Message", $"Marking noshow appointments Finished for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                                        { "ExceptionMsg", "" }
                                                    }
                                };
                                string payloadFinishedForPractice = JsonConvert.SerializeObject(marking_noshow_appointments_finshed_for_practice_model);
                                var contentFinishedForPractice = new StringContent(payloadFinishedForPractice, Encoding.UTF8, "application/json");
                                HttpResponseMessage responseFinishedForPractice = await httpClient.PostAsync(WriteLogsURL, contentFinishedForPractice);

                            }
                        }

                        var job_finished_model = new
                        {
                            PracticeID = 0,
                            TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                            Data = new Dictionary<string, string>
                            {
                                { "Message", $"MarkNoShowAppointments Job Ended" },
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
                        TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
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


            }
            else if (!responseLogin.IsSuccessStatusCode)
            {
                var job_didnot_start_model = new
                {
                    PracticeID = 0,
                    TableName = "MARKNOSHOWAPPOINTMENTSJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", $"MarkNoShowAppointments Job could not be started " },
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
