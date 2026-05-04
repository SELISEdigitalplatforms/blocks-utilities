namespace Utility.DomainService.Geolocation
{
    public class GeoRestrictionResponse
    {
        public bool Restricted { get; private set; }
        public KeyValuePair<string, string> Reason { get; private set; }

        public static GeoRestrictionResponse CreateNotRestricted()
        {
            return new GeoRestrictionResponse
            {
                Restricted = false
            };
        }

        public static GeoRestrictionResponse CreateRestricted(string error, string description)
        {
            return new GeoRestrictionResponse
            {
                Restricted = true,
                Reason = new KeyValuePair<string, string>(error, description)
            };
        }
    }
}