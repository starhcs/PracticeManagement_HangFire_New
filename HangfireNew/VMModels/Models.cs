using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HangfireNew.VMModels
{
    public class UserCredentials
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class CredentialsStore
    {
        public Dictionary<string, UserCredentials> UserCredentials { get; set; }
    }
    public class ApiSettings
    {
        public string BaseAddress { get; set; }
        public string LoginEndpoint { get; set; }
        public string WriteLogsEndpoint { get; set; }
    }

    public class SubmitterReceiverIds
    {
        public int SubmitterReceiverID { get; set; }
        public int PracticeID { get; set; }
    }


    public class Practices
    {
        public int PracticeID { get; set; }
        public string PracticeName { get; set; }
    }

    public class PostEra
    {
        public long HeaderID { get; set; }
        public string PaymentType { get; set; }

        public string TRNCHECKNUMBER { get; set; }
    }

    public class FileData
    {
        public int FileID { get; set; }
        public int TemplateID { get; set; }
        public string? PracticeID { get; set; }
        public string? FilePath { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public string? DeletedNotes { get; set; }
        public bool Deleted { get; set; }
        public string? Status { get; set; }
    }

    public class TemplateAndFileIds
    {
        public int TemplateID { get; set; }
        public int FileID { get; set; }
    }
}
