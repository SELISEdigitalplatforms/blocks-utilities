using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class GetMailBoxMails 
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Status { get; set; }
        public string? SearchText { get; set; }
        public DateRange? SendDateRange { get; set; }
        public bool? IsInbound { get; set; }
    }

    public class DateRange
    {
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
    }
}