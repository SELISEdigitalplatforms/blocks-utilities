namespace Utility.DomainService.Geolocation
{
    /// <summary>
    /// Why a geolocation request produced no result. Exists so the Api can pick a status code
    /// without reading the error message.
    /// </summary>
    public enum GeolocationFailureKind
    {
        /// <summary>No failure.</summary>
        None = 0,

        /// <summary>The request itself is wrong — no addresses, or too many of them.</summary>
        Validation = 1,

        /// <summary>
        /// The request was fine and nothing could be located.
        /// </summary>
        /// <remarks>
        /// This covers both a genuinely unlocatable address — a private range, an allocation the
        /// provider has no record of — and a provider that would not answer, because the repository
        /// collapses the two: a failed call and an empty answer both resolve to nothing. The
        /// provider's status code is logged, so the distinction is recoverable from the logs rather
        /// than from the response. Splitting it into a separate Unavailable kind means teaching the
        /// repository to report which of the two it hit.
        /// </remarks>
        NotFound = 2
    }
}
