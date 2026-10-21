var builder = DistributedApplication.CreateBuilder(args);

// Server settings, read from the "Parameters" section of this project's configuration
// (appsettings.json, overridable with user secrets). See README.md, "Configuration".
var serverName = builder.AddParameter("server-name")
    .WithDescription("Display name of the server, shown to clients.");
var eveClientId = builder.AddParameter("eve-client-id")
    .WithDescription("Client ID of your EVE application (developers.eveonline.com).");
var eveClientSecret = builder.AddParameter("eve-client-secret", secret: true)
    .WithDescription("Secret key of your EVE application. Prefer user secrets over appsettings.json.");
var eveCallbackUrl = builder.AddParameter("eve-callback-url")
    .WithDescription("Callback URL registered on your EVE application. Locally: the web dev server's /auth/eve/callback.");
var adminCharacterId = builder.AddParameter("admin-character-id")
    .WithDescription("EVE character ID of the server admin. Optional.");

var database = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("hunters");

var server = builder.AddProject<Projects.Hunters_Server>("server")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Hunters__Name", serverName)
    .WithEnvironment("Hunters__Sso__ClientId", eveClientId)
    .WithEnvironment("Hunters__Sso__ClientSecret", eveClientSecret)
    .WithEnvironment("Hunters__Sso__CallbackUrl", eveCallbackUrl)
    .WithEnvironment("Hunters__Admins__0", adminCharacterId);

// Fixed port: the EVE callback URL points at this dev server, which forwards /auth to the server.
builder.AddViteApp("web", "../../web/hunters-web")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(server)
    .WaitFor(server);

builder.Build().Run();
