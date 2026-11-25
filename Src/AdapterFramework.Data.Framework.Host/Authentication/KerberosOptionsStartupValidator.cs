using System;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Host.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdapterFramework.Data.Framework.Host.Authentication;

internal class KerberosOptionsStartupValidator : IValidateOptions<AccessAdapterAuthorizationOptions>
{
    private readonly ILogger _systemLogger;
    private readonly IConfiguration _configuration;
    private bool domainJoined;

    public KerberosOptionsStartupValidator(
        ILogger systemLogger,
        IConfiguration configuration)
    {
        _systemLogger = systemLogger;
        _configuration = configuration;
    }

    internal enum NetSetupJoinStatus
    {
        Unknown = 0,
        Unjoined,
        WorkgroupName,
        DomainName,
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int NetGetJoinInformation(
            string server, out IntPtr domain, out NetSetupJoinStatus status);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int NetApiBufferFree(IntPtr buffer);

    ValidateOptionsResult IValidateOptions<AccessAdapterAuthorizationOptions>.Validate(string name, AccessAdapterAuthorizationOptions options)
    {
        return ValidateKerberosOptionsWindows(options);
    }

    private void LogWindowsClientDetails()
    {
        if (!TryResolveHostNameWindows(out string hostName))
        {
            return;
        }

        int port = EdgeSystemHelper.GetPortNumber(_configuration);
        _systemLogger.LogInformation(
            "Client URL: https://{HostName}:{Port}/",
            hostName,
            port);
        if (domainJoined)
        {
            _systemLogger.LogInformation(
                "Expected HTTP SPN for Kerberos: HTTP/{HostName}",
                hostName);
        }
    }

    private bool TryResolveHostNameWindows(out string hostName)
    {
        hostName = string.Empty;
        string shortHostName;
        try
        {
            shortHostName = Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            shortHostName = string.Empty;
        }

        try
        {
            string fullyQualifiedHostName = Dns.GetHostEntry(shortHostName).HostName.TrimEnd('.');
            if (fullyQualifiedHostName.Contains('.') &&
                IsValidHostName(fullyQualifiedHostName))
            {
                hostName = fullyQualifiedHostName;
                return true;
            }
        }
        catch (Exception exception)
        when (exception is SocketException || exception is ArgumentException)
        {
            if (!IsValidHostName(shortHostName))
            {
                _systemLogger.LogWarning(
                    "Unable to determine a valid machine host or domain name. The client URL and expected HTTP SPN could not be logged.");
            }
            else
            {
                _systemLogger.LogWarning(
                    exception,
                    "Unable to resolve the fully qualified host name for {ShortHostName}; using the short machine name.",
                    shortHostName);
            }
        }

        hostName = shortHostName;
        return true;
    }

    private static bool IsValidHostName(string hostName)
    {
        return !string.IsNullOrWhiteSpace(hostName) &&
            Uri.CheckHostName(hostName) == UriHostNameType.Dns;
    }

    private ValidateOptionsResult ValidateKerberosOptionsWindows(AccessAdapterAuthorizationOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ValidateOptionsResult.Fail("Operating system must be Windows for local Kerberos authentication.");
        }

        // Get domain join status from NetGetJoinInformation dll
        IntPtr buffer = IntPtr.Zero;
        NetSetupJoinStatus status;

        try
        {
            int netGetJoinCheckStatus = NetGetJoinInformation(null, out buffer, out status);
            if (netGetJoinCheckStatus == 0 && status != NetSetupJoinStatus.DomainName)
            {
                _systemLogger.LogWarning("The adapter host is not domain-joined. Windows authentication remains enabled. Local Windows accounts require NTLM to be permitted by Windows policy and membership in the configured adapter authorization group.");
            }
            else if (netGetJoinCheckStatus != 0)
            {
                _systemLogger.LogWarning("Domain join status could not be resolved.");
            }
            else
            {
                domainJoined = true;
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                _ = NetApiBufferFree(buffer);
            }
        }


        if (string.IsNullOrWhiteSpace(options.LocalGroup.Name))
        {
            return ValidateOptionsResult.Fail(
                $"{ConfigurationConstants.KerberosLocalGroupNameKey} is required on Windows.");
        }

        if (string.IsNullOrWhiteSpace(options.LocalGroup.Sid))
        {
            return ValidateOptionsResult.Fail(
                $"{ConfigurationConstants.KerberosLocalGroupSidKey} is required on Windows.");
        }

        SecurityIdentifier configuredSid = null;

        try
        {
            configuredSid = new SecurityIdentifier(options.LocalGroup.Sid);
        }

        catch (ArgumentException ex)
        {
            return ValidateOptionsResult.Fail(
                $"The configured SID is invalid: " + ex.Message);
        }

        try
        {
            using var localContext = new PrincipalContext(
                ContextType.Machine);

            using var localGroup = GroupPrincipal.FindByIdentity(
                localContext,
                IdentityType.Name,
                options.LocalGroup.Name);

            if (localGroup == null)
            {
                return ValidateOptionsResult.Fail(
                    $"The Windows local security group " +
                    $"'{options.LocalGroup.Name}' does not exist.");
            }
            else if (localGroup.Sid == null)
            {
                return ValidateOptionsResult.Fail(
                    $"The Windows local security group " +
                    $"'{options.LocalGroup.Name}' has no SID.");
            }
            else if (configuredSid == null || !configuredSid.Equals(localGroup.Sid))
            {
                return ValidateOptionsResult.Fail(
                    $"The Windows local security group " +
                    $"'{options.LocalGroup.Name}' SID does not match the configured SID.");
            }

            if (localGroup.Members.Count == 0)
            {
                _systemLogger.LogError("The Windows security group " +
                   "{OptionsLocalGroupName} has no members. All Kerberos requests will be denied until members are added.", options.LocalGroup.Name);
            }
        }
        catch (PrincipalException ex)
        {
            return ValidateOptionsResult.Fail(
                $"Unable to validate Windows security group " +
                $"'{options.LocalGroup.Name}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ValidateOptionsResult.Fail(
                $"Access was denied while validating Windows " +
                $"security group '{options.LocalGroup.Name}': " +
                ex.Message);
        }
        LogWindowsClientDetails();
        return ValidateOptionsResult.Success;
    }
}
