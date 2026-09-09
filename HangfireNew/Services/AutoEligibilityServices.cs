
using Hangfire;
using HangfireNew.VMModels;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;
using static Org.BouncyCastle.Crypto.Engines.SM2Engine;

namespace HangfireNew.Services
{
    public interface IAutoEligibilityJobService
    {
        Task AutoAppointmentEligibilityJob();
    }

    public class AutoEligibility : IAutoEligibilityJobService
    {
        private readonly ApiSettings _apiSettings;
        private readonly CredentialsStore _credentials;

        public AutoEligibility(
            IOptions<ApiSettings> apiSettings,
            IOptions<CredentialsStore> credentials)
        {
            _apiSettings = apiSettings.Value;
            _credentials = credentials.Value;
        }


        [AutomaticRetry(Attempts = 0)]
        public async Task AutoAppointmentEligibilityJob()
        {
            string ApiAddress = $"{_apiSettings.BaseAddress}";
            string WriteLogsURL = $"{ApiAddress}HangfireJobs/WriteEligibilityJobLog";


            int lastLogID = await GetLastLogIDAsync("ELIGIBILITYJOBLOGS") + 1;
            if (lastLogID <= 0)
            {
                throw new Exception("Invalid lastLogID received. Aborting Submission job.");
            }
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(15)
            };

            var job_started_model = new
            {
                TableName = "ELIGIBILITYJOBLOGS",
                Data = new Dictionary<string, string>
                    {
                            { "Message", "Eligibility Job Started" },
                            { "ExceptionMsg", "" }
                    }
            };
            string payloadJobStarted = JsonConvert.SerializeObject(job_started_model);
            var contentJobStarted = new StringContent(payloadJobStarted, Encoding.UTF8, "application/json");
            HttpResponseMessage responseJobStarted = await httpClient.PostAsync(WriteLogsURL, contentJobStarted);


            string token = await Login(httpClient);
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);



            var practices = await GetPractices(httpClient);

            if (practices == null || practices.Count == 0)
                return;

            var AutoEligibilityParams = await GetAutoEligibilityParams(httpClient);


