namespace CRM.Domain.Enums;

public enum StaffRole
{
    Teacher,
    Coordinator,
    Admin,
    /// <summary>Classroom support; same (read + notes) access as a Teacher.</summary>
    TeacherAssistant,
    /// <summary>Runs the CRM and org systems; same management-write access as a Coordinator.</summary>
    TechnologySystemsCoordinator,
}
