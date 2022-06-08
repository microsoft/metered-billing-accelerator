namespace MeteredPage.ViewModels
{
    public class ErrorViewModel
    {
        public string RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public string Description { get; set; }

        public string ExceptionMessage {  get; set; }
    }
}
