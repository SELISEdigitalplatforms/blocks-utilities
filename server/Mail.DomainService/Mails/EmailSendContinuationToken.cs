using System.Text;
using System.Text.Json;

namespace Mail.DomainService.Mails
{
    public static class EmailSendContinuationToken
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        public static string Encode(DateTime createdAtUtc, string itemId)
        {
            var payload = new TokenPayload
            {
                CreatedAtUtc = createdAtUtc,
                ItemId = itemId
            };

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, SerializerOptions)));
        }

        public static bool TryDecode(string? token, out DateTime createdAtUtc, out string itemId)
        {
            createdAtUtc = default;
            itemId = string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var payload = JsonSerializer.Deserialize<TokenPayload>(json, SerializerOptions);
                if (payload == null || string.IsNullOrWhiteSpace(payload.ItemId))
                {
                    return false;
                }

                createdAtUtc = payload.CreatedAtUtc;
                itemId = payload.ItemId;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class TokenPayload
        {
            public DateTime CreatedAtUtc { get; set; }
            public string ItemId { get; set; } = string.Empty;
        }
    }
}
