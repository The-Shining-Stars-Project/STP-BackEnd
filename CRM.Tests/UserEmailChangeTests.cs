using CRM.Application.DTOs.Auth;
using CRM.Application.Exceptions;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>Sep 2026: admins can correct a login's email; it must not collide with another account.</summary>
public class UserEmailChangeTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditService _audit = new();
    private readonly AuthService _service;
    private readonly User _admin;
    private readonly User _teacher;

    public UserEmailChangeTests()
    {
        var hasher = new FakePasswordHasher();
        var (hash, salt) = hasher.HashPassword("Sup3rSecret!");
        _admin = new User { Id = Guid.NewGuid(), Email = "admin@example.org", FullName = "Admin", Role = UserRole.Admin, IsActive = true, PasswordHash = hash, PasswordSalt = salt };
        _teacher = new User { Id = Guid.NewGuid(), Email = "old@example.org", FullName = "Teacher", Role = UserRole.Staff, IsActive = true, PasswordHash = hash, PasswordSalt = salt };
        _uow.UsersRepo.Items.AddRange([_admin, _teacher]);
        _service = new AuthService(_uow, hasher, new FakeTokenService(), TestMfa.Totp(), TestMfa.Protector(), _audit, NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task Email_is_normalised_and_saved()
    {
        var updated = await _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { Email = "  New.Address@Example.org " }, _admin.Id);

        Assert.Equal("new.address@example.org", updated!.Email);
        Assert.Equal("new.address@example.org", _teacher.Email);
    }

    [Fact]
    public async Task An_address_owned_by_another_account_is_refused()
    {
        await Assert.ThrowsAsync<DuplicateEmailException>(
            () => _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { Email = "ADMIN@example.org" }, _admin.Id));

        Assert.Equal("old@example.org", _teacher.Email);
    }

    [Fact]
    public async Task Resending_the_same_address_is_not_a_conflict()
    {
        var updated = await _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { Email = "old@example.org", FullName = "Renamed" }, _admin.Id);
        Assert.Equal("Renamed", updated!.FullName);
    }
}
