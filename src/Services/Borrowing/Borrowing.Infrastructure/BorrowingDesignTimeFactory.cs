using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Borrowing.Infrastructure;

public sealed class BorrowingDesignTimeFactory : IDesignTimeDbContextFactory<BorrowingDbContext>
{
    public BorrowingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BorrowingDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? throw new InvalidOperationException("Load the borrowing environment before running EF tools.")).Options);
}
