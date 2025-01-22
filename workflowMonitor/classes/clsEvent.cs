namespace workflowMonitorService
{
    public class clsEvent
    {
        public int eventID { get; set; }
        public int actionID { get; set; }
        public string? applicationFilename { get; set; }
        public string? applicationDefaultParameter { get; set; }
        public string? eventParameters { get; set; }
        public bool actionLogOutput { get; set; }
        public string? applicationType { get; set; }
    }
}
