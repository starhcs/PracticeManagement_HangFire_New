namespace HangfireNew.VMModels
{
    public class BaseClass
    {
        public long? UserID { get; set; }
        public string UserName { get; set; }
        public long? PracticeID { get; set; }
        public string PracticeName { get; set; }

        public string PracticeType { get; set; }

        public string TimeZone { get; set; }


        public string TableName { get; set; }
    }
}
