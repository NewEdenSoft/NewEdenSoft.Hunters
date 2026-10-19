var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("hunters");

var server = builder.AddProject<Projects.Hunters_Server>("server")
    .WithReference(database)
    .WaitFor(database);

builder.AddViteApp("web", "../../web/hunters-web")
    .WithReference(server)
    .WaitFor(server);

builder.Build().Run();
