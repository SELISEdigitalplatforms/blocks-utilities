using Newtonsoft.Json;
using SixLabors.ImageSharp;
using System.Text.Json.Serialization;

namespace DomainService.OAuth
{
    public interface IExternalUserData
    {
        public string Platform { get; set; }
        public string DisplayName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string ExternalProviderUserId { get; set; }
        public string PhoneNumber { get; set; }
        public string ProfileImageUrl { get; set; }
        public List<string> Roles { get; set; }
        public List<string> Permissions { get; set; }
        public string UserPrincipalName { get; set; }
        public new string Department { get; set; }
        public new string EmployeeId { get; set; }
    }

    public class GoogleUserData : IExternalUserData
    {
        [JsonPropertyName("name")]
        public new string DisplayName { get; set; }

        [JsonPropertyName("given_name")]
        public new string FirstName { get; set; }

        [JsonPropertyName("family_name")]
        public new string LastName { get; set; }

        [JsonPropertyName("email")]
        public new string Email { get; set; }

        [JsonPropertyName("id")]
        public new string ExternalProviderUserId { get; set; }

        [JsonPropertyName("phone_number")]
        public new string PhoneNumber { get; set; }

        [JsonPropertyName("picture")]
        public new string ProfileImageUrl { get; set; }
        public string Platform { get; set; }
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];
        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get ; set ; }
        public new string Department { get; set; }
        public new string EmployeeId { get; set; }
    }

    public class MicrosoftUserData : IExternalUserData
    {
        [JsonPropertyName("displayName")]
        public new string DisplayName { get; set; }

        [JsonPropertyName("givenName")]
        public new string FirstName { get; set; }

        [JsonPropertyName("surname")]
        public new string LastName { get; set; }

        [JsonPropertyName("mail")]
        public new string Email { get; set; }

        [JsonPropertyName("id")]
        public new string ExternalProviderUserId { get; set; }

        [JsonPropertyName("mobilePhone")]
        public new string PhoneNumber { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public new string UserPrincipalName { get; set; }

        [JsonPropertyName("department")]
        public new string Department { get; set; }

        [JsonPropertyName("employeeId")]
        public new string EmployeeId { get; set; }

        public string Platform { get; set; }
        public string ProfileImageUrl { get; set; }

        [JsonPropertyName("roles")]
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];
    }

    public class BYOSsoUserData : IExternalUserData
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ExternalProviderUserId { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];

        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get ; set ; }
        public new string Department { get; set; }
        public new string EmployeeId { get; set; }
    }

    public class GithubUserData : IExternalUserData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        public string ExternalProviderUserId { get; set; }

        [JsonPropertyName("login")]
        public string Login { get; set; }

        [JsonPropertyName("name")]
        public string DisplayName { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("avatar_url")]
        public string ProfileImageUrl { get; set; }
        public string PhoneNumber { get; set; }
        public List<string> Permissions { get; set; } = [];
        public List<string> Roles { get; set; } = [];
        public string Platform { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get ; set ; }
        public new string Department { get; set; }
        public new string EmployeeId { get; set; }
    }

    public class GithubEmail
    {
        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }

        [JsonPropertyName("verified")]
        public bool Verified { get; set; }

        [JsonPropertyName("visibility")]
        public string Visibility { get; set; }
    }

    public class LinkedinUserData : IExternalUserData
    {
        [JsonPropertyName("sub")]
        public string ExternalProviderUserId { get; set; }

        [JsonPropertyName("given_name")]
        public string FirstName { get; set; }

        [JsonPropertyName("family_name")]
        public string LastName { get; set; }

        [JsonPropertyName("name")]
        public string DisplayName { get; set; }
        public string PhoneNumber { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }
        [JsonPropertyName("picture")]
        public string ProfileImageUrl { get; set; } 

        public List<string> Permissions { get; set; } = [];
        public List<string> Roles { get; set; } = [];
        public string Platform { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get ; set ; }
        public new string Department { get; set; }
        public new string EmployeeId { get; set; }
    }

    public class LinkedinUserInfo
    {
        [JsonPropertyName("sub")]
        public string Sub { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("given_name")]
        public string Given_Name { get; set; }
        [JsonPropertyName("family_name")]
        public string Family_Name { get; set; }
        [JsonPropertyName("picture")]
        public string Picture { get; set; }
        [JsonPropertyName("email")]
        public string Email { get; set; }
        [JsonPropertyName("email_verified")]
        public bool Email_Verified { get; set; }
        [JsonPropertyName("local")]
        public string Locale { get; set; }
    }

    public abstract class StandardSocialUserDataBase : IExternalUserData
    {
        [JsonPropertyName("id")]
        public string ExternalProviderUserId { get; set; }

        [JsonPropertyName("name")]
        public string DisplayName { get; set; }

        [JsonPropertyName("username")]
        public string UserName { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("profile_image_url")]
        public string ProfileImageUrl { get; set; }

        [JsonPropertyName("phone_number")]
        public string PhoneNumber { get; set; }

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string LastName { get; set; }

        public string Platform { get; set; }
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];

        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get; set; }
        public new string Department { get; set; }
        public new string EmployeeId { get; set; }
    }

    public class TwitterUserData : StandardSocialUserDataBase
    {
    }

    public class AppleUserData : StandardSocialUserDataBase
    {
    }
    public class AppleIdToken
    {

        [JsonProperty("email")]
        public string Email { get; set; }
        [JsonProperty("sub")]
        public string ExternalProviderUserId { get; set; }
    }
    public class FaceBookUserData : StandardSocialUserDataBase
    {
    }

}
