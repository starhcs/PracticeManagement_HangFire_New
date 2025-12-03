using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using HangfireNew.VMModels;
using Azure;
using Hangfire;
using System.Data;
using System.Data.SqlClient;

namespace HangfireNew.Services
{
    public interface IAppointmentReminder
    {
        // over here will come all the methods that are present in the class
        Task AppointmentReminderJob();

    }
    public class AppointmentReminder : IAppointmentReminder
    {
        private readonly ApiSettings _apiSettings;
        private readonly Dictionary<string, UserCredentials> _userCredentials;
        private readonly HttpClient _httpClient;
        private readonly ConnectionStrings _connectionstrings;

        public AppointmentReminder(IOptions<ApiSettings> apiSettings, IOptions<CredentialsStore> credentialsStore, IOptions<ConnectionStrings> connectionstrings)
        {
            _apiSettings = apiSettings.Value;
            _userCredentials = credentialsStore.Value.UserCredentials;
            _httpClient = new HttpClient();
            _connectionstrings = connectionstrings.Value;
        }

        [Hangfire.AutomaticRetry(Attempts = 0)]
        public async Task AppointmentReminderJob()
        {
            // the timestamp is being used to freeze the time so a single time will be used 
            //DateTime jobTimestamp = Convert.ToDateTime("2025-12-04 02:24:37.323"); //DateTime.Now; ////// Current Timestamp 2025-11-28 07:05:00.000

            DateTime jobTimestamp = DateTime.Now;

            HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var loginModel = new
            {
                Email = _userCredentials["AppointmentReminderJob"].Email,
                Password = _userCredentials["AppointmentReminderJob"].Password
            };
            string ApiAddress = $"{_apiSettings.BaseAddress}";
            string loginURL = $"{ApiAddress}Login/Login";
            string WriteLogsURL = $"{ApiAddress}HangfireJobs/WriteAppointmentReminderJobLog";
            string payloadLogin = JsonConvert.SerializeObject(loginModel);

            var contentLogin = new StringContent(payloadLogin, Encoding.UTF8, "application/json");


            HttpResponseMessage responseLogin = await httpClient.PostAsync(loginURL, contentLogin);
            if (responseLogin.IsSuccessStatusCode)
            {
                var job_started_model = new
                {
                    TableName = "APPOINTMENTREMINDERJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", "Appointment Reminder Job Started" },
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
                    Value = "AppointmentReminderJobs"
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
                        TableName = "APPOINTMENTREMINDERJOBLOGS",
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

                        for (int i = 0; i < data.Count; i++) ///////// Loop through Practices
                        {
                            //if (data[i].PracticeName != "ABC TEST PRACTICE") //// this is just for testing ,,, making debug easier
                            //{
                            //    continue;
                            //}
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

                                    var Email_appointments_model = new
                                    {
                                        PracticeID = data[i].PracticeID,
                                        TableName = "APPOINTMENTREMINDERJOBLOGS",
                                        Data = new Dictionary<string, string>
                                        {
                                            { "Message", $"Appointment Reminder Email Started for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                            { "ExceptionMsg", "" }
                                        }
                                    };
                                    string payloadEmailAppointments = JsonConvert.SerializeObject(Email_appointments_model);
                                    var contentEmailAppointments = new StringContent(payloadEmailAppointments, Encoding.UTF8, "application/json");
                                    HttpResponseMessage responseMarkNoshowAppointments = await httpClient.PostAsync(WriteLogsURL, contentEmailAppointments);

                                    //////////////////////////////////////////////// FROM HERE OUR JOB WILL START FOR EACH RECORD ///////////////////////////////////////////////////////



                                    string connString = _connectionstrings.ProjectXLive;
                                    DataSet ds = new DataSet();

                                    using (SqlConnection connection = new SqlConnection(connString))
                                    {

                                        using (SqlCommand cmd = new SqlCommand("sp_EmailAppointmentReminder", connection))
                                        {

                                            cmd.CommandType = CommandType.StoredProcedure;


                                            cmd.Parameters.AddWithValue("@ActionType", 0);
                                            cmd.Parameters.AddWithValue("@TimeStamp", jobTimestamp);
                                            cmd.Parameters.AddWithValue("@PracticeID", data[i].PracticeID);



                                            SqlDataAdapter adapter = new SqlDataAdapter(cmd);


                                            adapter.Fill(ds);
                                        }
                                    } // connection.Dispose() happens automatically

                                    HttpResponseMessage response1 = new HttpResponseMessage();
                                    HttpResponseMessage response2 = new HttpResponseMessage();

                                    if (ds != null && ds.Tables.Count == 2)
                                    {
                                        DataTable firstReminderList = ds.Tables[0];
                                        DataTable secondReminderList = ds.Tables[1];



                                        foreach (DataRow row in firstReminderList.Rows)  ////first reminder list
                                        {
                                            // The DataRow object is 'row'

                                            var APModel = new
                                            {
                                                TableName = "AppointmentReminder",
                                                ID = row["ID"].ToString(),
                                                Data = new Dictionary<string, string>
                                            {
                                                { "AppointmentDate", row["AppointmentDate"].ToString() },
                                                { "Email", row["Email"].ToString() },
                                                { "FirstName", row["FirstName"].ToString() },
                                                { "LastName", row["LastName"].ToString() },
                                                { "AddressLine1", row["AddressLine1"].ToString() },
                                                { "ProviderName", row["ProviderName"].ToString() },
                                                { "PracticeName", row["PracticeName"].ToString() },
                                                { "Contact", row["ContactPhoneNumber"].ToString() },
                                                { "Email1", row["EmailAddress"].ToString() },
                                                { "JobReminder1", row["JobReminder1"].ToString() }
                                                



        
                                                // Note: appointmentTime and currentJobTime are not used in the Dictionary,
                                                // so they are correctly removed along with the local variables.
                                            }
                                            };
                                            string EmailAppointmentsJson = JsonConvert.SerializeObject(APModel);
                                            var AppointmentReminderContent = new StringContent(EmailAppointmentsJson, Encoding.UTF8, "application/json");

                                            string AppointmentReminderUrl = $"{ApiAddress}HangfireJobs/AppointmentReminder";

                                            response1 = await httpClient.PostAsync(AppointmentReminderUrl, AppointmentReminderContent);

                                        }

                                        foreach (DataRow row in secondReminderList.Rows) //// second reminder list
                                        {
                                            var APModel = new
                                            {
                                                TableName = "AppointmentReminder",
                                                ID = row["ID"].ToString(),
                                                Data = new Dictionary<string, string>
                                            {
                                                { "AppointmentDate", row["AppointmentDate"].ToString() },
                                                { "Email", row["Email"].ToString() },
                                                { "FirstName", row["FirstName"].ToString() },
                                                { "LastName", row["LastName"].ToString() },
                                                { "AddressLine1", row["AddressLine1"].ToString() },
                                                { "ProviderName", row["ProviderName"].ToString() },
                                                { "PracticeName", row["PracticeName"].ToString() },
                                                { "Contact", row["ContactPhoneNumber"].ToString() },
                                                { "Email1", row["EmailAddress"].ToString() },
                                                { "JobReminder2", row["JobReminder2"].ToString() }


        
                                                // Note: appointmentTime and currentJobTime are not used in the Dictionary,
                                                // so they are correctly removed along with the local variables.
                                            }
                                            };
                                            string EmailAppointmentsJson = JsonConvert.SerializeObject(APModel);
                                            var AppointmentReminderContent = new StringContent(EmailAppointmentsJson, Encoding.UTF8, "application/json");

                                            string AppointmentReminderUrl = $"{ApiAddress}HangfireJobs/AppointmentReminder";

                                            response2 = await httpClient.PostAsync(AppointmentReminderUrl, AppointmentReminderContent);

                                        }
                                    }


                                    //get table 0 and table 1 from dataset and then loop through these tables and run the Email method for each record

                                    //var APModel = new
                                    //{
                                    //    TableName = "AppointmentReminder",
                                    //    Data = new Dictionary<string, string>
                                    //   {
                                    //   { "AppointmentDate", "2024-09-17 00:00:00.000" },
                                    //   { "Email", $"ashhadraven@gmail.com" },
                                    //   { "PatientID", "Walter White" }
                                    //  }
                                    //};
                                    //string EmailAppointmentsJson = JsonConvert.SerializeObject(APModel);
                                    //var AppointmentReminderContent = new StringContent(EmailAppointmentsJson, Encoding.UTF8, "application/json");

                                    //string AppointmentReminderUrl = $"{ApiAddress}HangfireJobs/AppointmentReminder";

                                    //HttpResponseMessage response1 = await httpClient.PostAsync(AppointmentReminderUrl, AppointmentReminderContent);

                                    //////////////////////////////////////////////////////////////////// JOBS END HERE ///////////////////////////////////////////////////////////////

                                    if (response1.IsSuccessStatusCode && response2.IsSuccessStatusCode)
                                    {
                                        var Email_appointments_model_success = new
                                        {
                                            PracticeID = data[i].PracticeID,
                                            TableName = "APPOINTMENTREMINDERJOBLOGS",
                                            Data = new Dictionary<string, string>
                                            {
                                                { "Message", $"Appointments Email Reminder Completed for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                                { "ExceptionMsg", "" }
                                            }
                                        };
                                        string EmailAppointmentsSuccess = JsonConvert.SerializeObject(Email_appointments_model_success);
                                        var contentAppointmentsSuccess = new StringContent(EmailAppointmentsSuccess, Encoding.UTF8, "application/json");
                                        HttpResponseMessage responseMarkNoshowAppointmentsSuccess = await httpClient.PostAsync(WriteLogsURL, contentAppointmentsSuccess);
                                    }
                                    else
                                    {
                                        var Email_appointments_model_failed = new
                                        {
                                            PracticeID = data[i].PracticeID,
                                            TableName = "APPOINTMENTREMINDERJOBLOGS",
                                            Data = new Dictionary<string, string>
                                            {
                                                { "Message", $"Appointments Email Reminder Failed for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                                { "ExceptionMsg", $"{response1.ReasonPhrase}" }
                                            }
                                        };
                                        string payloadEmailAppointmentsFailed = JsonConvert.SerializeObject(Email_appointments_model_failed);
                                        var contentMarkNoshowAppointmentsFailed = new StringContent(payloadEmailAppointmentsFailed, Encoding.UTF8, "application/json");
                                        HttpResponseMessage responseMarkNoshowAppointmentsFailed = await httpClient.PostAsync(WriteLogsURL, contentMarkNoshowAppointmentsFailed);
                                    }
                                }
                                else
                                {
                                    var switching_practice_failed = new
                                    {
                                        PracticeID = data[i].PracticeID,
                                        TableName = "APPOINTMENTREMINDERJOBLOGS",
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

                                //var Email_appointments_finshed_for_practice_model = new
                                //{
                                //    TableName = "APPOINTMENTREMINDERJOBLOGS",
                                //    Data = new Dictionary<string, string>
                                //                    {
                                //                        { "Message", $"Appointments Email Reminder Finished for {data[i].PracticeName} , ID = {data[i].PracticeID}" },
                                //                        { "ExceptionMsg", "" }
                                //                    }
                                //};
                                //string payloadFinishedForPractice = JsonConvert.SerializeObject(Email_appointments_finshed_for_practice_model);
                                //var contentFinishedForPractice = new StringContent(payloadFinishedForPractice, Encoding.UTF8, "application/json");
                                //HttpResponseMessage responseFinishedForPractice = await httpClient.PostAsync(WriteLogsURL, contentFinishedForPractice);

                            }
                        }

                        var job_finished_model = new
                        {
                            PracticeID = 0,
                            TableName = "APPOINTMENTREMINDERJOBLOGS",
                            Data = new Dictionary<string, string>
                            {
                                { "Message", $"Appointments Email Reminder Job Ended" },
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
                        TableName = "APPOINTMENTREMINDERJOBLOGS",
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
                    TableName = "APPOINTMENTREMINDERJOBLOGS",
                    Data = new Dictionary<string, string>
                    {
                            { "Message", $"AppointmentsReminder Job could not be started " },
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
