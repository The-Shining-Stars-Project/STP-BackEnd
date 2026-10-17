using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Oct 2026: Cohort Roll-Up showed Pathways pillars under Part-time programs. Both frameworks
/// number their sections from 1, so the roll-up must keep only the skills of the program's
/// Track (Track lives on the objective area) and order rows by track and area, not by section.
/// The slug never decides: production slugs are hand-made ("pathways:-manteca").
/// </summary>
public class CohortTrackFilterTests
{
    private const string Month = "2026-10";

    private static readonly Guid PtProgram = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid PwProgram = Guid.Parse("a1000000-0000-0000-0000-000000000002");

    private static readonly Guid PtSocial = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    private static readonly Guid PtComm = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    private static readonly Guid PtMulti = Guid.Parse("b1000000-0000-0000-0000-000000000006");
    private static readonly Guid PwCollab = Guid.Parse("b1000000-0000-0000-0000-000000000101");
    private static readonly Guid PwTheater = Guid.Parse("b1000000-0000-0000-0000-000000000105");

    // Part-time: Social (section 1), Communication (section 2).
    private static readonly Guid TurnTaking = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid EyeContact = Guid.Parse("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid Projection = Guid.Parse("c1000000-0000-0000-0000-000000000003");
    // Pathways: Collaborative Social Skills (section 1, colliding with Part-time), Theater (section 5).
    private static readonly Guid Ensemble = Guid.Parse("c1000000-0000-0000-0000-000000000101");
    private static readonly Guid Regulation = Guid.Parse("c1000000-0000-0000-0000-000000000102");
    private static readonly Guid RolePlay = Guid.Parse("c1000000-0000-0000-0000-000000000103");

    private static readonly Guid[] PartTimeSkills = [TurnTaking, EyeContact, Projection];
    private static readonly Guid[] PathwaysSkills = [Ensemble, Regulation, RolePlay];

    private readonly FakeUnitOfWork _uow = new();

    public CohortTrackFilterTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = PtProgram, Name = "Manteca PT", Slug = "manteca-pt", Track = ProgramTrack.PartTime },
            new CrmProgram { Id = PwProgram, Name = "Pathways: Manteca", Slug = "pathways:-manteca", Track = ProgramTrack.Pathways },
        ]);

        // Inserted Pathways-first and out of order, so the result order has to come from the service.
        ((FakeRepository<ObjectiveArea>)_uow.ObjectiveAreas).Items.AddRange([
            new ObjectiveArea { Id = PwTheater, Name = "Theater & Acting", SortOrder = 104, Track = ProgramTrack.Pathways },
            new ObjectiveArea { Id = PwCollab, Name = "Collaborative Social Skills", SortOrder = 100, Track = ProgramTrack.Pathways },
            new ObjectiveArea { Id = PtMulti, Name = "Multi-Area", SortOrder = 6, Track = ProgramTrack.PartTime },
            new ObjectiveArea { Id = PtComm, Name = "Communication Goals", SortOrder = 2, Track = ProgramTrack.PartTime },
            new ObjectiveArea { Id = PtSocial, Name = "Social Skill Development", SortOrder = 1, Track = ProgramTrack.PartTime },
        ]);

        // Under the old section-then-sort order these interleaved: Turn-taking, Ensemble, Eye
        // contact, Regulation, Projection, Role-play.
        _uow.SubSkillsRepo.Items.AddRange([
            new SubSkill { Id = RolePlay, ObjectiveAreaId = PwTheater, Name = "Role-play", SectionNumber = 5, SortOrder = 1 },
            new SubSkill { Id = Regulation, ObjectiveAreaId = PwCollab, Name = "Emotional regulation", SectionNumber = 1, SortOrder = 2 },
            new SubSkill { Id = Ensemble, ObjectiveAreaId = PwCollab, Name = "Ensemble participation", SectionNumber = 1, SortOrder = 1 },
            new SubSkill { Id = Projection, ObjectiveAreaId = PtComm, Name = "Projection", SectionNumber = 2, SortOrder = 1 },
            new SubSkill { Id = EyeContact, ObjectiveAreaId = PtSocial, Name = "Eye contact", SectionNumber = 1, SortOrder = 2 },
            new SubSkill { Id = TurnTaking, ObjectiveAreaId = PtSocial, Name = "Turn-taking", SectionNumber = 1, SortOrder = 1 },
        ]);

        _uow.ScoreThresholdsRepo.Items.AddRange([
            new ScoreThreshold { Level = ProgressLevel.Novice, MinAverage = 0.0 },
            new ScoreThreshold { Level = ProgressLevel.Intermediate, MinAverage = 1.5 },
            new ScoreThreshold { Level = ProgressLevel.Expert, MinAverage = 2.5 },
        ]);
    }

    private CohortRollUpService Admin() => new(_uow, new FakeAllowAllAccess(_uow));

    private CohortRollUpService TeacherOf(params Guid[] programIds) =>
        new(_uow, new AssignedOnlyAccess(programIds.ToHashSet()));

    private Guid AddStar(string name, Guid programId, Guid? secondary = null)
    {
        var id = Guid.NewGuid();
        _uow.ParticipantsRepo.Items.Add(new Participant
        {
            Id = id, FullName = name, Initials = name[..2].ToUpperInvariant(),
            ProgramId = programId, SecondaryProgramId = secondary,
        });
        return id;
    }

    private Task Score(Guid starId, Guid skillId) =>
        _uow.WeeklyDataEntries.AddAsync(new WeeklyDataEntry
        {
            ParticipantId = starId, SubSkillId = skillId, MonthKey = Month,
            WeekNumber = 1, WeekDate = new DateTime(2026, 10, 5), Score = DataScore.Independent,
        });

    [Fact]
    public async Task A_part_time_program_shows_only_part_time_rows()
    {
        var rollUp = await Admin().GetRollUpAsync(Guid.NewGuid(), Month, PtProgram);

        Assert.Equal(PartTimeSkills, rollUp.Rows.Select(r => r.SubSkillId).ToArray());
        Assert.All(rollUp.Rows, r => Assert.Equal(ProgramTrack.PartTime, r.Track));
    }

    [Fact]
    public async Task A_pathways_program_shows_only_pathways_rows()
    {
        var rollUp = await Admin().GetRollUpAsync(Guid.NewGuid(), Month, PwProgram);

        Assert.Equal(PathwaysSkills, rollUp.Rows.Select(r => r.SubSkillId).ToArray());
        Assert.All(rollUp.Rows, r => Assert.Equal(ProgramTrack.Pathways, r.Track));
    }

    [Fact]
    public async Task All_programs_shows_both_tracks_part_time_first_without_interleaving()
    {
        var rollUp = await Admin().GetRollUpAsync(Guid.NewGuid(), Month, null);

        Assert.Equal([.. PartTimeSkills, .. PathwaysSkills], rollUp.Rows.Select(r => r.SubSkillId).ToArray());

        // Each area's rows form one contiguous block, so the page can group by area id.
        var areaRuns = rollUp.Rows
            .Select(r => r.ObjectiveAreaId)
            .Where((id, i) => i == 0 || id != rollUp.Rows[i - 1].ObjectiveAreaId)
            .ToArray();
        Assert.Equal([PtSocial, PtComm, PwCollab, PwTheater], areaRuns);
    }

    [Fact]
    public async Task Rows_carry_their_objective_area_id_and_track()
    {
        var rollUp = await Admin().GetRollUpAsync(Guid.NewGuid(), Month, null);

        var pt = rollUp.Rows.Single(r => r.SubSkillId == TurnTaking);
        Assert.Equal(PtSocial, pt.ObjectiveAreaId);
        Assert.Equal(ProgramTrack.PartTime, pt.Track);
        Assert.Equal("Social Skill Development", pt.ObjectiveAreaName);

        var pw = rollUp.Rows.Single(r => r.SubSkillId == Ensemble);
        Assert.Equal(PwCollab, pw.ObjectiveAreaId);
        Assert.Equal(ProgramTrack.Pathways, pw.Track);
        Assert.Equal(pt.SectionNumber, pw.SectionNumber); // same section number, different area
    }

    [Fact]
    public async Task All_programs_for_a_part_time_teacher_shows_only_part_time_rows()
    {
        var rollUp = await TeacherOf(PtProgram).GetRollUpAsync(Guid.NewGuid(), Month, null);

        Assert.Equal(PartTimeSkills, rollUp.Rows.Select(r => r.SubSkillId).ToArray());
    }

    [Fact]
    public async Task The_track_comes_from_the_program_record_not_its_slug()
    {
        // Named and slugged like Pathways, but set to Part-time on the Programs page.
        var misleading = Guid.NewGuid();
        _uow.ProgramsRepo.Items.Add(new CrmProgram
        {
            Id = misleading, Name = "Pathways Prep", Slug = "pathways", Track = ProgramTrack.PartTime,
        });

        var rollUp = await Admin().GetRollUpAsync(Guid.NewGuid(), Month, misleading);

        Assert.Equal(PartTimeSkills, rollUp.Rows.Select(r => r.SubSkillId).ToArray());
    }

    [Fact]
    public async Task Scores_on_the_other_tracks_skills_are_not_counted_or_listed()
    {
        // Part-time primary, Pathways secondary: Weekly Data lets Pathways skills be scored
        // in the star's Pathways group, but the Part-time roll-up has no row for them.
        var star = AddStar("Chris Quiwa", PtProgram, secondary: PwProgram);
        await Score(star, Ensemble);

        var service = Admin();
        var pt = await service.GetRollUpAsync(Guid.NewGuid(), Month, PtProgram);
        Assert.Equal(0, pt.ParticipantCount);
        Assert.Empty(await service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, Ensemble, ProgressLevel.Expert, PtProgram));

        // "All programs" includes the Pathways framework, so the same score shows there.
        var all = await service.GetRollUpAsync(Guid.NewGuid(), Month, null);
        Assert.Equal(1, all.Rows.Single(r => r.SubSkillId == Ensemble).ExpertCount);
        Assert.Equal(1, all.ParticipantCount);
        Assert.Single(await service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, Ensemble, ProgressLevel.Expert, null));
    }

    /// <summary>A non-admin scope: only the given programs.</summary>
    private sealed class AssignedOnlyAccess(IReadOnlySet<Guid> programIds) : IProgramAccessService
    {
        public Task<ProgramAccess> ForUserAsync(Guid userId) =>
            Task.FromResult(new ProgramAccess(false, programIds));

        public Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId) =>
            throw new NotSupportedException("The roll-up never loads a single star.");
    }
}
