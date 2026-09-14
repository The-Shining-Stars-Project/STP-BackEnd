namespace CRM.Domain.Enums;

public enum StaffRole
{
    Teacher,
    Coordinator,
    Admin,
    /// <summary>Classroom support; same (read + notes) access as a Teacher.</summary>
    TeacherAssistant,
}
