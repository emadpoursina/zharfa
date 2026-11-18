namespace Zharfa.Api.Security;

using Microsoft.AspNetCore.Authorization;

/// <summary>An authorization requirement met when the signed-in role holds <see cref="Permission"/>.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
