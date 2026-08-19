// Copyright 2018-2026 AVEVA Group Limited
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

public class EndpointConfigurationBase : EdgeConfigurationBase, IEndpointConfiguration
{
    private const string BufferingName = "Buffering";
    private const string MaxBufferSizeMbName = "MaxBufferSizeMB";

    private enum ValidationType
    {
        UserName,
        ClientId,
        None,
    }

    [Id]
    public virtual string Id { get; set; }

    [Required]
    [Url(ErrorMessage = "The Endpoint field is not a valid fully-qualified http or https URL.")]
    public string Endpoint { get; set; }

    [MinLength(OneCharacter)]
    [MutuallyExclusiveTo(nameof(ClientId), nameof(ClientSecret))]
    public virtual string UserName { get; set; }

    [Protected]
    [MinLength(OneCharacter)]
    [MutuallyExclusiveTo(nameof(ClientSecret))]
    public virtual string Password { get; set; }

    [MinLength(OneCharacter)]
    [MutuallyExclusiveTo(nameof(UserName), nameof(Password))]
    public string ClientId { get; set; }

    [Protected]
    [MinLength(OneCharacter)]
    [MutuallyExclusiveTo(nameof(Password))]
    public string ClientSecret { get; set; }

    [Description(DateTimePatternDescription)]
    public virtual DateTime? DebugExpiration { get; set; }

    [Url(ErrorMessage = "The TokenEndpoint field is not a valid fully-qualified http or https URL.")]
    public string TokenEndpoint { get; set; }

    public bool ValidateEndpointCertificate { get; set; } = true;

    public static ICollection<string> CheckForDuplicateEndpoints(ConfigurationChangedEventArgs configurationChangedEventArgs, string configName)
    {
        var errors = new List<string>();

        ThrowHelper.ThrowIfArgumentNull(configurationChangedEventArgs, nameof(configurationChangedEventArgs));

        if (configurationChangedEventArgs.NewValue is EndpointConfigurationBase[] endpointConfigurations)
        {
            var endpointSet = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var endpointConfiguration in endpointConfigurations)
            {
                if (endpointSet.Contains(endpointConfiguration.Endpoint))
                {
                    errors.Add($"Duplicate endpoint URL was found in the {configName} Configuration. Duplicate value: {endpointConfiguration.Endpoint}");
                }
                else
                {
                    endpointSet.Add(endpointConfiguration.Endpoint);
                }
            }
        }

