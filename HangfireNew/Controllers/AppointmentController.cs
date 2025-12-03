//using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.Mvc;
//using HangfireNew.VMModels;
//using Hangfire;
//using System.Data;
//using System.Globalization;
//using Microsoft.Extensions.Options;
//using Newtonsoft.Json.Linq;
//using Newtonsoft.Json;
//using System.Text;
//using HangfireNew.VMModels;
//using Azure;
//using Hangfire;
//using Hangfire.Annotations;
//using System.Net.Http;
//using System.Data;
//using System.Data.SqlClient;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Http;
//using System;



//namespace HangfireNew.Controllers
//{
//    [Route("[controller]")]
//    [ApiController]

//    public class AppointmentController : ControllerBase
//    {
//        private readonly ConnectionStrings _connectionstrings;
//        private readonly ApiSettings _apiSettings;
//        private readonly Dictionary<string, UserCredentials> _userCredentials;
//        private readonly HttpClient _httpClient;
//        public AppointmentController(IOptions<ConnectionStrings> connectionstrings, IOptions<ApiSettings> apiSettings, IOptions<CredentialsStore> credentialsStore) 
//        {
//            _connectionstrings = connectionstrings.Value;
//            _apiSettings = apiSettings.Value;
//            _userCredentials = credentialsStore.Value.UserCredentials;
//            _httpClient = new HttpClient();
//        }

//        [Route("AppointmentEmail")]
//        [HttpPost]
//        public async Task AppointmentEmail(AppointmentEmail appointment)
//        {


//            //HttpClient httpClient = new();
//            //httpClient.Timeout = TimeSpan.FromMinutes(5);
//            //string ApiAddress = $"{_apiSettings.BaseAddress}";
//            //string loginURL = $"{ApiAddress}Login/Login";
//            //string WriteLogsURL = $"{ApiAddress}HangfireJobs/WriteMarkNoShowAppointmentsJobLog";

//            //var loginModel = new
//            //{
//            //    Email = _userCredentials["MarkNoShowAppointmentsJob"].Email,
//            //    Password = _userCredentials["MarkNoShowAppointmentsJob"].Password
//            //};
//            //string payloadLogin = JsonConvert.SerializeObject(loginModel);

//            //var contentLogin = new StringContent(payloadLogin, Encoding.UTF8, "application/json");


//            //HttpResponseMessage responseLogin = await httpClient.PostAsync(loginURL, contentLogin);

//            //string responseContent = await responseLogin.Content.ReadAsStringAsync();
//            //var jsonObject = JObject.Parse(responseContent);
//            //string token = jsonObject["token"].ToString();

//            //httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
//            //string appointmentReminderUrl = $"{ApiAddress}HangfireJobs/AppointmentReminder";
//            //var APModel = new
//            //{
//            //    TableName = "AppointmentReminder",
//            //    Data = new Dictionary<string, string>
//            //                {
//            //                    { "AppointmentDate", $"" },
//            //                    { "Email", "" },
//            //                    { "PatientID", "" }
//            //                }
//            //};
//            //string PracticesPayloadJson = JsonConvert.SerializeObject(APModel);

//            //var contentPractices = new StringContent(PracticesPayloadJson, Encoding.UTF8, "application/json");

//            //HttpResponseMessage responseEmail = await httpClient.PostAsync(appointmentReminderUrl, contentPractices);

//            string connString = _connectionstrings.ProjectXLive;
//            DataSet ds = new DataSet();

//            using (SqlConnection connection = new SqlConnection(connString))
//            {

//                using (SqlCommand cmd = new SqlCommand("sp_AppointmentReminder", connection))
//                {

//                    cmd.CommandType = CommandType.StoredProcedure;


//                    cmd.Parameters.AddWithValue("@ActionType", 0);


//                    SqlDataAdapter adapter = new SqlDataAdapter(cmd);


//                    adapter.Fill(ds);
//                }
//            } // connection.Dispose() happens automatically



//            ///////////////////////////////LOGIC FOR MAKING APPOINTMENTS FROM TABLE ZERO/////////////////////////////////////////////////////////////////////////////////

//            DataTable zerotable = new DataTable();
//            zerotable = ds.Tables[0];



//            foreach (DataRow row in zerotable.Rows)
//            {
//                var ID = row["PatientAppointmentID"];
//                var reminderDate = row["AppointmentDate"];
//                var reminderTime = row["AppointmentStart"];
//                string jobid = "";
//                string email = row["Email"].ToString();
//                string PracticeID = row["PracticeID"].ToString();

//                //DateTime onlyDate = Convert.ToDateTime(reminderDate).Date;
//                string onlyDate = Convert.ToDateTime(reminderDate).Date.ToString("yyyy-MM-dd");

//                reminderTime = onlyDate + " " + reminderTime;

//                if (DateTime.TryParse(reminderTime.ToString(), out DateTime targetDate))
//                {

//                    DateTime current = DateTime.Now;

//                    //TimeSpan remainingTime = (targetDate.AddDays(-1)) - DateTime.Now;
//                    int minutesBefore = 30; // over here will come the selected time from the front end 
//                    TimeSpan remainingTime = (targetDate.AddMinutes(-minutesBefore)) - DateTime.Now;


//                    if (remainingTime.TotalSeconds > 0)
//                    {
//                        //jobid = BackgroundJob.Schedule(
//                        //    () => Console.WriteLine("This job will run 24 hrs prior to the appointment"), remainingTime
//                        //);

//                        jobid = BackgroundJob.Schedule(
//                                () => AppointmentEmailReminder(email, ID.ToString(), reminderTime.ToString()),
//                                remainingTime
//                            );


