using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Blocks.Genesis;
using DnsClient;
using DomainService.Projects;
using DomainService.Shared.Dtos;
using DomainService.Shared.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Renci.SshNet;

namespace DomainService.Shared
{
    public class DomainManagementService : IDomainManagementService
    {
        private readonly ILogger<DomainManagementService> _logger;
        private readonly IBlocksSecret _blocksSecret;
        private readonly IProjectRepository _projectRepository;
        private readonly ITenants _tenants;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        public DomainManagementService(ILogger<DomainManagementService> logger,
                                       IBlocksSecret blocksSecret,
                                       IProjectRepository projectRepository,
                                       HttpClient httpClient,
                                       ITenants tenants,
                                       IConfiguration configuration)
        {
            _logger = logger;
            _blocksSecret = blocksSecret;
            _projectRepository = projectRepository;
            _tenants = tenants;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<BaseResponse> ConfigureDomainAsync(ConfigureDomainRequest request)
        {
            _logger.LogInformation("Processing request {RequestId} for domain {Domain}", request.ProjectKey, request.CookieDomain);
            var cookieDomain = request.CookieDomain.Replace("https://", "");

            var (domain, blocksApiDomain) = ExtractDomainParts(cookieDomain);

            var (verifySuccess, verifyMessage) = await VerifyDomainAsync(domain);

            if (!verifySuccess)
            {
                _logger.LogWarning($"Domain verification failed for domain {domain}: {verifyMessage}");
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "domain_verification_failed", $"{domain} - {verifyMessage}"}}};
            }

            var (verifyBlocksDomainSuccess, verifyBlocksDomainMessage) = await VerifyDomainAsync(blocksApiDomain);