        return errors;
    }

    public override IEnumerable<ValidationResult> Validate()
    {
        RemoveDeprecatedProperties();

        var errors = new List<string>();
        if (string.IsNullOrEmpty(Id))
        {
            Id = Guid.NewGuid().ToString();
        }

        ValidateEndpoints(errors);
        ValidateCredentials(errors);

        foreach (var error in errors)
        {
            yield return new ValidationResult(error);
        }
    }

    public override string ToString()
    {
        string authType;
        if (!string.IsNullOrEmpty(UserName))
        {
            authType = BasicAuth;
        }
        else if (!string.IsNullOrEmpty(ClientId))
        {
            authType = BearerAuth;
        }
        else
        {
            authType = NegotiateAuth;
        }

        return $@"
    {nameof(Id)}: {Id}
    {nameof(Endpoint)}: {Endpoint}
    {nameof(ValidateEndpointCertificate)}: {ValidateEndpointCertificate}
    Authentication type: {authType}";
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(Id);
        hashCode.Add(Endpoint);
        hashCode.Add(ClientId);
        hashCode.Add(ClientSecret);
        hashCode.Add(UserName);
        hashCode.Add(Password);
        hashCode.Add(DebugExpiration);
        hashCode.Add(TokenEndpoint);
        hashCode.Add(ValidateEndpointCertificate);
        return hashCode.ToHashCode();
    }

    public override bool Equals(object obj)
    {
        return obj switch
        {
            IEndpointConfiguration otherEndpoint => Equals(otherEndpoint),
            _ => false,
        };
    }

    public bool Equals(IEndpointConfiguration other)
    {
        if (other == null)
        {
            return false;
        }

        return Id == other.Id &&
               Endpoint == other.Endpoint &&
               UserName == other.UserName &&
               Password == other.Password &&
               ClientId == other.ClientId &&
               ClientSecret == other.ClientSecret &&
               EqualityComparer<DateTime?>.Default.Equals(DebugExpiration, other.DebugExpiration) &&
               TokenEndpoint == other.TokenEndpoint &&
               ValidateEndpointCertificate == other.ValidateEndpointCertificate;
    }

    private void ValidateEndpoints(List<string> errors)
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uriResult))
        {
            errors.Add($"{nameof(Endpoint)} - must be a valid URI.");
            return;
        }

        if (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{nameof(Endpoint)} - URI scheme is neither HTTP nor HTTPS");
            return;
        }

        if (!string.IsNullOrWhiteSpace(TokenEndpoint))
        {
            if (!Uri.TryCreate(TokenEndpoint, UriKind.Absolute, out var tokenUriResult))
            {
                errors.Add($"{nameof(TokenEndpoint)} - must be a valid URI");
                return;
            }

            if (tokenUriResult.Scheme != Uri.UriSchemeHttp && tokenUriResult.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add($"{nameof(TokenEndpoint)} - URI scheme is neither HTTP nor HTTPS");
            }
        }
    }

    private void ValidateCredentials(List<string> errors)
    {
        if (!UsingOneAuthentication(errors, out ValidationType validationType))
        {
            return;
        }

        switch (validationType)
        {
            case ValidationType.UserName:
                if (string.IsNullOrEmpty(Password))
                {
                    errors.Add($"{nameof(Password)} not supplied. {nameof(Password)} is required when using {nameof(UserName)} and {nameof(Password)} authentication.");
                }

                return;
            case ValidationType.ClientId:
                if (string.IsNullOrWhiteSpace(ClientSecret))
                {
                    errors.Add($"{nameof(ClientSecret)} not supplied. {nameof(ClientSecret)} is required when using {nameof(ClientId)} and {nameof(ClientSecret)} authentication.");
                }

                break;
            case ValidationType.None:
                break;
        }
    }

    /// <summary>
    /// Indicates whether the current configuration has only one authentication type.
    /// </summary>
    /// <param name="errors">Collection to add errors to. Cannot be null.</param>
    /// <param name="validationType">The validation type being used.</param>
    /// <returns> <see langword="true"/> if ONLY ONE of the following is set: <see cref="UserName"/> or <see cref="ClientId"/> is set; otherwise, <see langword="false"/>.
    /// <remarks>If the URL schema is HTTP then returns TRUE if no UserName/Password or ClientId/ClientSecret pairs are present.</remarks>
    /// </returns>
    private bool UsingOneAuthentication(List<string> errors, out ValidationType validationType)
    {
        bool userNameSet = !string.IsNullOrWhiteSpace(UserName);
        bool clientIdSet = !string.IsNullOrWhiteSpace(ClientId);
        bool passwordSet = !string.IsNullOrWhiteSpace(Password);
        bool clientSecretSet = !string.IsNullOrWhiteSpace(ClientSecret);

        int numberAuthentication = new[] { userNameSet, clientIdSet }.Count(x => x);
        if (numberAuthentication > 1 ||
            (passwordSet && clientIdSet) || (clientSecretSet && userNameSet) || (clientSecretSet && passwordSet))
        {
            errors.Add("Only one type of authentication is allowed. " +
                "You MUST have only ONE of the following: " +
                $"{nameof(UserName)} and {nameof(Password)} pair OR {nameof(ClientId)} and {nameof(ClientSecret)} pair.");
            validationType = ValidationType.None;
            return false;
        }

        if (!userNameSet && passwordSet)
        {
            errors.Add($"{nameof(UserName)} not supplied. {nameof(UserName)} is required when using {nameof(UserName)} and {nameof(Password)} authentication.");
            validationType = ValidationType.None;
            return false;
        }
        
        if (!clientIdSet && clientSecretSet)
        {
            errors.Add($"{nameof(ClientId)} not supplied. {nameof(ClientId)} is required when using {nameof(ClientId)} and {nameof(ClientSecret)} authentication.");
            validationType = ValidationType.None;
            return false;
        }

        if (userNameSet)
        {
            validationType = ValidationType.UserName;
            return true;
        }

        if (clientIdSet)
        {
            validationType = ValidationType.ClientId;
            return true;
        }

        validationType = ValidationType.None;
        return true;
    }

    private void RemoveDeprecatedProperties()
    {
        JsonExtensionData.Remove(BufferingName);
        JsonExtensionData.Remove(MaxBufferSizeMbName);
    }
}
