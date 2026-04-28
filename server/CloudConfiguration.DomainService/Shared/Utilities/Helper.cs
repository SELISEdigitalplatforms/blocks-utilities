using System.Security.Cryptography;

namespace CloudConfiguration.DomainService.Shared.Utilities
{
    public static class Helper
    {
        public static string GetMaskedCloudStorageRegionEndPoint(string endPoint)
        {
            if (string.IsNullOrEmpty(endPoint))
                return string.Empty;

            ReadOnlySpan<char> span = endPoint.AsSpan();
            if (span.Length <= 2)
                return new string('*', span.Length);

            return $"{span[0]}{new string('*', span.Length - 2)}{span[^1]}";
        }

        /// <summary>
        /// Encrypts the given plaintext using the provided Base64 secret key.
        /// </summary>
        public static string Encrypt(string plainText, string base64Key)
        {
            byte[] key = Convert.FromBase64String(base64Key);
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length); // prepend IV
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs))
                    {
                        sw.Write(plainText);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        /// <summary>
        /// Decrypts the given ciphertext using the provided Base64 secret key.
        /// </summary>
        public static bool TryDecrypt(string cipherText, string base64Key, out string result)
        {
            try
            {
                byte[] key = Convert.FromBase64String(base64Key);
                byte[] cipherBytes = Convert.FromBase64String(cipherText);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;

                    byte[] iv = new byte[aes.BlockSize / 8];
                    Array.Copy(cipherBytes, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var ms = new MemoryStream(cipherBytes, iv.Length, cipherBytes.Length - iv.Length))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs))
                    {
                        result = sr.ReadToEnd();
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                result = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Generates a random 256-bit (32-byte) AES key and returns it as a Base64 string.
        /// </summary>
        public static string GenerateAesKey()
        {
            byte[] key = new byte[32]; // 256-bit key
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }
            return Convert.ToBase64String(key);
        }
    }
}
