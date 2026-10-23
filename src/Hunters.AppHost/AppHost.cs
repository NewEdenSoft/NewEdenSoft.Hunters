var builder = DistributedApplication.CreateBuilder(args);

// Server settings, read from the "Parameters" section of this project's configuration:
// appsettings.json holds the defaults, appsettings.Development.json (not committed) or user
// secrets hold your own values. See README.md, "Configuration".
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

// Persistent: the container outlives AppHost runs and its data is in a named volume, so collected
// intel (90 days of hours and kills) builds up across sessions. Its generated password is kept in this
// project's user secrets, which must stay for the volume to stay usable.
var database = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("hunters");

var server = builder.AddProject<Projects.Hunters_Server>("server")
    .WithReference(database)
    .WaitFor(database)
    // The Vite dev server serves the web app during development.
    .WithEnvironment("Hunters__Web__Enabled", "false")
    .WithEnvironment("Hunters__Name", serverName)
    .WithEnvironment("Hunters__Sso__ClientId", eveClientId)
    .WithEnvironment("Hunters__Sso__ClientSecret", eveClientSecret)
    .WithEnvironment("Hunters__Sso__CallbackUrl", eveCallbackUrl)
    .WithEnvironment("Hunters__Admins__0", adminCharacterId);

// The intel service collects public activity data into the database's intel schema (D22). The
// server reads it; it doesn't wait for intel, whose endpoints are empty until the first data is in.
builder.AddProject<Projects.Hunters_Intel>("intel")
    .WithReference(database)
    .WaitFor(database);

// Fixed port: the EVE callback URL points at this dev server, which forwards /auth to the server.
builder.AddViteApp("web", "../../web/hunters-web")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(server)
    .WaitFor(server);

builder.Build().Run();
