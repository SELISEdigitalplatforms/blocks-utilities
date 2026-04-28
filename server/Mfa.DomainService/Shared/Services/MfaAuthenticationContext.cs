using System.Security.Cryptography;
using System.Text.Json;

namespace Mfa.DomainService.Services
{
    public class MfaAuthenticationContext
    {
        public string UserId { get; set; }

        public string MfaId { get; set; }

        public string MfaCode { get; set; }

        public static MfaAuthenticationContext Create(string mfaId, string userId)
        {
            return new MfaAuthenticationContext
            {
                UserId = userId,
                MfaId = mfaId,
                MfaCode = GenerateRandomAccessCode()
            };
        }

        private static string GenerateRandomAccessCode()
        {
            return GenerateSecureRandomNumber();
        }

        public static string GenerateSecureRandomNumber()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[2];
            rng.GetBytes(bytes);
            int number = BitConverter.ToUInt16(bytes, 0) % 88889 + 11111;
            return number.ToString();
        }

        public string Sterilize()
        {
            return JsonSerializer.Serialize(this);
        }

        public static MfaAuthenticationContext Deserialize(string json)
        {
            return JsonSerializer.Deserialize<MfaAuthenticationContext>(json);
        }
    }
}
