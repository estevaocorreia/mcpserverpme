using Microsoft.EntityFrameworkCore;


namespace Mcpserver.Infrastructure.Data
{


    public sealed class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }


    }

}

