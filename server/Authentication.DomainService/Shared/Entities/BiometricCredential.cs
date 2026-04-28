using Blocks.Genesis;

namespace DomainService.Entities
{
    public class BiometricCredential : BaseEntity
    {
        public string UserId { get; set; }
        public string PhysicalAddress { get; set; }
        public bool IsActive { get; set; }
        public string BiometricId { get; set; }
        public string BiometriKey { get; set; }
        public BiometricType BiometricType { get; set; }
        public string DeviceInformation { get; set; }
    }

    public enum BiometricType
    {
        Fingerprint,
        Face,
        Iris,
        Retina
    }
}
