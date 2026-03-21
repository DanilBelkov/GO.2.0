using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GO2.Api.Data;

// Шаблон design-time фабрики для `dotnet ef` (оставлен закомментированным для локального использования при необходимости).
//public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
//{
//    public AppDbContext CreateDbContext(string[] args)
//    {
//        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
//        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=go2;Username=postgres;Password=dany");
//        return new AppDbContext(optionsBuilder.Options);
//    }
//}

