using System;
using System.Linq;
using System.Threading.Tasks;
using Application.Tests.Fixtures;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Application.Tests.Integration;

public class AuthServiceIntegrationTests
{
    private readonly Argon2PasswordHasher<ApplicationUser> _hasher = new();

    [Fact]
    public async Task RegisterUser_HashesPasswordAndSavesToDb()
    {
        using var context = TestDbContextFactory.Create();

        var user = new ApplicationUser
        {
            Id = "user-auth-1",
            UserName = "testuser@example.com",
            Email = "testuser@example.com",
            Name = "Test User"
        };

        var hashedPassword = _hasher.HashPassword(user, "SecurePass123!");
        user.PasswordHash = hashedPassword;

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var dbUser = context.Users.FirstOrDefault(u => u.Email == "testuser@example.com");
        dbUser.Should().NotBeNull();
        
        var verifyResult = _hasher.VerifyHashedPassword(dbUser!, dbUser!.PasswordHash!, "SecurePass123!");
        verifyResult.Should().Be(PasswordVerificationResult.Success);
    }
}
