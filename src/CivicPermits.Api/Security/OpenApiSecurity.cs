using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CivicPermits.Api.Security;

/// <summary>Describes bearer-token sign-in in the OpenAPI document, and marks the operations that need it.</summary>
internal static class OpenApiSecurity
{
    private const string SchemeName = "Bearer";

    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Civic Permits API",
                Version = "v1",
                Description = "A city permit office's service: apply for a permit, review and decide, inspect, issue. "
                    + "Portfolio demonstration with fictional data. Errors are RFC 9457 problem documents with a stable `code`.",
            };

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "The access token from POST /api/v1/auth/login, sent as `Authorization: Bearer <token>`.",
            };

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
            bool needsSignIn = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();

            if (needsSignIn)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
                });
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
