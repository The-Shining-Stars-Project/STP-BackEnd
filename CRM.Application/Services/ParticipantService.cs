using CRM.Application.DTOs.Participants;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class ParticipantService : IParticipantService
{
    private readonly IUnitOfWork _uow;
    private readonly IStatsQueries _stats;
    private readonly IProgramAccessService _access;
    private readonly IOrgClock _clock;

    public ParticipantService(IUnitOfWork uow, IStatsQueries stats, IProgramAccessService access, IOrgClock clock)
    {
        _uow = uow;
        _stats = stats;
        _access = access;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ParticipantSummaryDto>> GetAllAsync(Guid userId, CancellationToken ct = default)
    {
        // Scoped to the caller's programs (#1) — a teacher listing participants must not
        // see (or be able to enumerate the ids of) children in programs they don't teach.
        var access = await _access.ForUserAsync(userId);

        var participants = (await _uow.Participants.GetAllAsync(ct))
            .Where(p => access.CanAccess(p.ProgramId))
            .ToList();
        if (participants.Count == 0) return new List<ParticipantSummaryDto>();

        var programs = await _uow.Programs.GetAllAsync(ct);
        var programMap = programs.ToDictionary(p => p.Id, p => p.Name);
        var slugMap = programs.ToDictionary(p => p.Id, p => p.Slug);

        // Attendance % from SQL-side aggregates (#8/#11) — no whole-ledger load.
        var pctMap = AttendanceStats.PercentByParticipant(await _stats.GetParticipantAttendanceAsync(ct));
        return participants.Select(p => ToSummary(p, programMap, slugMap, pctMap)).ToList();
    }

    public async Task<ParticipantDetailDto?> GetByIdAsync(Guid userId, Guid id)
    {
        var p = await _access.RequireParticipantAsync(userId, id);
        if (p is null) return null;

        var prog = await _uow.Programs.GetByIdAsync(p.ProgramId);
        var secondary = p.SecondaryProgramId is { } sid ? await _uow.Programs.GetByIdAsync(sid) : null;
        var records = await _uow.Attendance.ListAsync(r => r.ParticipantId == id);

        // A star's paperwork is admin-only (client rule): teachers get the profile without
        // the document list, and the document endpoints themselves are Admin-gated too.
        var access = await _access.ForUserAsync(userId);
        var documents = access.IsAdmin
            ? await _uow.DocumentRecords.ListAsync(d => d.ParticipantId == id)
            : Array.Empty<DocumentRecord>();

        return new ParticipantDetailDto
        {
            Id = p.Id,
            FullName = p.FullName,
            PreferredName = p.PreferredName,
            Initials = p.Initials,
            Status = p.Status,
            ProgramId = p.ProgramId,
            ProgramName = prog?.Name ?? string.Empty,
            ProgramSlug = prog?.Slug ?? string.Empty,
            AttendancePct = AttendanceStats.PercentFor(records),
            StartDate = p.StartDate.ToString("yyyy-MM-dd"),
            HasDocAlerts = DocumentAlerts.For(p),
            BirthYear = p.BirthYear,
            ServiceCoordinator = p.ServiceCoordinator,
            GuardianName = p.GuardianName,
            GuardianPhone = p.GuardianPhone,
            GuardianEmail = p.GuardianEmail,
            ReferralSource = p.ReferralSource,
            TShirtSize = p.TShirtSize,
            IntakeNotes = p.IntakeNotes,
            AuthorizationExpiry = p.AuthorizationExpiry?.ToString("yyyy-MM-dd"),
            IppExpiry = p.IppExpiry?.ToString("yyyy-MM-dd"),
            DateOfBirth = p.DateOfBirth?.ToString("yyyy-MM-dd"),
            Allergies = p.Allergies,
            AllergyAnaphylactic = p.AllergyAnaphylactic,
            AreasOfConcern = p.AreasOfConcern,
            ServiceCoordinatorEmail = p.ServiceCoordinatorEmail,
            ServiceCoordinatorPhone = p.ServiceCoordinatorPhone,
            ContactInRemind = p.ContactInRemind,
            IntakeDocsSubmitted = p.IntakeDocsSubmitted,
            HasHighSchoolDiploma = p.HasHighSchoolDiploma,
            EmergencyContacts = SplitContacts(p.EmergencyContacts),
            IsSdpClient = p.IsSdpClient,
            SdpFmsName = p.SdpFmsName,
            SdpIndependentFacilitator = p.SdpIndependentFacilitator,
            SdpStartDate = p.SdpStartDate?.ToString("yyyy-MM-dd"),
            SecondaryProgramId = p.SecondaryProgramId,
            SecondaryProgramName = secondary?.Name,
            SecondaryProgramSlug = secondary?.Slug,
            Documents = documents.OrderBy(d => d.CreatedAt).Select(ParticipantDocumentService.ToDto).ToList(),
            RecentAttendance = new(),
        };
    }

    public async Task<ParticipantDetailDto> CreateAsync(Guid userId, CreateParticipantDto dto)
    {
        // You may only enrol a child into a program you run.
        var access = await _access.ForUserAsync(userId);
        access.Require(dto.ProgramId);

        var participant = new Participant
        {
            FullName = dto.FullName,
            PreferredName = string.IsNullOrWhiteSpace(dto.PreferredName) ? null : dto.PreferredName.Trim(),
            Initials = dto.Initials,
            ProgramId = dto.ProgramId,
            Status = dto.Status,
            BirthYear = dto.BirthYear ?? dto.DateOfBirth?.Year,
            ServiceCoordinator = dto.ServiceCoordinator,
            StartDate = dto.StartDate ?? _clock.Today,
            GuardianName = dto.GuardianName,
            GuardianPhone = dto.GuardianPhone,
            GuardianEmail = dto.GuardianEmail,
            ReferralSource = dto.ReferralSource,
            TShirtSize = dto.TShirtSize,
            IntakeNotes = dto.IntakeNotes,
            AuthorizationExpiry = dto.AuthorizationExpiry,
            IppExpiry = dto.IppExpiry,
            DateOfBirth = dto.DateOfBirth,
            Allergies = dto.Allergies,
            AllergyAnaphylactic = dto.AllergyAnaphylactic,
            AreasOfConcern = dto.AreasOfConcern,
            ServiceCoordinatorEmail = dto.ServiceCoordinatorEmail,
            ServiceCoordinatorPhone = dto.ServiceCoordinatorPhone,
            ContactInRemind = dto.ContactInRemind,
            IntakeDocsSubmitted = dto.IntakeDocsSubmitted,
            HasHighSchoolDiploma = dto.HasHighSchoolDiploma,
            EmergencyContacts = JoinContacts(dto.EmergencyContacts),
            IsSdpClient = dto.IsSdpClient,
            SdpFmsName = dto.SdpFmsName,
            SdpIndependentFacilitator = dto.SdpIndependentFacilitator,
            SdpStartDate = dto.SdpStartDate,
            SecondaryProgramId = dto.SecondaryProgramId,
        };
        if (dto.SecondaryProgramId is { } secId) access.Require(secId);

        await _uow.Participants.AddAsync(participant);
        await _uow.SaveChangesAsync();

        return (await GetByIdAsync(userId, participant.Id))!;
    }

    public async Task<ParticipantDetailDto?> UpdateAsync(Guid userId, Guid id, UpdateParticipantDto dto)
    {
        var participant = await _access.RequireParticipantAsync(userId, id);
        if (participant is null) return null;

        // Moving a child between programs needs scope over the destination too, otherwise
        // it becomes a way to push records into a program you can't otherwise write to.
        if (dto.ProgramId.HasValue && dto.ProgramId.Value != participant.ProgramId)
        {
            var access = await _access.ForUserAsync(userId);
            access.Require(dto.ProgramId.Value);
        }

        if (dto.FullName is not null) participant.FullName = dto.FullName;
        if (dto.Initials is not null) participant.Initials = dto.Initials;
        if (dto.PreferredName is not null) participant.PreferredName = string.IsNullOrWhiteSpace(dto.PreferredName) ? null : dto.PreferredName.Trim();
        if (dto.ProgramId.HasValue) participant.ProgramId = dto.ProgramId.Value;
        if (dto.Status.HasValue) participant.Status = dto.Status.Value;
        if (dto.BirthYear.HasValue) participant.BirthYear = dto.BirthYear;
        if (dto.ServiceCoordinator is not null) participant.ServiceCoordinator = dto.ServiceCoordinator;
        if (dto.GuardianName is not null) participant.GuardianName = dto.GuardianName;
        if (dto.GuardianPhone is not null) participant.GuardianPhone = dto.GuardianPhone;
        if (dto.GuardianEmail is not null) participant.GuardianEmail = dto.GuardianEmail;
        if (dto.ReferralSource is not null) participant.ReferralSource = dto.ReferralSource;
        if (dto.TShirtSize is not null) participant.TShirtSize = dto.TShirtSize;
        if (dto.IntakeNotes is not null) participant.IntakeNotes = dto.IntakeNotes;
        if (dto.StartDate.HasValue) participant.StartDate = dto.StartDate.Value;
        if (dto.EmergencyContacts is not null) participant.EmergencyContacts = JoinContacts(dto.EmergencyContacts);
        if (dto.IsSdpClient.HasValue) participant.IsSdpClient = dto.IsSdpClient;
        if (dto.SdpFmsName is not null) participant.SdpFmsName = dto.SdpFmsName;
        if (dto.SdpIndependentFacilitator is not null) participant.SdpIndependentFacilitator = dto.SdpIndependentFacilitator;
        if (dto.SdpStartDate.HasValue) participant.SdpStartDate = dto.SdpStartDate;
        else if (dto.ClearSdpStartDate) participant.SdpStartDate = null;
        if (dto.AuthorizationExpiry.HasValue) participant.AuthorizationExpiry = dto.AuthorizationExpiry;
        else if (dto.ClearAuthorizationExpiry) participant.AuthorizationExpiry = null;
        if (dto.IppExpiry.HasValue) participant.IppExpiry = dto.IppExpiry;
        else if (dto.ClearIppExpiry) participant.IppExpiry = null;
        if (dto.DateOfBirth.HasValue)
        {
            participant.DateOfBirth = dto.DateOfBirth;
            participant.BirthYear ??= dto.DateOfBirth.Value.Year;
        }
        if (dto.Allergies is not null) participant.Allergies = dto.Allergies;
        if (dto.AllergyAnaphylactic.HasValue) participant.AllergyAnaphylactic = dto.AllergyAnaphylactic.Value;
        if (dto.AreasOfConcern is not null) participant.AreasOfConcern = dto.AreasOfConcern;
        if (dto.ServiceCoordinatorEmail is not null) participant.ServiceCoordinatorEmail = dto.ServiceCoordinatorEmail;
        if (dto.ServiceCoordinatorPhone is not null) participant.ServiceCoordinatorPhone = dto.ServiceCoordinatorPhone;
        if (dto.ContactInRemind is not null) participant.ContactInRemind = dto.ContactInRemind;
        if (dto.IntakeDocsSubmitted.HasValue) participant.IntakeDocsSubmitted = dto.IntakeDocsSubmitted.Value;
        if (dto.HasHighSchoolDiploma.HasValue) participant.HasHighSchoolDiploma = dto.HasHighSchoolDiploma;
        if (dto.SecondaryProgramId is { } newSecId && newSecId != participant.SecondaryProgramId)
        {
            (await _access.ForUserAsync(userId)).Require(newSecId);
            participant.SecondaryProgramId = newSecId;
        }
        else if (dto.ClearSecondaryProgram) participant.SecondaryProgramId = null;

        await _uow.Participants.UpdateAsync(participant);
        await _uow.SaveChangesAsync();

        return await GetByIdAsync(userId, id);
    }

    /// <summary>
    /// Notes-only update for teachers: in-scope like every read, no management policy. The
    /// profile's other fields stay behind ManagementWrite on the full update.
    /// </summary>
    public async Task<ParticipantDetailDto?> UpdateIntakeNotesAsync(Guid userId, Guid id, string? notes)
    {
        var participant = await _access.RequireParticipantAsync(userId, id);
        if (participant is null) return null;

        participant.IntakeNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await _uow.Participants.UpdateAsync(participant);
        await _uow.SaveChangesAsync();
        return await GetByIdAsync(userId, id);
    }

    /// <summary>
    /// Soft delete. The row stays (attendance, scores and documents keep their foreign keys —
    /// a hard delete failed on the Restrict constraints the moment a star had history) but the
    /// global query filter hides it from every list, lookup and navigation from here on.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid id)
    {
        var participant = await _access.RequireParticipantAsync(userId, id);
        if (participant is null) return false;

        participant.IsDeleted = true;
        participant.DeletedAt = DateTime.UtcNow;
        await _uow.Participants.UpdateAsync(participant);
        await _uow.SaveChangesAsync();
        return true;
    }

    // ── Emergency contacts ──────────────────────────────────────────────────────
    // Stored newline-joined in one column; exposed as a list. Blank lines are dropped,
    // each line is trimmed, and the caps are enforced here rather than trusted from the
    // DTO attributes alone, because the CSV import builds participants too.

    internal static string? JoinContacts(IEnumerable<string>? contacts)
    {
        if (contacts is null) return null;
        var lines = contacts
            .Select(c => (c ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim())
            .Where(c => c.Length > 0)
            .ToList();
        if (lines.Count > ParticipantLimits.EmergencyContactsMax)
            throw new ArgumentException($"At most {ParticipantLimits.EmergencyContactsMax} emergency contacts can be recorded.");
        if (lines.Any(c => c.Length > ParticipantLimits.EmergencyContactMaxLength))
            throw new ArgumentException($"Each emergency contact must be {ParticipantLimits.EmergencyContactMaxLength} characters or fewer.");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    internal static List<string> SplitContacts(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? new List<string>()
            : stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static ParticipantSummaryDto ToSummary(
        Participant p,
        Dictionary<Guid, string> programMap,
        Dictionary<Guid, string>? slugMap = null,
        Dictionary<Guid, int>? pctMap = null) =>
        new()
        {
            Id = p.Id,
            FullName = p.FullName,
            PreferredName = p.PreferredName,
            Initials = p.Initials,
            Status = p.Status,
            ProgramId = p.ProgramId,
            ProgramName = programMap.GetValueOrDefault(p.ProgramId, string.Empty),
            ProgramSlug = slugMap?.GetValueOrDefault(p.ProgramId, string.Empty) ?? string.Empty,
            AttendancePct = pctMap?.GetValueOrDefault(p.Id, 0) ?? p.AttendancePct,
            StartDate = p.StartDate.ToString("yyyy-MM-dd"),
            HasDocAlerts = DocumentAlerts.For(p),
            BirthYear = p.BirthYear,
            ServiceCoordinator = p.ServiceCoordinator,
            GuardianName = p.GuardianName,
            GuardianPhone = p.GuardianPhone,
            GuardianEmail = p.GuardianEmail,
            ReferralSource = p.ReferralSource,
            TShirtSize = p.TShirtSize,
            IntakeNotes = p.IntakeNotes,
            AuthorizationExpiry = p.AuthorizationExpiry?.ToString("yyyy-MM-dd"),
            IppExpiry = p.IppExpiry?.ToString("yyyy-MM-dd"),
            DateOfBirth = p.DateOfBirth?.ToString("yyyy-MM-dd"),
            Allergies = p.Allergies,
            AllergyAnaphylactic = p.AllergyAnaphylactic,
            AreasOfConcern = p.AreasOfConcern,
            ServiceCoordinatorEmail = p.ServiceCoordinatorEmail,
            ServiceCoordinatorPhone = p.ServiceCoordinatorPhone,
            ContactInRemind = p.ContactInRemind,
            IntakeDocsSubmitted = p.IntakeDocsSubmitted,
            HasHighSchoolDiploma = p.HasHighSchoolDiploma,
            EmergencyContacts = SplitContacts(p.EmergencyContacts),
            IsSdpClient = p.IsSdpClient,
            SdpFmsName = p.SdpFmsName,
            SdpIndependentFacilitator = p.SdpIndependentFacilitator,
            SdpStartDate = p.SdpStartDate?.ToString("yyyy-MM-dd"),
            SecondaryProgramId = p.SecondaryProgramId,
            SecondaryProgramName = p.SecondaryProgramId is { } sid2 ? programMap.GetValueOrDefault(sid2) : null,
            SecondaryProgramSlug = p.SecondaryProgramId is { } sid3 ? slugMap?.GetValueOrDefault(sid3) : null,
        };
}
