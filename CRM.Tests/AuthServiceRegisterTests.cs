using CRM.Application.DTOs.Auth;
using CRM.Application.Exceptions;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Infrastructure.Auth;
using CRM.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>
/// Registration rejects for two unrelated reasons, and the API has to tell them apart.
/// Both used to be InvalidOperationException, the controller mapped every one to 409, and
/// the users screen read 409 as "that email is taken" — so an admin creating a colleague's
/// account was told to change the address when the real problem was the password.
/// </summary>
public class AuthServiceRegisterTests
{
    private const string Existing = "taken@example.org";

    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditService _audit = new();
    private readonly AuthService _service;

    public AuthServiceRegisterTests()
    {
        var hasher = new FakePasswordHasher();
        var (hash, salt) = hasher.HashPassword("Sup3rSecret!");
        _uow.UsersRepo.Items.Add(new User
        {
            Id = Guid.NewGuid(), Email = Existing, FullName = "Taken",
            Role = UserRole.Staff, IsActive = true, PasswordHash = hash, PasswordSalt = salt,
        });
        _service = new AuthService(
            _uow, hasher, new FakeTokenService(), TestMfa.Totp(), TestMfa.Protector(),
            _audit, NullLogger<AuthService>.Instance);
    }

    private static RegisterUserDto Dto(string email, string password) => new()
    {
        Email = email, FullName = "New Person", Password = password, Role = UserRole.Staff,
    };

    [Theory]
    [InlineData("short1")]              // under 8 characters
    [InlineData("nodigitshere")]        // no digit
    [InlineData("12345678")]            // no letter
    [InlineData("ChangeMe!123")]        // the burned default
    public async Task A_rejected_password_is_not_a_duplicate_email(string password)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegisterAsync(Dto("brand.new@example.org", password)));

        // The distinction the controller keys off: this must NOT be the 409 type.
        Assert.IsNotType<DuplicateEmailException>(ex);
        Assert.DoesNotContain("already exists", ex.Message);
    }

    [Fact]
    public async Task A_taken_address_throws_the_duplicate_type()
    {
        await Assert.ThrowsAsync<DuplicateEmailException>(
            () => _service.RegisterAsync(Dto(Existing, "Val1dPassword")));
    }

    [Fact]
    public async Task A_taken_address_is_matched_case_insensitively_and_trimmed()
    {
        await Assert.ThrowsAsync<DuplicateEmailException>(
            () => _service.RegisterAsync(Dto("  TAKEN@Example.ORG  ", "Val1dPassword")));
    }

    [Fact]
    public async Task A_valid_request_creates_the_account()
    {
        var created = await _service.RegisterAsync(Dto("fresh@example.org", "Val1dPassword"));
        Assert.Equal("fresh@example.org", created.Email);
        Assert.True(created.IsActive);
    }
}
