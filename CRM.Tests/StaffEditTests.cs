using CRM.Application.DTOs.Staff;
using CRM.Application.Services;
using CRM.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>
/// Sep 17 2026: staff records can be corrected after the fact (misspelled names, wrong role
/// or start date), former members included, and the Technology &amp; Systems Coordinator role exists.
/// </summary>
public class StaffEditTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly StaffService _staff;

    public StaffEditTests()
    {
        _staff = new StaffService(_uow, new FakeOrgClock(), new FakeFileStorage(), NullLogger<StaffService>.Instance);
    }

    [Fact]
    public async Task Update_corrects_name_initials_role_and_start_date()
    {
        var created = await _staff.CreateAsync(new CreateStaffDto { FullName = "JoAnna Smyth", Initials = "JS", Role = StaffRole.Teacher });

        var updated = await _staff.UpdateAsync(created.Id, new UpdateStaffDto
        {
            FullName = "JoAnna Smith",
            Initials = "JSm",
            Role = StaffRole.TechnologySystemsCoordinator,
            StartDate = new DateTime(2026, 8, 3, 15, 0, 0),
        });

        Assert.NotNull(updated);
        Assert.Equal("JoAnna Smith", updated!.FullName);
        Assert.Equal("JSm", updated.Initials);
        Assert.Equal(StaffRole.TechnologySystemsCoordinator, updated.Role);
        Assert.Equal("2026-08-03", updated.StartDate);
        Assert.False(updated.IsFormer);
    }

    [Fact]
    public async Task Update_renames_a_former_member_without_reactivating_them()
    {
        var created = await _staff.CreateAsync(new CreateStaffDto { FullName = "Scott Davis", Initials = "SD", Role = StaffRole.Teacher });
        await _staff.UpdateAsync(created.Id, new UpdateStaffDto { EndDate = new DateTime(2025, 6, 30) });

        var updated = await _staff.UpdateAsync(created.Id, new UpdateStaffDto { FullName = "Scott Davis (2024–25)" });

        Assert.NotNull(updated);
        Assert.Equal("Scott Davis (2024–25)", updated!.FullName);
        Assert.True(updated.IsFormer);
        Assert.Equal("2025-06-30", updated.EndDate);
    }

    [Fact]
    public async Task Update_leaves_untouched_fields_alone()
    {
        var created = await _staff.CreateAsync(new CreateStaffDto { FullName = "Bronel Lee", Initials = "BL", Role = StaffRole.Coordinator, TShirtSize = "L" });

        var updated = await _staff.UpdateAsync(created.Id, new UpdateStaffDto { Role = StaffRole.TechnologySystemsCoordinator });

        Assert.Equal("Bronel Lee", updated!.FullName);
        Assert.Equal("L", updated.TShirtSize);
        Assert.Equal(created.StartDate, updated.StartDate);
    }
}
