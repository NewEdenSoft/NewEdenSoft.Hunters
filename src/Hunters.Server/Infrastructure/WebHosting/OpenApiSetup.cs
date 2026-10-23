using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

/// <summary>
/// The OpenAPI document (<c>/openapi/v1.json</c>) and an interactive reference for it (Scalar, at
/// <c>/scalar</c>). Calls from the reference are signed in like any other client: by the session
/// cookie of the web app on the same host, or by a desktop session token entered as a bearer token.
/// </summary>
public static class OpenApiSetup
{
    public const string BearerScheme = "Bearer";

    public static IServiceCollection AddHuntersOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "A session token from a desktop login. The web app's session cookie works too.",
            };
            document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [] }];
            return Task.CompletedTask;
        }));

    public static IEndpointRouteBuilder MapHuntersOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
        endpoints.MapScalarApiReference(options => options
            .WithTitle("Hunters API")
            .AddPreferredSecuritySchemes(BearerScheme));
        return endpoints;
    }
}
