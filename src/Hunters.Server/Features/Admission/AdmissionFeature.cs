namespace NewEdenSoft.Hunters.Server.Features.Admission;

/// <summary>The optional admission policy and its periodic recheck.</summary>
public static class AdmissionFeature
{
    public static IServiceCollection AddAdmissionFeature(this IServiceCollection services)
    {
        services.AddScoped<AdmissionService>();
        services.AddHostedService<AdmissionRecheckService>();
        return services;
    }
}