            if (!verifyBlocksDomainSuccess)
            {
                _logger.LogWarning($"Domain verification failed for domain {blocksApiDomain}: {verifyMessage}");
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "domain_verification_failed", $"{blocksApiDomain} - {verifyBlocksDomainMessage}"}}};
            }

            var (nginxSuccess, nginxMessage) = await UpdateNginxConfigAndSetupSslRemoteAsync(domain, blocksApiDomain);

            if (!nginxSuccess)
            {
                _logger.LogError("Nginx configuration failed: {Message}", nginxMessage);
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "nginx_configuration_failed", nginxMessage } } };
            }

            _logger.LogInformation("Successfully configured domain {Domain}", request.CookieDomain);
            await UpdateDomainValidationStatusAsync(request.ProjectKey, true);

            return new BaseResponse { IsSuccess = true };
        }

        private async Task UpdateDomainValidationStatusAsync(string tenantId, bool status)
        {
            var project = _tenants.GetTenantByID(tenantId);

            if (project is not null)
            {
                project.IsDomainVerified = status;
                await _projectRepository.UpdateProjectAsync(project);
                await _tenants.UpdateTenantVersionAsync(new TenantCacheUpdateMessage
                {
                    Action = "upsert",
                    TenantId = project.TenantId,
                    Tenant = project
                });
            }
        }


        private async Task<(bool, string)> VerifyDomainAsync(string domain)
        {
            _logger.LogInformation("Verifying domain {Domain}", domain);

            try
            {
                var dnsResolver = new LookupClient();
                var result = await dnsResolver.QueryAsync(domain, QueryType.A);

                if (!result.Answers.Any())
                {
                    _logger.LogWarning("No CNAME records found for {Domain}", domain);
                    return (false, "No A records found.");
                }

                _logger.LogInformation("Domain {Domain} verified successfully", domain);
                return (true, "Domain verified.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Domain verification failed for {Domain}", domain);
                return (false, $"Domain verification error: {ex.Message}");
            }
        }
        

        private async Task<(bool Success, string Message)> CheckPingBlocksApi(string domain)
        {
            var url = $"https://{domain}/identifier/v1/ping";
            _logger.LogInformation("Checking blocksapi ping: {Url}", url);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                // If you want to ignore SSL cert errors like verify=False, you'd need a custom HttpClientHandler (not recommended in prod)

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                _logger.LogInformation("Blocksapi ping response status code: {StatusCode}", (int)response.StatusCode);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return (true, "Ping successful.");
                }
                else
                {
                    return (false, $"Ping failed with status code {(int)response.StatusCode}.");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Blocksapi ping request failed.");
                return (false, $"Ping request exception: {ex.Message}");
            }
            catch (TaskCanceledException ex) // Timeout, etc.
            {
                _logger.LogWarning(ex, "Blocksapi ping request timed out.");
                return (false, "Ping request timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during blocksapi ping.");
                return (false, $"Unexpected error: {ex.Message}");
            }
        }

        private async Task<(bool, string)> UpdateNginxConfigAndSetupSslRemoteAsync(string domain, string blocksApiDomain)
        {
            var host = _blocksSecret.SshHost;
            var username = _blocksSecret.SshUsername;
            var password = _blocksSecret.SshPassword;

            List<string> commands = new List<string>();
            List<string> allowedDomains = new List<string>();
            bool result;
            string response;

            _logger.LogInformation("Connecting to SSH server {Host}...", host);

            using var sshClient = new SshClient(host, username, password);

            try
            {
                sshClient.Connect();
                if (!sshClient.IsConnected)
                {
                    _logger.LogError("Failed to connect to SSH server {Host}", host);
                    return (false, "SSH connection failed.");
                }

                _logger.LogInformation("SSH connected to {Host}", host);



                //var domainHttpsChecker = await GetHttpsCertificateInfoAsync(domain);
                //if (domainHttpsChecker is not null && !domainHttpsChecker.HasValidCertificate)
                //{
                    
                //}

                //var blocksApiDomainHttpsChecker = await GetHttpsCertificateInfoAsync(blocksApiDomain);
                //if (blocksApiDomainHttpsChecker is not null && !blocksApiDomainHttpsChecker.HasValidCertificate && !checkPingSuccess)
                //{

                //}

                var (checkPingSuccess, checkPingMessage) = await CheckPingBlocksApi(blocksApiDomain);
                commands = UpdateNginxConfigCommands(domain, IdentifierConstants.RemoteFeTemplate, "fe-domain");

                (result, response) = await ExecuteRemoteCommands(sshClient, commands);
                allowedDomains.Add(domain);
                if (!result)
                {
                    _logger.LogError($"Failed to update nginx config for domain {domain}.");
                    await ExecuteRemoteCommands(sshClient, CleanupNginxConfigCommands(allowedDomains));
                    return (false, $"Failed to update nginx config for domain {domain}.");
                }

                if (!checkPingSuccess)
                {
                    commands = UpdateNginxConfigCommands(blocksApiDomain, IdentifierConstants.RemoteBlocksapiTemplate, "blocksapi-domain");
                    (result, response) = await ExecuteRemoteCommands(sshClient, commands);
                    allowedDomains.Add(blocksApiDomain);
                    if (!result)
                    {
                        _logger.LogError($"Failed to update nginx config for domain {blocksApiDomain}.");
                        await ExecuteRemoteCommands(sshClient, CleanupNginxConfigCommands(allowedDomains));
                        return (false, $"Failed to update nginx config for domain {domain}.");
                    }
                    _logger.LogInformation($"Updated nginx config successfully for domain {blocksApiDomain}.");
                }

                commands = ReloadNginxConfigCommands();
                (result, response) = await ExecuteRemoteCommands(sshClient, commands);
                if (!result)
                {
                    _logger.LogError($"Failed to reload nginx.");
                    await ExecuteRemoteCommands(sshClient, CleanupNginxConfigCommands(allowedDomains));
                    return (false, $"Failed to reload nginx.");
                }
                _logger.LogInformation($"Updated nginx config successfully for domains {string.Join(",", allowedDomains)}.");

                foreach (var item in allowedDomains)
                {
                    commands = SSLCertificateInstallCommands(item);
                    (result, response) = await ExecuteRemoteCommands(sshClient, commands);
                    if (!result)
                    {
                        _logger.LogError($"Failed install SSL certification for domain {item}.");
                        await ExecuteRemoteCommands(sshClient, CleanupNginxConfigCommands(new List<string> {item}));
                        return (false, $"Failed install SSL certification for domain {item}");
                    }
                    _logger.LogInformation($"SSL Crtification installed successfully for domain {item}.");
                }


                return (true, "All commands executed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH execution error");
                return (false, $"SSH error: {ex.Message}");
            }
            finally
            {
                if (sshClient.IsConnected)
                {
                    sshClient.Disconnect();
                    _logger.LogInformation("Disconnected from SSH server {Host}", host);
                }
            }
        }

        private async Task<(bool, string)> ExecuteRemoteCommands(SshClient sshClient, IEnumerable<string> commands)
        {
            foreach (var command in commands)
            {
                _logger.LogInformation("Executing: {Command}", command);
                using var cmd = sshClient.CreateCommand(command);
                await cmd.ExecuteAsync();

                if (cmd.ExitStatus != 0)
                {
                    _logger.LogError("Command failed: {Command}. Error: {Error}", command, cmd.Error);
                    return (false, $"Command failed: {command}. Error: {cmd.Error}");
                }
            }
            return (true, "All commands executed successfully.");
        }

        private async Task<(bool, string)> ExecuteRemoteCommandsAsync(IEnumerable<string> commands)
        {
            var host = _blocksSecret.SshHost;
            var username = _blocksSecret.SshUsername;
            var password = _blocksSecret.SshPassword;

            _logger.LogInformation("Connecting to SSH server {Host}...", host);

            using var sshClient = new SshClient(host, username, password);

            try
            {
                sshClient.Connect();
                if (!sshClient.IsConnected)
                {
                    _logger.LogError("Failed to connect to SSH server {Host}", host);
                    return (false, "SSH connection failed.");
                }

                _logger.LogInformation("SSH connected to {Host}", host);

                foreach (var command in commands)
                {
                    _logger.LogInformation("Executing: {Command}", command);
                    using var cmd = sshClient.CreateCommand(command);
                    await cmd.ExecuteAsync();

                    if (cmd.ExitStatus != 0)
                    {
                        _logger.LogError("Command failed: {Command}. Error: {Error}", command, cmd.Error);
                        return (false, $"Command failed: {command}. Error: {cmd.Error}");
                    }
                }

                return (true, "All commands executed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH execution error");
                return (false, $"SSH error: {ex.Message}");
            }
            finally
            {
                if (sshClient.IsConnected)
                {
                    sshClient.Disconnect();
                    _logger.LogInformation("Disconnected from SSH server {Host}", host);
                }
            }
        }

        private (string feDomain, string blocksapiDomain) ExtractDomainParts(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentException("Domain cannot be null or empty.", nameof(domain));

            var parts = domain.Split('.');
            if (parts.Length < 1)
                throw new ArgumentException("Invalid domain format.", nameof(domain));

            string feDomain = domain;
            string blocksapiDomain;

            if (parts.Length == 1)
            {
                blocksapiDomain = $"{_configuration["CnameRecordDomain"]}.{domain}";
            }
            else
            {
                parts[0] = _configuration["CnameRecordDomain"];
                blocksapiDomain = string.Join('.', parts);
            }
            return (feDomain, blocksapiDomain);
        }

        private List<string> UpdateNginxConfigCommands(string domain, string path, string placeholder)
        {
            return new List<string>  { 
                $"sudo cp {path} /etc/nginx/sites-available/{domain}",
                $"sudo sed -i 's/{{{placeholder}}}/{domain}/g' /etc/nginx/sites-available/{domain}",
                $"sudo ln -sf /etc/nginx/sites-available/{domain} /etc/nginx/sites-enabled/",
            };
        }
        
        private List<string> ReloadNginxConfigCommands()
        {
            return new List<string>  {
                $"sudo nginx -t",
                $"sudo systemctl reload nginx",
            };
        }
        
        private List<string> SSLCertificateInstallCommands(string domain)
        {
            return new List<string>  {
                   $"sudo certbot --webroot -w {IdentifierConstants.CertbotWebrootPath} --installer nginx -d {domain} --email {IdentifierConstants.CertbotEmail} --agree-tos --redirect --non-interactive -v",
            };
        }

        private List<string> CleanupNginxConfigCommands(List<string> domains)
        {
            var commands = new List<string>();

            foreach (var domain in domains)
            {
                commands.AddRange(new[]
                {
                    $"sudo rm -f /etc/nginx/sites-enabled/{domain}",
                    $"sudo rm -f /etc/nginx/sites-available/{domain}",
                });
            }
            commands.AddRange(ReloadNginxConfigCommands());
            return commands;
        }


        public async Task<(bool, string)> DisableDomainBindingAsync(DisableDomainBindingRequest request)
        {
            var domain = request.Domain.Replace("https://", "");

            var commands = new[]
            {
                $"sudo find / -type f -name \"*{domain}*\" -exec rm -f {{}} \\; 2>/dev/null",
                $"sudo rm -rf /etc/letsencrypt/archive/{domain}-*",
                $"sudo find /etc/nginx/sites-enabled/ -xtype l -delete",
                $"sudo nginx -t",
                $"sudo systemctl reload nginx"
            };

            var executionResult = await ExecuteRemoteCommandsAsync(commands);
            await UpdateDomainValidationStatusAsync(request.ProjectId, false);
            return executionResult;
        }


        private async Task<HttpsCertificateInfo> GetHttpsCertificateInfoAsync(string domain)
        {
            var result = new HttpsCertificateInfo();

            try
            {
                using (var tcpClient = new TcpClient())
                {
                    await tcpClient.ConnectAsync(domain, 443);

                    SslPolicyErrors policyErrors = SslPolicyErrors.None;

                    using (var sslStream = new SslStream(tcpClient.GetStream(), false,
                        (sender, cert, chain, errors) =>
                        {
                            policyErrors = errors;
                            return errors == SslPolicyErrors.None;
                        }))
                    {
                        await sslStream.AuthenticateAsClientAsync(domain);

                        result.SslPolicyErrors = policyErrors.ToString();

                        if (sslStream.RemoteCertificate is X509Certificate2 cert)
                        {
                            result.Subject = cert.Subject;
                            result.Issuer = cert.Issuer;
                            result.ValidFrom = cert.NotBefore;
                            result.ValidUntil = cert.NotAfter;
                            result.IsExpired = DateTime.UtcNow > cert.NotAfter;
                            result.HasValidCertificate = policyErrors == SslPolicyErrors.None && !result.IsExpired;
                        }
                        else
                        {
                            result.HasValidCertificate = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.HasValidCertificate = false;
                result.SslPolicyErrors = ex.Message;
            }

            _logger.LogInformation($"Certificate installation status for {domain}: {JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}");
            result.HasValidCertificate = false;
            return result;
        }

    }
}
