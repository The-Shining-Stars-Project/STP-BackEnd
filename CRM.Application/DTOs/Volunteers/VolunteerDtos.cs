using System.ComponentModel.DataAnnotations;

namespace CRM.Application.DTOs.Volunteers;

public class VolunteerDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public string StartDate { get; set; } = string.Empty;
}

public class CreateVolunteerDto
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public Guid ProgramId { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime? StartDate { get; set; }
}

public class UpdateVolunteerDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? FullName { get; set; }

    public Guid? ProgramId { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public bool? IsActive { get; set; }
}
