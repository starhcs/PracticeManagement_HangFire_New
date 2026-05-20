namespace HangfireNew.VMModels
{
    public class SearchModel : BaseClass
    {
        public long? ID { get; set; }
        public long? ParentID { get; set; }
        public string Value { get; set; }
        public string SearchParam { get; set; }
        public int? PerPage { get; set; }
        public int? PageNo { get; set; }
        public long? SelectedUserID { get; set; }
        public string HeaderID { get; set; }

        public long? PatientID { get; set; }

        public int? ActionType { get; set; }
        public Dictionary<string, string> SearchCriteria { get; set; }

    }
}
