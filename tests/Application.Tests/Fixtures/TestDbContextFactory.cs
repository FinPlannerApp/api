using System;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests.Fixtures;

public static class TestDbContextFactory
{
    public static ApplicationDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options, new HttpContextAccessor());
        context.Database.EnsureCreated();
        return context;
    }
}