//                        using (SqlConnection connection = new SqlConnection(connString))
//                        {
//                            using (SqlCommand cmd = new SqlCommand("sp_AppointmentReminder", connection))
//                            {
//                                cmd.CommandType = CommandType.StoredProcedure;

//                                cmd.Parameters.AddWithValue("@ActionType", 1);
//                                cmd.Parameters.AddWithValue("@patientappointmentid", ID);
//                                cmd.Parameters.AddWithValue("@hangfirejobid", jobid);
//                                cmd.Parameters.AddWithValue("@remindertime", reminderTime);


//                                connection.Open();
//                                cmd.ExecuteNonQuery(); // runs the stored procedure
//                            }
//                        }

//                    }
//                    else
//                    {
//                        Console.WriteLine("Target date is in the past. Cannot schedule job.");
                        
//                    }

//                    //jobid and ID which we will map to the tracker table



//                }
//                else
//                {
//                    Console.WriteLine("Invalid date format: ");
//                }
//                // success
//            }





//            // WHERE THE DATE IS CAHNGED // NOW THE LOGIC FOR MAKING APPOINTMENTS FROM TABLE ONE //  ////////////////////////////////////////////////////////////

//            DataTable onetable = new DataTable();
//            onetable = ds.Tables[1];

//            foreach (DataRow rowd in onetable.Rows)
//            {
               
//                var jobid = rowd["HangfireJobId"].ToString();
//                var newTime = rowd["AppointmentDate"].ToString();
//                var patientAppointmentID = rowd["PatientAppointmentID"].ToString();
//                string jobidnew = "";
//                string email = rowd["Email"].ToString();

//                BackgroundJob.Delete(jobid);

//                //DateTime targetDate = DateTime.ParseExact(
//                //    newTime,
//                //    "MM/dd/yyyy hh:mm:ss tt",
//                //    CultureInfo.InvariantCulture
//                //);

//                if (DateTime.TryParse(newTime, out DateTime targetDate))
//                {
//                    // success

//                    //TimeSpan remainingTime = (targetDate.AddDays(-1)) - DateTime.Now;

//                    int minutesBefore = 30; // over here will come the selected time from the front end 
//                    TimeSpan remainingTime = (targetDate.AddMinutes(-minutesBefore)) - DateTime.Now;

//                    if (remainingTime.TotalSeconds > 0)
//                    {
//                        //jobidnew = BackgroundJob.Schedule(
//                        //    () => Console.WriteLine("this will update the job to the new time"), remainingTime
//                        //);
//                        jobid = BackgroundJob.Schedule(
//                                        () => AppointmentEmailReminder(email, patientAppointmentID.ToString(), newTime),
//                                        remainingTime
//                                    );

//                    }
//                    else
//                    {
//                        Console.WriteLine("Target date is in the past. Cannot schedule job.");
//                    }

//                    using (SqlConnection connection = new SqlConnection(connString))
//                    {
//                        using (SqlCommand cmd = new SqlCommand("sp_AppointmentReminder", connection))
//                        {
//                            cmd.CommandType = CommandType.StoredProcedure;

//                            cmd.Parameters.AddWithValue("@ActionType", 2);
//                            cmd.Parameters.AddWithValue("@patientappointmentid", patientAppointmentID);
//                            cmd.Parameters.AddWithValue("@hangfirejobid", jobidnew);
//                            cmd.Parameters.AddWithValue("@remindertime", newTime);


//                            connection.Open();
//                            cmd.ExecuteNonQuery(); // runs the stored procedure
//                        }
//                    }
//                }
//                else
//                {
//                    Console.WriteLine("Invalid date format: ");
//                }




//            }


//            return;
//        }



//        [Route("AppointmentEmailReminder")]
//        [HttpPost]
//        public async Task AppointmentEmailReminder(string email, string patientid, string appointmentDate) 
//        {
//            string ApiAddress = $"{_apiSettings.BaseAddress}";
//            string loginURL = $"{ApiAddress}Login/Login";
//            string WriteLogsURL = $"{ApiAddress}HangfireJobs/WriteMarkNoShowAppointmentsJobLog";

//            HttpClient httpClient = new();

//            var loginModel = new
//            {
//                Email = _userCredentials["AppointmentReminderJob"].Email,
//                Password = _userCredentials["AppointmentReminderJob"].Password
//            };
//             string payloadLogin = JsonConvert.SerializeObject(loginModel);

//            var contentLogin = new StringContent(payloadLogin, Encoding.UTF8, "application/json");


//            HttpResponseMessage responseLogin = await httpClient.PostAsync(loginURL, contentLogin);

//            string responseContent = await responseLogin.Content.ReadAsStringAsync();
//            var jsonObject = JObject.Parse(responseContent);
//            string token = jsonObject["token"].ToString();

//            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            
//            //string appointmentReminderUrl = $"{ApiAddress}HangfireJobs/AppointmentReminder";
//            string appointmentReminderUrl = "http://localhost:16655/HangfireJobs/AppointmentReminder";

//            var APModel = new
//            {
//                TableName = "AppointmentReminder", // 
//                Data = new Dictionary<string, string>
//                            {
//                                { "AppointmentDate", appointmentDate },
//                                { "Email", $"{email}" },
//                                { "PatientID", patientid }
//                            }
//            };
//            string APPayloadJson = JsonConvert.SerializeObject(APModel);

//            var APEmail = new StringContent(APPayloadJson, Encoding.UTF8, "application/json");

//            HttpResponseMessage responseEmail = await httpClient.PostAsync(appointmentReminderUrl, APEmail);


//            return;
//        }
//    }
//}
