using Microsoft.EntityFrameworkCore;

namespace NewEdenSoft.Hunters.Server.Data;

public sealed class HuntersDbContext(DbContextOptions<HuntersDbContext> options) : DbContext(options)
{
    /// <summary>Connection string name, shared by Aspire and Docker Compose (<c>ConnectionStrings__hunters</c>).</summary>
    public const string ConnectionName = "hunters";
}
