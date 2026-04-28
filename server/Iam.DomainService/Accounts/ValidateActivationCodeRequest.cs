using System;
using Blocks.Genesis;

namespace Iam.DomainService.Accounts;

public class ValidateActivationCodeRequest : IProjectKey
{
    public string ActivationCode { get; set; } = string.Empty;
    public string? ProjectKey { get; set; }

}