            if (AutoEligibilityParams.IsAutoEligiblity.ToLower() == "true")
            {
                if (AutoEligibilityParams.AutoEligbilityParam == "Appointment")
                {
                    foreach (var practice in practices)
                    {
                        try
                        {
                            // SWITCH PRACTICE
                            await SwitchPractice(httpClient, practice.PracticeID);

                            // GET FRESH TOKEN
                            string freshToken = await GetFreshToken(httpClient);
                            httpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", freshToken);


                            var downloading_files_model = new
                            {
                                TableName = "ELIGIBILITYJOBLOGS",
                                Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Eligibility Job Started for {practice.PracticeName}" },
                                            { "ExceptionMsg", "" }
                                        }
                            };
                            string payloadDownloadingFiles = JsonConvert.SerializeObject(downloading_files_model);
                            var contentDownloadingFiles = new StringContent(payloadDownloadingFiles, Encoding.UTF8, "application/json");
                            HttpResponseMessage responseDownloadingFiles = await httpClient.PostAsync(WriteLogsURL, contentDownloadingFiles);


                            var getResponse = await httpClient.PostAsync(
                                $"{_apiSettings.BaseAddress}Eligibility/GetAppointmentEligibilityJob",
                                CreateContent(new { TableName = "AppointmentEligibility" }));

                            if (!getResponse.IsSuccessStatusCode)
                                continue;

                            var data = JsonConvert.DeserializeObject<List<AppointmentEligibilityDto>>(
                                await getResponse.Content.ReadAsStringAsync());

                            if (data == null || data.Count == 0)
                                continue;

                            foreach (var item in data)
                            {
                                var processResponse = await httpClient.PostAsync(
                                    $"{_apiSettings.BaseAddress}Eligibility/AppointmentEligibilityJob",
                                    CreateContent(new
                                    {
                                        TableName = "GETDATAAPPOINTMENTEDI270GENERATION",
                                        Data = new List<AppointmentEligibilityDto> { item }
                                    }));

                                if (processResponse.IsSuccessStatusCode)
                                {
                                    var process_files_after_download_model = new
                                    {
                                        TableName = "ELIGIBILITYJOBLOGS",
                                        Data = new Dictionary<string, string>
                                                                    {
                                                                        { "Message", $"Practice {practice.PracticeName}, Eligbility Type : Appointment, processed successfully." },
                                                                        { "ExceptionMsg", "" }
                                                                    }
                                    };
                                    string payloadProcessFilesAfterDownload = JsonConvert.SerializeObject(process_files_after_download_model);
                                    var contentProcessFilesAfterDownload = new StringContent(payloadProcessFilesAfterDownload, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseProcessFilesAfterDownload = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownload);
                                }
                                else
                                {
                                    var process_files_after_download_model = new
                                    {
                                        TableName = "ELIGIBILITYJOBLOGS",
                                        Data = new Dictionary<string, string>
                                                                    {
                                                                        { "Message", "" },
                                                                        { "ExceptionMsg", $"Practice {practice.PracticeName}, Eligbility Type : Appointment, process Failed." }
                                                                    }
                                    };
                                    string payloadProcessFilesAfterDownload = JsonConvert.SerializeObject(process_files_after_download_model);
                                    var contentProcessFilesAfterDownload = new StringContent(payloadProcessFilesAfterDownload, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseProcessFilesAfterDownload = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownload);
                                }

                                if (!processResponse.IsSuccessStatusCode)
                                {

                                    continue;
                                }
                            }



                            var downloading_files_model1 = new
                            {
                                TableName = "ELIGIBILITYJOBLOGS",
                                Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Eligibility Job Ended for {practice.PracticeName}" },
                                            { "ExceptionMsg", "" }
                                        }
                            };
                            string payloadDownloadingFiles1 = JsonConvert.SerializeObject(downloading_files_model1);
                            var contentDownloadingFiles1 = new StringContent(payloadDownloadingFiles1, Encoding.UTF8, "application/json");
                            HttpResponseMessage responseDownloadingFiles1 = await httpClient.PostAsync(WriteLogsURL, contentDownloadingFiles1);
                        }
                        catch
                        {

                            continue;
                        }

                    }
                }

                if (AutoEligibilityParams.AutoEligbilityParam == "Patient")
                {
                    foreach (var practice in practices)
                    {
                        try
                        {

                            // SWITCH PRACTICE
                            await SwitchPractice(httpClient, practice.PracticeID);

                            // GET FRESH TOKEN
                            string freshToken = await GetFreshToken(httpClient);
                            httpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", freshToken);


                            var downloading_files_model = new
                            {
                                TableName = "ELIGIBILITYJOBLOGS",
                                Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Eligibility Job Started for {practice.PracticeName}" },
                                            { "ExceptionMsg", "" }
                                        }
                            };
                            string payloadDownloadingFiles = JsonConvert.SerializeObject(downloading_files_model);
                            var contentDownloadingFiles = new StringContent(payloadDownloadingFiles, Encoding.UTF8, "application/json");
                            HttpResponseMessage responseDownloadingFiles = await httpClient.PostAsync(WriteLogsURL, contentDownloadingFiles);



                            var getResponse = await httpClient.PostAsync(
                                $"{_apiSettings.BaseAddress}Eligibility/GetPatientAutoEligibilityJob",
                                CreateContent(new { TableName = "AppointmentEligibility" }));

                            if (!getResponse.IsSuccessStatusCode)
                                continue;

                            var data = JsonConvert.DeserializeObject<List<PatientEligibilityDto>>(
                             await getResponse.Content.ReadAsStringAsync());

                            if (data == null || data.Count == 0)
                                return;

                            foreach (var item in data)
                            {
                                var processResponse = await httpClient.PostAsync(
                                    $"{_apiSettings.BaseAddress}Eligibility/EligibilityRequest",
                                    CreateContent(new
                                    {
                                        TableName = "Generate270",
                                        Data = new List<PatientEligibilityDto> { item }
                                    }));

                                if (processResponse.IsSuccessStatusCode)
                                {
                                    var process_files_after_download_model = new
                                    {
                                        TableName = "ELIGIBILITYJOBLOGS",
                                        Data = new Dictionary<string, string>
                                                                    {
                                                                        { "Message", $"Practice {practice.PracticeName}, Eligbility Type : Patient, processed successfully." },
                                                                        { "ExceptionMsg", "" }
                                                                    }
                                    };
                                    string payloadProcessFilesAfterDownload = JsonConvert.SerializeObject(process_files_after_download_model);
                                    var contentProcessFilesAfterDownload = new StringContent(payloadProcessFilesAfterDownload, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseProcessFilesAfterDownload = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownload);
                                }
                                else
                                {
                                    var process_files_after_download_model = new
                                    {
                                        TableName = "ELIGIBILITYJOBLOGS",
                                        Data = new Dictionary<string, string>
                                                                    {
                                                                        { "Message", "" },
                                                                        { "ExceptionMsg", $"Practice {practice.PracticeName}, Eligbility Type : Patient, process Failed." }
                                                                    }
                                    };
                                    string payloadProcessFilesAfterDownload = JsonConvert.SerializeObject(process_files_after_download_model);
                                    var contentProcessFilesAfterDownload = new StringContent(payloadProcessFilesAfterDownload, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseProcessFilesAfterDownload = await httpClient.PostAsync(WriteLogsURL, contentProcessFilesAfterDownload);
                                }

                                if (!processResponse.IsSuccessStatusCode)
                                {

                                    continue;
                                }


                            }



                            var downloading_files_model1 = new
                            {
                                TableName = "ELIGIBILITYJOBLOGS",
                                Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Eligibility Job Ended for {practice.PracticeName}" },
                                            { "ExceptionMsg", "" }
                                        }
                            };
                            string payloadDownloadingFiles1 = JsonConvert.SerializeObject(downloading_files_model1);
                            var contentDownloadingFiles1 = new StringContent(payloadDownloadingFiles1, Encoding.UTF8, "application/json");
                            HttpResponseMessage responseDownloadingFiles1 = await httpClient.PostAsync(WriteLogsURL, contentDownloadingFiles1);


                        }
                        catch
                        {

                            continue;
                        }
                    }
                }
            }

            var job_finished_model = new
            {
                PracticeID = 0,
                TableName = "ELIGIBILITYJOBLOGS",
                Data = new Dictionary<string, string>
                            {
                                { "Message", $"Eligibility Job Ended" },
                                { "ExceptionMsg", "" }
                            }
            };
            string payloadJobFinished = JsonConvert.SerializeObject(job_finished_model);
            var contentJobFinished = new StringContent(payloadJobFinished, Encoding.UTF8, "application/json");
            HttpResponseMessage responseJobFinished = await httpClient.PostAsync(WriteLogsURL, contentJobFinished);


            int lastLogID1 = await GetLastLogIDAsync("ELIGIBILITYJOBLOGS");
            string response = await SendLogsEmail(lastLogID, lastLogID1);

        }


        public async Task<int> GetLastLogIDAsync(string tablename )
        {
            using HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var model = new
            {
                TableName = tablename
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

        private async Task<string> Login(HttpClient httpClient)
        {
            var model = new
            {
                Email = _credentials.UserCredentials["AppointmentEligibilityJob"].Email,
                Password = _credentials.UserCredentials["AppointmentEligibilityJob"].Password
            };

            var response = await httpClient.PostAsync(
                $"{_apiSettings.BaseAddress}Login/Login",
                CreateContent(model));

            if (!response.IsSuccessStatusCode)
                throw new Exception("Login failed");

            return JObject.Parse(await response.Content.ReadAsStringAsync())["token"]!.ToString();
        }

        private async Task<List<Practices>> GetPractices(HttpClient httpClient)
        {
            var response = await httpClient.PostAsync(
                $"{_apiSettings.BaseAddress}General/GetAllPractices",
                CreateContent(new
                {
                    TableName = "Practice",
                    Value = "AppointmentEligibilityJob"
                }));

            if (!response.IsSuccessStatusCode)
                return new List<Practices>();

            return JsonConvert.DeserializeObject<List<Practices>>(
                await response.Content.ReadAsStringAsync());
        }

        private async Task SwitchPractice(HttpClient httpClient, int practiceId)
        {
            var response = await httpClient.PostAsync(
                $"{_apiSettings.BaseAddress}General/SwitchPractice",
                CreateContent(new
                {
                    TableName = "Practice",
                    Data = new { CurrentPracticeID = practiceId.ToString() }
                }));

            if (!response.IsSuccessStatusCode)
                throw new Exception("SwitchPractice failed");
        }

        private async Task<string> GetFreshToken(HttpClient httpClient)
        {
            var response = await httpClient.PostAsync(
                $"{_apiSettings.BaseAddress}Login/GetFreshToken",
                CreateContent(new { TableName = "User" }));

            if (!response.IsSuccessStatusCode)
                throw new Exception("GetFreshToken failed");

            return JObject.Parse(await response.Content.ReadAsStringAsync())["token"]!.ToString();
        }

        private static StringContent CreateContent(object model)
        {
            return new StringContent(
                JsonConvert.SerializeObject(model),
                Encoding.UTF8,
                "application/json");
        }

        public async Task<AutoEligibilityParams> GetAutoEligibilityParams(HttpClient httpClient)
        {
            var response = await httpClient.PostAsync(
                $"{_apiSettings.BaseAddress}General/FindByID",
                CreateContent(new
                {
                    TableName = "PracticesSetting",
                    SearchCriteria = new
                    {
                        Search = "AutoEligibilityJob",

                    }
                }));

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<AutoEligibilityParams>(json);
        }

        public async Task<string> SendLogsEmail(int initialLogID, int finalLogID)
        {
            using HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(15);
            var model = new
            {
                TableName = "ELIGIBILITYJOBLOGS",
                LogsNature = "Eligibility",
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

    }



    public class AppointmentEligibilityDto
    {
        public string PatientAppointmentID { get; set; }
        public string PatientID { get; set; }
        public string EligiblityDate { get; set; }
        public string InsuranceID { get; set; }
        public string PatientInsuranceID { get; set; }
        public string ProviderID { get; set; }
        public string ServiceTypeCode { get; set; }
        public string UsePracticeNPI { get; set; }
    }

    public class PatientEligibilityDto
    {
        // public string PatientID { get; set; }
        //public string EligiblityDate { get; set; }
        //public string InsuranceID { get; set; }
        public string PatientInsuranceID { get; set; }
        public string ProviderID { get; set; }
        public string ServiceTypeCode { get; set; }
        public string UsePracticeNPI { get; set; }
    }

    public class Practices
    {
        public int PracticeID { get; set; }
        public string PracticeName { get; set; }
    }
    public class AutoEligibilityParams
    {
        public string IsAutoEligiblity { get; set; }
        public string AutoEligibilityTime { get; set; }
        public string AutoEligbilityParam { get; set; }
        public string AppointmentDays { get; set; }
    }

}

////////////////////////////////////
//using Hangfire;
//using HangfireNew.VMModels;
//using Microsoft.Extensions.Options;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using System.Net.Http.Headers;
//using System.Text;

//namespace HangfireNew.Services
//{
//    public interface IAppointmentEligibilityJobService
//    {
//        Task AutoAppointmentEligibilityJob_Practice16();
//    }

//    public class AppointmentEligibilityJobService : IAppointmentEligibilityJobService
//    {
//        private readonly ApiSettings _apiSettings;
//        private readonly CredentialsStore _credentials;

//        public AppointmentEligibilityJobService(
//            IOptions<ApiSettings> apiSettings,
//            IOptions<CredentialsStore> credentials)
//        {
//            _apiSettings = apiSettings.Value;
//            _credentials = credentials.Value;
//        }

//        [AutomaticRetry(Attempts = 0)]
//        public async Task AutoAppointmentEligibilityJob_Practice16()
//        {
//            const int practiceId = 16;

//            using var httpClient = new HttpClient
//            {
//                Timeout = TimeSpan.FromMinutes(15)
//            };

//            // ================= LOGIN =================
//            string token = await Login(httpClient);
//            httpClient.DefaultRequestHeaders.Authorization =
//                new AuthenticationHeaderValue("Bearer", token);

//            // ================= SWITCH PRACTICE =================
//            await SwitchPractice(httpClient, practiceId);

//            // ================= GET FRESH TOKEN =================
//            string freshToken = await GetFreshToken(httpClient);
//            httpClient.DefaultRequestHeaders.Authorization =
//                new AuthenticationHeaderValue("Bearer", freshToken);

//            // ================= API 1 : GET DATA =================
//            var getResponse = await httpClient.PostAsync(
//                $"{_apiSettings.BaseAddress}Eligibility/GetAppointmentEligibilityJob",
//                CreateContent(new
//                {
//                    TableName = "AppointmentEligibility"
//                }));

//            if (!getResponse.IsSuccessStatusCode)
//                throw new Exception("GetAppointmentEligibilityJob failed");

//            var data =
//                JsonConvert.DeserializeObject<List<AppointmentEligibilityDto>>(
//                    await getResponse.Content.ReadAsStringAsync());

//            if (data == null || data.Count == 0)
//                return;

//            // ================= API 2 : PROCESS DATA =================
//            var processResponse = await httpClient.PostAsync(
//                $"{_apiSettings.BaseAddress}Eligibility/AppointmentEligibilityJob",
//                CreateContent(new
//                {
//                    TableName = "GETDATAAPPOINTMENTEDI270GENERATION",
//                    Data = data
//                }));

//            if (!processResponse.IsSuccessStatusCode)
//                throw new Exception("AppointmentEligibilityJob failed");
//        }

//        // ================= HELPER METHODS =================

//        private async Task<string> Login(HttpClient httpClient)
//        {
//            var model = new
//            {
//                Email = _credentials.UserCredentials["AppointmentEligibilityJob"].Email,
//                Password = _credentials.UserCredentials["AppointmentEligibilityJob"].Password
//            };

//            var response = await httpClient.PostAsync(
//                $"{_apiSettings.BaseAddress}Login/Login",
//                CreateContent(model));

//            if (!response.IsSuccessStatusCode)
//                throw new Exception("Login failed");

//            return JObject
//                .Parse(await response.Content.ReadAsStringAsync())["token"]!
//                .ToString();
//        }

//        private async Task SwitchPractice(HttpClient httpClient, int practiceId)
//        {
//            var response = await httpClient.PostAsync(
//                $"{_apiSettings.BaseAddress}General/SwitchPractice",
//                CreateContent(new
//                {
//                    TableName = "Practice",
//                    Data = new { CurrentPracticeID = practiceId.ToString() }
//                }));

//            if (!response.IsSuccessStatusCode)
//                throw new Exception("SwitchPractice failed");
//        }

//        private async Task<string> GetFreshToken(HttpClient httpClient)
//        {
//            var response = await httpClient.PostAsync(
//                $"{_apiSettings.BaseAddress}Login/GetFreshToken",
//                CreateContent(new { TableName = "User" }));

//            if (!response.IsSuccessStatusCode)
//                throw new Exception("GetFreshToken failed");

//            return JObject
//                .Parse(await response.Content.ReadAsStringAsync())["token"]!
//                .ToString();
//        }

//        private static StringContent CreateContent(object model)
//        {
//            return new StringContent(
//                JsonConvert.SerializeObject(model),
//                Encoding.UTF8,
//                "application/json");
//        }
//    }

//    // ================= DTO =================
//    public class AppointmentEligibilityDto
//    {
//        public string PatientAppointmentID { get; set; }
//        public string PatientID { get; set; }
//        public string EligiblityDate { get; set; }
//        public string InsuranceID { get; set; }
//        public string PatientInsuranceID { get; set; }
//        public string ProviderID { get; set; }
//        public string ServiceTypeCode { get; set; }
//        public string UsePracticeNPI { get; set; }
//    }
//}





////using Hangfire;
////using HangfireNew.VMModels;
////using Microsoft.Extensions.Options;
////using Newtonsoft.Json;
////using Newtonsoft.Json.Linq;
////using System.Text;

////namespace HangfireNew.Services
////{
////    public interface IAppointmentEligibilityJobService
////    {
////        Task AutoAppointmentEligibilityJob();
////    }
////    public class AppointmentEligibilityJobService : IAppointmentEligibilityJobService
////    {
////        private readonly ApiSettings _apiSettings;
////        private readonly CredentialsStore _credentials;
////        private readonly HttpClient _httpClient;

////        public AppointmentEligibilityJobService(
////            IOptions<ApiSettings> apiSettings,
////            IOptions<CredentialsStore> credentials)
////        {
////            _apiSettings = apiSettings.Value;
////            _credentials = credentials.Value;
////            _httpClient = new HttpClient
////            {
////                Timeout = TimeSpan.FromMinutes(15)
////            };
////        }

////        [AutomaticRetry(Attempts = 0)]
////        public async Task AutoAppointmentEligibilityJob()
////        {

////            var loginModel = new
////            {
////                Email = _credentials.UserCredentials["AppointmentEligibilityJob"].Email,
////                Password = _credentials.UserCredentials["AppointmentEligibilityJob"].Password
////            };

////            var loginResponse = await _httpClient.PostAsync(
////                $"{_apiSettings.BaseAddress}Login/Login",
////                new StringContent(JsonConvert.SerializeObject(loginModel), Encoding.UTF8, "application/json")
////            );

////            if (!loginResponse.IsSuccessStatusCode)
////                throw new Exception("Login failed");

////            var loginJson = JObject.Parse(await loginResponse.Content.ReadAsStringAsync());
////            string token = loginJson["token"]!.ToString();

////            _httpClient.DefaultRequestHeaders.Authorization =
////                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

////            var firstApiModel = new
////            {
////                TableName = "AppointmentEligibility"
////            };

////            var firstApiResponse = await _httpClient.PostAsync(
////                $"{_apiSettings.BaseAddress}Eligibility/GetAppointmentEligibilityJob",
////                new StringContent(JsonConvert.SerializeObject(firstApiModel), Encoding.UTF8, "application/json")
////            );

////            if (!firstApiResponse.IsSuccessStatusCode)
////                throw new Exception("GetAppointmentEligibilityJob failed");

////            var firstApiData =
////                JsonConvert.DeserializeObject<List<AppointmentEligibilityDto>>(
////                    await firstApiResponse.Content.ReadAsStringAsync()
////                );

////            if (firstApiData == null || firstApiData.Count == 0)
////                return;
////            var secondApiModel = new
////            {
////                TableName = "GETDATAAPPOINTMENTEDI270GENERATION",
////                Data = firstApiData.Select(x => new
////                {
////                    PatientAppointmentID = x.PatientAppointmentID?.ToString() ?? "",
////                    PatientID = x.PatientID?.ToString() ?? "",
////                    EligiblityDate = x.EligiblityDate?.ToString() ?? "",
////                    InsuranceID = x.InsuranceID?.ToString() ?? "",
////                    PatientInsuranceID = x.PatientInsuranceID?.ToString() ?? "",
////                    ProviderID = x.ProviderID?.ToString() ?? "",
////                    ServiceTypeCode = x.ServiceTypeCode?.ToString() ?? "",
////                    UsePracticeNPI = x.UsePracticeNPI.ToString().ToLower()
////                }).ToList()
////            };


////            var secondApiResponse = await _httpClient.PostAsync(
////                $"{_apiSettings.BaseAddress}Eligibility/AppointmentEligibilityJob",
////                new StringContent(JsonConvert.SerializeObject(secondApiModel), Encoding.UTF8, "application/json")
////            );

////            if (!secondApiResponse.IsSuccessStatusCode)
////                throw new Exception("AppointmentEligibilityJob failed");
////        }
////    }

////}

////public class AppointmentEligibilityDto
////{
////    public string PatientAppointmentID { get; set; }
////    public string PatientID { get; set; }
////    public string EligiblityDate { get; set; }
////    public string InsuranceID { get; set; }
////    public string PatientInsuranceID { get; set; }
////    public string ProviderID { get; set; }
////    public string ServiceTypeCode { get; set; }
////    public string UsePracticeNPI { get; set; }
////}

//using Hangfire;
//using HangfireNew.VMModels;
//using Microsoft.Extensions.Options;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using System.Text;

//namespace HangfireNew.Services
//{
//    public interface IAppointmentEligibilityJobService
//    {
//        Task AutoAppointmentEligibilityJob();
//    }

//    public class AppointmentEligibilityJobService : IAppointmentEligibilityJobService
//    {
//        private readonly ApiSettings _apiSettings;
//        private readonly CredentialsStore _credentials;

//        public AppointmentEligibilityJobService(
//            IOptions<ApiSettings> apiSettings,
//            IOptions<CredentialsStore> credentials)
//        {
//            _apiSettings = apiSettings.Value;
//            _credentials = credentials.Value;
//        }

//        [AutomaticRetry(Attempts = 0)]
//        public async Task AutoAppointmentEligibilityJob()
//        {
//            const int practiceId = 0;
//            bool hasMoreData = true;

//            while (hasMoreData)
//            {
//                using var httpClient = new HttpClient
//                {
//                    Timeout = TimeSpan.FromMinutes(15)
//                };


//                string token = await LoginAndGetToken(httpClient);
//                httpClient.DefaultRequestHeaders.Authorization =
//                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);


//                var getModel = new
//                {
//                    TableName = "AppointmentEligibility"
//                };

//                var getResponse = await httpClient.PostAsync(
//                    $"{_apiSettings.BaseAddress}Eligibility/GetAppointmentEligibilityJob",
//                    CreateContent(getModel));

//                if (!getResponse.IsSuccessStatusCode)
//                    throw new Exception("GetAppointmentEligibilityJob failed");

//                var data =
//                    JsonConvert.DeserializeObject<List<AppointmentEligibilityDto>>(
//                        await getResponse.Content.ReadAsStringAsync());

//                if (data == null || data.Count == 0)
//                {
//                    hasMoreData = false;
//                    break;
//                }


//                var processModel = new
//                {
//                    TableName = "GETDATAAPPOINTMENTEDI270GENERATION",
//                    Data = data.Select(x => new
//                    {
//                        PatientAppointmentID = x.PatientAppointmentID.ToString(),
//                        PatientID = x.PatientID.ToString(),
//                        EligiblityDate = x.EligiblityDate.ToString(),
//                        InsuranceID = x.InsuranceID.ToString(),
//                        PatientInsuranceID = x.PatientInsuranceID.ToString(),
//                        ProviderID = x.ProviderID.ToString(),
//                        ServiceTypeCode = x.ServiceTypeCode.ToString(),
//                        UsePracticeNPI = x.UsePracticeNPI.ToString()
//                    }).ToList()
//                };

//                var processResponse = await httpClient.PostAsync(
//                    $"{_apiSettings.BaseAddress}Eligibility/AppointmentEligibilityJob",
//                    CreateContent(processModel));

//                if (!processResponse.IsSuccessStatusCode)
//                    throw new Exception("AppointmentEligibilityJob failed");
//            }
//        }


//        private async Task<string> LoginAndGetToken(HttpClient httpClient)
//        {
//            var loginModel = new
//            {
//                Email = _credentials.UserCredentials["AppointmentEligibilityJob"].Email,
//                Password = _credentials.UserCredentials["AppointmentEligibilityJob"].Password
//            };

//            var response = await httpClient.PostAsync(
//                $"{_apiSettings.BaseAddress}Login/Login",
//                CreateContent(loginModel));

//            if (!response.IsSuccessStatusCode)
//                throw new Exception("Login failed");

//            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
//            return json["token"]!.ToString();
//        }

//        private static StringContent CreateContent(object model)
//        {
//            return new StringContent(
//                JsonConvert.SerializeObject(model),
//                Encoding.UTF8,
//                "application/json");
//        }
//    }


//    public class AppointmentEligibilityDto
//    {
//        public string PatientAppointmentID { get; set; }
//        public string PatientID { get; set; }
//        public string EligiblityDate { get; set; }
//        public string InsuranceID { get; set; }
//        public string PatientInsuranceID { get; set; }
//        public string ProviderID { get; set; }
//        public string ServiceTypeCode { get; set; }
//        public string UsePracticeNPI { get; set; }
//    }
//}
