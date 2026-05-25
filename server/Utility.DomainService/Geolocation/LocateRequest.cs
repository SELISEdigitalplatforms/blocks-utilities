using Blocks.Genesis;

namespace Utility.DomainService.Geolocation
{
    public class LocateRequest : IProjectKey
    {
        /// <summary>
        /// Use custom ip lookup provider.
        /// </summary>
        public bool UseCustomProvider { get; set; } = false;
        
        /// <summary>
        /// Project key for tenant context.
        /// </summary>
        public string? ProjectKey { get; set; }
    }
}