namespace Mail.DomainService.Dtos
{
    public class CheckMailDeliveryStatusCommand
    {
        public string ItemId { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public DateTime NotBeforeUtc { get; set; }
        public int Attempt { get; set; } = 1;
    }
}
