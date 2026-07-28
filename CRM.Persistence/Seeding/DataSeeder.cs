using CRM.Application.Interfaces;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CRM.Persistence.Seeding;

public static class DataSeeder
{
    /// <summary>
    /// Seeds the single administrator login (email <c>admin@shiningstars.org</c>, password
    /// <c>ChangeMe!123</c>). This is the only account created — the CRM is administered from
    /// here, and additional users are added through the app. Idempotent: no-ops if the account
    /// already exists. Email is stored lower-case because login normalises to lower-case.
    /// </summary>
    public static async Task SeedAdminUserAsync(AppDbContext db, IPasswordHasher hasher)
    {
        if (await db.Users.AnyAsync(u => u.Email == "admin@shiningstars.org")) return;

        var (hash, salt) = hasher.HashPassword("ChangeMe!123");
        db.Users.Add(new User
        {
            Email = "admin@shiningstars.org",
            FullName = "Site Administrator",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Admin,
            IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the master onboarding checklist template (copied onto each new staff
    /// member at creation). Reference data, seeded in every environment; idempotent —
    /// no-ops once any template item exists so admin edits are never overwritten.
    /// </summary>
    public static async Task SeedChecklistTemplateAsync(AppDbContext db)
    {
        if (await db.ChecklistTemplateItems.AnyAsync()) return;

        var defaults = new (string Section, string Label)[]
        {
            ("HR & Compliance",     "W-4 / I-9 completed"),
            ("HR & Compliance",     "Background check cleared"),
            ("HR & Compliance",     "Emergency contact form submitted"),
            ("Training",            "Program overview training"),
            ("Training",            "Child safety & mandated reporter training"),
            ("Training",            "First aid / CPR certification"),
            ("Program Requirements","Liability waiver signed"),
            ("Program Requirements","Code of conduct acknowledged"),
            ("Program Requirements","Media release policy reviewed"),
            ("Access & Setup",      "Staff email account created"),
            ("Access & Setup",      "Program schedule provided"),
            ("Access & Setup",      "Participant roster access granted"),
        };

        db.ChecklistTemplateItems.AddRange(defaults.Select((d, i) => new ChecklistTemplateItem
        {
            Section = d.Section,
            Label = d.Label,
            SortOrder = i,
        }));
        await db.SaveChangesAsync();
    }

    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Programs.AnyAsync()) return;

        var today = DateTime.UtcNow.Date;
        // Deterministic PRNG so a re-seed produces the same "history" every time.
        var rng = new Random(20260717);

        // ── Programs ──────────────────────────────────────────────────────────
        // Slugs are fixed: the frontend derives per-program theme colours from them
        // (var(--mjc), var(--pathways), var(--manteca)), so these three must not change.

        var mjc = new CrmProgram
        {
            Name = "MJC", Slug = "mjc", ColorHex = "#378add",
            SessionSchedule = "Mon / Tue / Wed", DefaultLocation = "MJC East Campus",
            MeetingDays = MeetingDays.Monday | MeetingDays.Tuesday | MeetingDays.Wednesday,
            StartTime = new TimeOnly(16, 0), EndTime = new TimeOnly(18, 0),
        };
        var pathways = new CrmProgram
        {
            Name = "Pathways", Slug = "pathways", ColorHex = "#1d9e75",
            SessionSchedule = "Mon – Fri", DefaultLocation = "Manteca",
            MeetingDays = MeetingDays.Monday | MeetingDays.Tuesday | MeetingDays.Wednesday
                        | MeetingDays.Thursday | MeetingDays.Friday,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(15, 0),
        };
        var manteca = new CrmProgram
        {
            Name = "Manteca PT", Slug = "manteca", ColorHex = "#d85a30",
            SessionSchedule = "Mon / Tue / Wed", DefaultLocation = "St. Paul's",
            MeetingDays = MeetingDays.Monday | MeetingDays.Tuesday | MeetingDays.Wednesday,
            StartTime = new TimeOnly(16, 30), EndTime = new TimeOnly(18, 30),
        };
        db.Programs.AddRange(mjc, pathways, manteca);

        // ── Staff ─────────────────────────────────────────────────────────────

        var rachel = new StaffMember { FullName = "Rachel Mendez",  Initials = "RM", Role = StaffRole.Coordinator, StartDate = new DateTime(2022, 1, 15), OnboardingProgressPct = 100 };
        var devon  = new StaffMember { FullName = "Devon Pierce",   Initials = "DP", Role = StaffRole.Teacher,     StartDate = new DateTime(2022, 3, 1),  OnboardingProgressPct = 100 };
        var nina   = new StaffMember { FullName = "Nina Schaefer",  Initials = "NS", Role = StaffRole.Teacher,     StartDate = new DateTime(2023, 1, 10), OnboardingProgressPct = 100 };
        var alex   = new StaffMember { FullName = "Alex Tran",      Initials = "AX", Role = StaffRole.Coordinator, StartDate = new DateTime(2023, 6, 1),  OnboardingProgressPct = 100 };
        var maya   = new StaffMember { FullName = "Maya Lindqvist", Initials = "ML", Role = StaffRole.Teacher,     StartDate = new DateTime(2023, 9, 1),  OnboardingProgressPct = 100 };
        var priya  = new StaffMember { FullName = "Priya Desai",    Initials = "PD", Role = StaffRole.Teacher,     StartDate = new DateTime(2025, 1, 20), OnboardingProgressPct = 100 };
        var joss   = new StaffMember { FullName = "Joss Kimura",    Initials = "JK", Role = StaffRole.Coordinator, StartDate = new DateTime(2026, 4, 6),  OnboardingProgressPct = 70  };
        var marcus = new StaffMember { FullName = "Marcus Webb",    Initials = "MW", Role = StaffRole.Teacher,     StartDate = new DateTime(2026, 6, 15), OnboardingProgressPct = 40  };
        var allStaff = new[] { rachel, devon, nina, alex, maya, priya, joss, marcus };
        db.Staff.AddRange(allStaff);

        await db.SaveChangesAsync(); // programs + staff now have Ids

        // ── Participants ──────────────────────────────────────────────────────
        // Each row carries an attendance "reliability" (probability present) used to
        // generate a realistic ledger below, and an optional "left N weeks ago" for
        // Former Stars who stopped attending. Prospective Stars have no attendance yet.

        var ptMeta = new List<(Participant P, CrmProgram Prog, double Reliability, int? LeftWeeksAgo)>();
        Participant Pt(string name, string initials, CrmProgram prog, ParticipantStatus status,
            int birthYear, string sc, DateTime start, double reliability, int? leftWeeksAgo = null)
        {
            var p = new Participant
            {
                FullName = name, Initials = initials, ProgramId = prog.Id, Status = status,
                BirthYear = birthYear, ServiceCoordinator = sc, StartDate = start, AttendancePct = 0,
            };
            db.Participants.Add(p);
            ptMeta.Add((p, prog, reliability, leftWeeksAgo));
            return p;
        }

        // MJC
        Pt("Kezia Morales",   "KM", mjc, ParticipantStatus.Active,      2005, "D. Kwan",    new DateTime(2023, 9, 1),  0.96);
        Pt("Eli Hart",        "EH", mjc, ParticipantStatus.Active,      2004, "D. Kwan",    new DateTime(2023, 8, 1),  0.92);
        Pt("Nina Ruiz",       "NR", mjc, ParticipantStatus.Active,      2006, "D. Kwan",    new DateTime(2024, 1, 8),  0.90);
        Pt("Leo Walsh",       "LW", mjc, ParticipantStatus.Active,      2003, "D. Kwan",    new DateTime(2023, 3, 6),  0.83);
        Pt("Hannah Boyd",     "HB", mjc, ParticipantStatus.Active,      2007, "M. Flores",  new DateTime(2024, 9, 3),  0.94);
        Pt("Isaac Nguyen",    "IN", mjc, ParticipantStatus.Active,      2005, "M. Flores",  new DateTime(2024, 2, 5),  0.88);
        Pt("Grace Bello",     "GB", mjc, ParticipantStatus.Active,      2008, "D. Kwan",    new DateTime(2025, 1, 14), 0.91);
        Pt("Marcus Tildon",   "MT", mjc, ParticipantStatus.Attention,   2002, "D. Kwan",    new DateTime(2022, 3, 1),  0.63);
        Pt("Sofia Reyes",     "SR", mjc, ParticipantStatus.Attention,   2009, "M. Flores",  new DateTime(2026, 5, 4),  0.68);
        Pt("Aaron Cortez",    "AC", mjc, ParticipantStatus.Prospective, 2010, "D. Kwan",    today.AddDays(21),         0.00);
        Pt("Delia Prince",    "DP", mjc, ParticipantStatus.Former,      2001, "D. Kwan",    new DateTime(2022, 9, 12), 0.74, leftWeeksAgo: 6);

        // Pathways
        Pt("Aiden Cole",      "AC", pathways, ParticipantStatus.Active,      2004, "R. Alvarez", new DateTime(2023, 9, 5),  0.93);
        Pt("Mia Soto",        "MS", pathways, ParticipantStatus.Active,      2005, "R. Alvarez", new DateTime(2022, 9, 6),  0.89);
        Pt("Jasmine Vega",    "JV", pathways, ParticipantStatus.Active,      2006, "R. Alvarez", new DateTime(2023, 1, 10), 0.95);
        Pt("Emma Guerrero",   "EG", pathways, ParticipantStatus.Active,      2003, "R. Alvarez", new DateTime(2022, 9, 6),  0.90);
        Pt("Noah Bright",     "NB", pathways, ParticipantStatus.Active,      2007, "S. Park",    new DateTime(2024, 3, 5),  0.87);
        Pt("Olivia Chen",     "OC", pathways, ParticipantStatus.Active,      2008, "S. Park",    new DateTime(2024, 9, 3),  0.92);
        Pt("Diego Ramirez",   "DR", pathways, ParticipantStatus.Active,      2005, "R. Alvarez", new DateTime(2025, 2, 4),  0.85);
        Pt("Tyler Brooks",    "TB", pathways, ParticipantStatus.Attention,   2002, "R. Alvarez", new DateTime(2024, 1, 9),  0.66);
        Pt("Ruby Sinclair",   "RS", pathways, ParticipantStatus.Prospective, 2009, "S. Park",    today.AddDays(14),         0.00);
        Pt("Owen Fletcher",   "OF", pathways, ParticipantStatus.Former,      2000, "R. Alvarez", new DateTime(2023, 2, 7),  0.70, leftWeeksAgo: 9);

        // Manteca PT
        Pt("Sam Obi",         "SO", manteca, ParticipantStatus.Active,      2004, "T. Cho",     new DateTime(2023, 9, 4),  0.86);
        Pt("Priya Kapoor",    "PK", manteca, ParticipantStatus.Active,      2005, "T. Cho",     new DateTime(2022, 10, 3), 0.88);
        Pt("Jordan Fisk",     "JF", manteca, ParticipantStatus.Active,      2006, "T. Cho",     new DateTime(2023, 6, 5),  0.81);
        Pt("Lucia Marín",     "LM", manteca, ParticipantStatus.Active,      2003, "T. Cho",     new DateTime(2023, 1, 9),  0.84);
        Pt("Caleb Stone",     "CS", manteca, ParticipantStatus.Active,      2007, "B. Ngata",   new DateTime(2024, 4, 8),  0.90);
        Pt("Ava Delgado",     "AD", manteca, ParticipantStatus.Active,      2008, "B. Ngata",   new DateTime(2024, 10, 7), 0.92);
        Pt("Ethan Park",      "EP", manteca, ParticipantStatus.Attention,   2002, "T. Cho",     new DateTime(2023, 3, 6),  0.64);
        Pt("Zoe Hammond",     "ZH", manteca, ParticipantStatus.Prospective, 2010, "B. Ngata",   today.AddDays(10),         0.00);

        await db.SaveChangesAsync(); // participants now have Ids

        // ── Staff-program assignments ─────────────────────────────────────────

        db.StaffProgramAssignments.AddRange(
            new StaffProgramAssignment { StaffMemberId = rachel.Id, ProgramId = mjc.Id      },
            new StaffProgramAssignment { StaffMemberId = devon.Id,  ProgramId = mjc.Id      },
            new StaffProgramAssignment { StaffMemberId = nina.Id,   ProgramId = mjc.Id      },
            new StaffProgramAssignment { StaffMemberId = marcus.Id, ProgramId = mjc.Id      },
            new StaffProgramAssignment { StaffMemberId = maya.Id,   ProgramId = pathways.Id },
            new StaffProgramAssignment { StaffMemberId = joss.Id,   ProgramId = pathways.Id },
            new StaffProgramAssignment { StaffMemberId = priya.Id,  ProgramId = pathways.Id },
            new StaffProgramAssignment { StaffMemberId = devon.Id,  ProgramId = manteca.Id  },
            new StaffProgramAssignment { StaffMemberId = alex.Id,   ProgramId = manteca.Id  },
            new StaffProgramAssignment { StaffMemberId = priya.Id,  ProgramId = manteca.Id  }
        );

        // ── Onboarding checklists (one per staff member) ──────────────────────

        var onboardingItems = new (string Section, string Label)[]
        {
            ("Documents",     "Signed offer letter"),
            ("Documents",     "I-9 / ID verification"),
            ("Documents",     "Background check"),
            ("Documents",     "TB test results"),
            ("Systems Setup", "Email account created"),
            ("Systems Setup", "Google Workspace access"),
            ("Systems Setup", "CRM access granted"),
            ("Training",      "Org orientation complete"),
            ("Training",      "Program handbook review"),
            ("Training",      "First session shadowing"),
        };
        foreach (var s in allStaff)
        {
            var completed = (int)Math.Round(onboardingItems.Length * s.OnboardingProgressPct / 100.0);
            for (var i = 0; i < onboardingItems.Length; i++)
            {
                var done = i < completed;
                db.OnboardingItems.Add(new OnboardingItem
                {
                    StaffMemberId = s.Id,
                    Section = onboardingItems[i].Section,
                    Label = onboardingItems[i].Label,
                    SortOrder = i,
                    IsCompleted = done,
                    CompletedDate = done ? s.StartDate.AddDays(3 + i) : null,
                });
            }
        }

        // ── Sessions (≈11 weeks of history per program, on its meeting days) ──

        var windowStart = today.AddDays(-7 * 11);
        var sessionsByProgram = new Dictionary<Guid, List<Session>>();
        var startLabelByProgram = new Dictionary<Guid, int> { [mjc.Id] = 41, [pathways.Id] = 33, [manteca.Id] = 27 };
        foreach (var prog in new[] { mjc, pathways, manteca })
        {
            var list = new List<Session>();
            var label = startLabelByProgram[prog.Id];
            for (var d = windowStart; d <= today; d = d.AddDays(1))
            {
                if (!MeetsOn(prog, d)) continue;
                var isPast = d < today;
                list.Add(new Session
                {
                    ProgramId = prog.Id,
                    Date = d,
                    Room = prog.DefaultLocation,
                    TimeRange = FormatTimeRange(prog.StartTime, prog.EndTime),
                    Label = $"Session {++label}",
                    Status = isPast ? SessionStatus.Submitted : SessionStatus.Open,
                    SubmittedAt = isPast ? d.AddHours(15) : null,
                });
            }
            sessionsByProgram[prog.Id] = list;
            db.Sessions.AddRange(list);
        }

        await db.SaveChangesAsync(); // sessions now have Ids

        // ── Attendance ledger + a few notes ───────────────────────────────────

        var noteworthy = new List<(AttendanceRecord Rec, string Content, string Type)>();
        foreach (var (p, prog, reliability, leftWeeksAgo) in ptMeta)
        {
            if (p.Status == ParticipantStatus.Prospective) continue; // not attending yet
            var leftDate = leftWeeksAgo is int w ? today.AddDays(-7 * w) : (DateTime?)null;

            foreach (var s in sessionsByProgram[p.ProgramId])
            {
                if (s.Date < p.StartDate) continue;
                if (leftDate is DateTime ld && s.Date > ld) continue;

                AttendanceStatus status;
                if (s.Date == today)
                    // Today's session is still open: mark most, leave a couple unmarked.
                    status = rng.NextDouble() < 0.20 ? AttendanceStatus.Unmarked
                           : rng.NextDouble() < reliability ? AttendanceStatus.Present : AttendanceStatus.Absent;
                else
                    status = rng.NextDouble() < reliability ? AttendanceStatus.Present : AttendanceStatus.Absent;

                var rec = new AttendanceRecord { ParticipantId = p.Id, SessionId = s.Id, Status = status };
                db.AttendanceRecords.Add(rec);

                // Attach a small number of realistic notes on recent absences / highlights.
                var daysAgo = (today - s.Date).Days;
                if (daysAgo is >= 0 and <= 14 && noteworthy.Count < 8)
                {
                    if (status == AttendanceStatus.Absent && p.Status == ParticipantStatus.Attention)
                        noteworthy.Add((rec, $"Second absence this window — follow up with {p.ServiceCoordinator} about supports.", "concern"));
                    else if (status == AttendanceStatus.Present && rng.NextDouble() < 0.06)
                        noteworthy.Add((rec, $"{p.FullName.Split(' ')[0]} led the group warm-up today — big confidence step.", "observation"));
                }
            }
        }

        await db.SaveChangesAsync(); // attendance records now have Ids

        foreach (var (rec, content, type) in noteworthy)
            db.AttendanceNotes.Add(new AttendanceNote { AttendanceRecordId = rec.Id, Content = content, NoteType = type });

        // ── Arts profiles (Student Frame) for active/attention Stars ──────────

        foreach (var (p, prog, _, _) in ptMeta)
        {
            if (p.Status is not (ParticipantStatus.Active or ParticipantStatus.Attention)) continue;
            var first = p.FullName.Split(' ')[0];
            db.ParticipantArtsProfiles.Add(new ParticipantArtsProfile
            {
                ParticipantId = p.Id,
                IppSummary = $"{first} works toward increased independence in group performance settings, with goals in expressive communication and turn-taking.",
                CurrentLevel = p.Status == ParticipantStatus.Attention
                    ? $"{first} engages with 1:1 support and is building tolerance for full-group activities."
                    : $"{first} participates independently in most ensemble activities and initiates peer interaction.",
                TsspArtsGoal = $"By end of term, {first} will lead or co-lead at least one ensemble segment with minimal prompting.",
            });
        }

        // ── Calendar events (recent past + upcoming) ──────────────────────────

        db.CalendarEvents.AddRange(
            // Past
            new CalendarEvent { Title = "Winter Showcase",            Date = today.AddDays(-24), ProgramId = null,        Location = "Main Stage", Meta = "All programs · Families invited",   IsUpcoming = false },
            new CalendarEvent { Title = "Ensemble Auditions",         Date = today.AddDays(-17), ProgramId = mjc.Id,      Location = "FH 152",     Meta = "Spring Show casting",               IsUpcoming = false },
            new CalendarEvent { Title = "Staff Development Day",      Date = today.AddDays(-10), ProgramId = null,        Location = "Room B",     Meta = "Mandated reporter refresher",       IsUpcoming = false },
            new CalendarEvent { Title = "Family Progress Night",      Date = today.AddDays(-4),  ProgramId = pathways.Id, Location = "Room C",     Meta = "Q2 progress reviews",               IsUpcoming = false },
            // Upcoming
            new CalendarEvent { Title = "Spring Showcase Rehearsal",  Date = today.AddDays(2),   ProgramId = mjc.Id,      Location = "Main Stage", Meta = "Full cast · Rachel Mendez",         TimeRange = "9:00–11:30 AM",  IsUpcoming = true },
            new CalendarEvent { Title = "Script Reading",             Date = today.AddDays(5),   ProgramId = manteca.Id,  Location = "Gym B",      Meta = "Devon Pierce",                      TimeRange = "1:00–3:00 PM",   IsUpcoming = true },
            new CalendarEvent { Title = "End-of-term Assessment",     Date = today.AddDays(8),   ProgramId = mjc.Id,      Location = "Room B",     Meta = "Rachel Mendez",                     IsUpcoming = true },
            new CalendarEvent { Title = "Regional Theatre Workshop",  Date = today.AddDays(9),   ProgramId = pathways.Id, Location = "Off-site",   Meta = "All Pathways Stars · Bus 9 AM",     TimeRange = "9:00 AM–2:00 PM", IsUpcoming = true },
            new CalendarEvent { Title = "Mid-term Check-in",          Date = today.AddDays(12),  ProgramId = pathways.Id, Location = "Room C",     Meta = "Joss Kimura",                       IsUpcoming = true },
            new CalendarEvent { Title = "Parent Observation",         Date = today.AddDays(13),  ProgramId = manteca.Id,  Location = "Gym B",      Meta = "All families welcome",              IsUpcoming = true },
            new CalendarEvent { Title = "Spring Showcase (Matinee)",  Date = today.AddDays(26),  ProgramId = null,        Location = "Main Stage", Meta = "All programs · Public performance",  TimeRange = "2:00–4:00 PM",   IsUpcoming = true }
        );

        // ── Projects & Tasks ──────────────────────────────────────────────────

        var springShow = new Project { Title = "Spring Show 2026",      Type = ProjectType.Production, Status = "inprogress", Scope = "Full musical production across all three programs", DueDate = today.AddDays(26) };
        var summerCamp = new Project { Title = "Summer Intensive 2026",  Type = ProjectType.Event,      Status = "planning",   Scope = "Two-week performing-arts intensive", DueDate = today.AddDays(70) };
        var onboarding = new Project { Title = "New Staff Onboarding",   Type = ProjectType.Staff,      Status = "inprogress", Scope = "Q2–Q3 2026 hires (Joss, Marcus)" };
        var compliance = new Project { Title = "Compliance Renewals",    Type = ProjectType.Admin,      Status = "inprogress", Scope = "Annual document + certification audit", DueDate = today.AddDays(20) };
        var enrollment = new Project { Title = "Fall Enrollment Drive",  Type = ProjectType.Admin,      Status = "planning",   Scope = "Outreach + intake for Fall cohort", DueDate = today.AddDays(55) };
        var showcaseAv = new Project { Title = "Showcase A/V + Programs", Type = ProjectType.Production, Status = "planning",   Scope = "Printed programs, lighting, and recording" };
        db.Projects.AddRange(springShow, summerCamp, onboarding, compliance, enrollment, showcaseAv);

        await db.SaveChangesAsync(); // projects now have Ids

        db.Tasks.AddRange(
            new ProjectTask { ProjectId = springShow.Id, Name = "Finalize script selection",       Status = Domain.Enums.TaskStatus.Done,       Priority = TaskPriority.High,   AssignedToId = rachel.Id },
            new ProjectTask { ProjectId = springShow.Id, Name = "Cast all roles",                   Status = Domain.Enums.TaskStatus.Done,       Priority = TaskPriority.High,   AssignedToId = devon.Id },
            new ProjectTask { ProjectId = springShow.Id, Name = "Block Act I choreography",         Status = Domain.Enums.TaskStatus.InProgress, Priority = TaskPriority.High,   AssignedToId = maya.Id,   DueDate = today.AddDays(6) },
            new ProjectTask { ProjectId = springShow.Id, Name = "Reserve main stage venue",         Status = Domain.Enums.TaskStatus.InProgress, Priority = TaskPriority.Medium, AssignedToId = rachel.Id, DueDate = today.AddDays(3) },
            new ProjectTask { ProjectId = springShow.Id, Name = "Costume fittings",                 Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Medium, AssignedToId = nina.Id,   DueDate = today.AddDays(12) },
            new ProjectTask { ProjectId = springShow.Id, Name = "Print performance programs",       Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Low,    AssignedToId = alex.Id,   DueDate = today.AddDays(20) },

            new ProjectTask { ProjectId = summerCamp.Id, Name = "Draft two-week curriculum",        Status = Domain.Enums.TaskStatus.InProgress, Priority = TaskPriority.Medium, AssignedToId = priya.Id,  DueDate = today.AddDays(24) },
            new ProjectTask { ProjectId = summerCamp.Id, Name = "Confirm facility availability",    Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Medium, AssignedToId = alex.Id,   DueDate = today.AddDays(30) },
            new ProjectTask { ProjectId = summerCamp.Id, Name = "Open registration form",          Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Low,    AssignedToId = rachel.Id, DueDate = today.AddDays(38) },

            new ProjectTask { ProjectId = onboarding.Id, Name = "Complete CRM access for Joss",     Status = Domain.Enums.TaskStatus.Done,       Priority = TaskPriority.High,   AssignedToId = rachel.Id },
            new ProjectTask { ProjectId = onboarding.Id, Name = "Schedule Marcus orientation",      Status = Domain.Enums.TaskStatus.InProgress, Priority = TaskPriority.High,   AssignedToId = joss.Id,   DueDate = today.AddDays(4) },
            new ProjectTask { ProjectId = onboarding.Id, Name = "First-session shadowing (Marcus)", Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Medium, AssignedToId = devon.Id,  DueDate = today.AddDays(9) },

            new ProjectTask { ProjectId = compliance.Id, Name = "Audit participant document expiry", Status = Domain.Enums.TaskStatus.InProgress, Priority = TaskPriority.High,  AssignedToId = alex.Id,   DueDate = today.AddDays(2) },
            new ProjectTask { ProjectId = compliance.Id, Name = "Renew staff CPR certifications",    Status = Domain.Enums.TaskStatus.Overdue,    Priority = TaskPriority.High,  AssignedToId = rachel.Id, DueDate = today.AddDays(-3), IsOverdue = true },
            new ProjectTask { ProjectId = compliance.Id, Name = "File Q2 attendance report",         Status = Domain.Enums.TaskStatus.Done,       Priority = TaskPriority.Medium, AssignedToId = rachel.Id },

            new ProjectTask { ProjectId = enrollment.Id, Name = "Update program one-pagers",         Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Medium, AssignedToId = priya.Id,  DueDate = today.AddDays(28) },
            new ProjectTask { ProjectId = enrollment.Id, Name = "Coordinate with service agencies",  Status = Domain.Enums.TaskStatus.Blocked,    Priority = TaskPriority.Medium, AssignedToId = alex.Id,   DueDate = today.AddDays(34) },

            new ProjectTask { ProjectId = showcaseAv.Id, Name = "Book lighting + sound tech",        Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Medium, AssignedToId = devon.Id,  DueDate = today.AddDays(15) },
            new ProjectTask { ProjectId = showcaseAv.Id, Name = "Arrange performance recording",     Status = Domain.Enums.TaskStatus.Upcoming,   Priority = TaskPriority.Low,    AssignedToId = marcus.Id, DueDate = today.AddDays(18) }
        );

        await db.SaveChangesAsync();
    }

    private static bool MeetsOn(CrmProgram program, DateTime date)
    {
        var flag = date.DayOfWeek switch
        {
            DayOfWeek.Sunday    => MeetingDays.Sunday,
            DayOfWeek.Monday    => MeetingDays.Monday,
            DayOfWeek.Tuesday   => MeetingDays.Tuesday,
            DayOfWeek.Wednesday => MeetingDays.Wednesday,
            DayOfWeek.Thursday  => MeetingDays.Thursday,
            DayOfWeek.Friday    => MeetingDays.Friday,
            DayOfWeek.Saturday  => MeetingDays.Saturday,
            _                   => MeetingDays.None,
        };
        return program.MeetingDays.HasFlag(flag);
    }

    private static string? FormatTimeRange(TimeOnly? start, TimeOnly? end) =>
        start is { } s && end is { } e ? $"{s:h:mm tt}–{e:h:mm tt}" : null;

    /// <summary>
    /// Seeds sample attendance for TODAY so the attendance page and dashboard show realistic
    /// activity: a session per program with most participants marked Present (every 4th Absent)
    /// and a couple of notes. Idempotent — skips entirely once any attendance note exists.
    /// </summary>
    public static async Task SeedSampleAttendanceAsync(AppDbContext db)
    {
        if (await db.AttendanceNotes.AnyAsync()) return;

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var programs = await db.Programs.ToListAsync();

        foreach (var program in programs)
        {
            var active = await db.Participants
                .Where(p => p.ProgramId == program.Id && p.Status == ParticipantStatus.Active)
                .ToListAsync();
            if (active.Count == 0) continue;

            // Get-or-create today's session for this program.
            var session = await db.Sessions
                .FirstOrDefaultAsync(s => s.ProgramId == program.Id && s.Date >= today && s.Date < tomorrow);
            if (session is null)
            {
                session = new Session
                {
                    ProgramId = program.Id,
                    Date = today,
                    Room = program.DefaultLocation,
                    TimeRange = program.StartTime is { } st && program.EndTime is { } et
                        ? $"{st:h:mm tt}–{et:h:mm tt}"
                        : null,
                    Status = SessionStatus.Open,
                };
                db.Sessions.Add(session);
                await db.SaveChangesAsync();
            }

            // Ensure a record per active participant.
            var existing = (await db.AttendanceRecords.Where(r => r.SessionId == session.Id).ToListAsync())
                .ToDictionary(r => r.ParticipantId);
            var records = new List<AttendanceRecord>();
            foreach (var p in active)
            {
                if (!existing.TryGetValue(p.Id, out var rec))
                {
                    rec = new AttendanceRecord { ParticipantId = p.Id, SessionId = session.Id, Status = AttendanceStatus.Unmarked };
                    db.AttendanceRecords.Add(rec);
                }
                records.Add(rec);
            }
            await db.SaveChangesAsync();

            // Mark a realistic spread: every 4th Absent, the rest Present.
            for (var i = 0; i < records.Count; i++)
                records[i].Status = (i % 4 == 3) ? AttendanceStatus.Absent : AttendanceStatus.Present;

            // A couple of sample notes on the first participants.
            if (records.Count > 0)
                db.AttendanceNotes.Add(new AttendanceNote
                {
                    AttendanceRecordId = records[0].Id,
                    Content = $"Great energy in {program.Name} warm-ups today.",
                    NoteType = "observation",
                });
            if (records.Count > 1)
                db.AttendanceNotes.Add(new AttendanceNote
                {
                    AttendanceRecordId = records[1].Id,
                    Content = "Arrived late — follow up with family about transportation.",
                    NoteType = "concern",
                });

            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds the demo Script Library so the Scripts page has content in every environment
    /// (explicit client request — the Scripts page is kept as a working UI demo, #18).
    /// Idempotent — skips entirely once any script exists. Links each script to the programs
    /// it names, when those programs exist (matched by slug); the "productions" tag has no
    /// backing program and is simply not linked.
    /// </summary>
    public static async Task SeedScriptsAsync(AppDbContext db)
    {
        if (await db.Scripts.AnyAsync()) return;

        var programIdBySlug = await db.Programs.ToDictionaryAsync(p => p.Slug, p => p.Id);

        // (Title, Subtitle, Type, Status, IsOriginal, IsAdapted, CastMin, CastMax, Duration, ProgramSlugs)
        var rows = new (string Title, string? Subtitle, ScriptType Type, ScriptStatus Status,
            bool IsOriginal, bool IsAdapted, int? CastMin, int? CastMax, string? Duration, string[] Slugs)[]
        {
            ("The Magic Garden", "An original musical in two acts", ScriptType.Musical, ScriptStatus.Active, true, false, 12, 18, "55 min", new[] { "pathways" }),
            ("Cinderella", "Adapted for performing arts", ScriptType.Play, ScriptStatus.Active, false, true, 8, 12, "40 min", new[] { "mjc" }),
            ("Under the Sea", "A musical celebration", ScriptType.Musical, ScriptStatus.Active, true, false, 6, 10, "35 min", new[] { "manteca" }),
            ("The Brave Little Star", "Scene collection — one act per program group", ScriptType.Scene, ScriptStatus.Active, true, false, 4, 8, "20 min / scene", new[] { "mjc", "pathways", "manteca" }),
            ("Stardust", "New original musical — in development", ScriptType.Musical, ScriptStatus.Draft, true, false, null, null, "TBD", Array.Empty<string>()),
            ("A Midsummer Dream", "Adapted from Shakespeare", ScriptType.Play, ScriptStatus.Archived, false, true, 15, 20, "60 min", Array.Empty<string>()),
            ("Rainbow Road", "A musical journey", ScriptType.Musical, ScriptStatus.Archived, true, false, 10, 14, "45 min", new[] { "mjc", "pathways" }),
            ("The Lighthouse Keeper", null, ScriptType.Play, ScriptStatus.Archived, true, false, 6, 8, "30 min", new[] { "manteca" }),
        };

        foreach (var row in rows)
        {
            var script = new Script
            {
                Title = row.Title,
                Subtitle = row.Subtitle,
                Type = row.Type,
                Status = row.Status,
                IsOriginal = row.IsOriginal,
                IsAdapted = row.IsAdapted,
                CastMin = row.CastMin,
                CastMax = row.CastMax,
                Duration = row.Duration,
            };
            db.Scripts.Add(script);

            foreach (var slug in row.Slugs)
            {
                if (programIdBySlug.TryGetValue(slug, out var programId))
                    db.ScriptPrograms.Add(new ScriptProgram { Script = script, ProgramId = programId });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the Games Library (~57 games from the Annual Programming Calendar), tagging each
    /// to the shared taxonomy by slug. Idempotent — skips entirely once any game exists. The
    /// taxonomy (ObjectiveAreas + SubSkills) is seeded by the AddSkillsTaxonomy migration, so it
    /// is present by the time this runs; if it somehow isn't, this no-ops rather than crashing.
    /// </summary>
    public static async Task SeedGamesLibraryAsync(AppDbContext db)
    {
        if (await db.Games.AnyAsync()) return;

        var areaBySlug = await db.ObjectiveAreas.ToDictionaryAsync(a => a.Slug, a => a.Id);
        var subBySlug = await db.SubSkills.ToDictionaryAsync(s => s.Slug, s => s.Id);
        if (areaBySlug.Count == 0 || subBySlug.Count == 0) return; // taxonomy not seeded yet

        foreach (var row in GameSeedData.Games)
        {
            if (!areaBySlug.TryGetValue(row.AreaSlug, out var areaId)) continue;

            var game = new Game
            {
                Name = row.Name,
                Source = row.Source,
                Category = row.Category,
                CategoryLabel = row.CategoryLabel,
                Tiers = row.Tiers,
                PrimaryObjectiveAreaId = areaId,
                Description = row.Description,
                BestForVariations = row.BestFor,
                WhenToUse = row.WhenToUse,
            };
            db.Games.Add(game);

            var order = 0;
            foreach (var (slug, isPrimary) in row.SubGoals)
            {
                if (!subBySlug.TryGetValue(slug, out var subId)) continue;
                db.GameSubGoals.Add(new GameSubGoal
                {
                    GameId = game.Id,
                    SubSkillId = subId,
                    IsPrimary = isPrimary,
                    SortOrder = order++,
                });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the current-quarter roster: each active/attention Star placed on a Site and in a
    /// Star Group, with an assigned staff lead. Uses the Sites/StarGroups seeded by migration.
    /// Idempotent — no-ops once any roster assignment exists.
    /// </summary>
    public static async Task SeedRosterAsync(AppDbContext db)
    {
        if (await db.RosterAssignments.AnyAsync()) return;

        var sites = await db.Sites.OrderBy(s => s.SortOrder).ToListAsync();
        var groups = await db.StarGroups.OrderBy(g => g.SortOrder).ToListAsync();
        if (sites.Count == 0 || groups.Count == 0) return; // reference data missing

        var staff = await db.Staff.OrderBy(s => s.FullName).ToListAsync();
        var participants = await db.Participants
            .Where(p => p.Status == ParticipantStatus.Active || p.Status == ParticipantStatus.Attention)
            .OrderBy(p => p.FullName)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var quarter = (now.Month - 1) / 3 + 1;

        var i = 0;
        foreach (var p in participants)
        {
            db.RosterAssignments.Add(new RosterAssignment
            {
                ParticipantId = p.Id,
                SiteId = sites[i % sites.Count].Id,
                StarGroupId = groups[i % groups.Count].Id,
                AssignedStaffId = staff.Count > 0 ? staff[i % staff.Count].Id : null,
                Quarter = quarter,
                Year = now.Year,
                CountedInRatio = true,
            });
            i++;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a slice of the weekly-data tracker so the Weekly Data, Per-Star Planning, and
    /// Cohort Roll-Up pages have real content: focus skills + weekly scores for the previous and
    /// current month, confirmed month-end snapshots and summaries for the previous month, and
    /// per-Star plans for the current month. Idempotent — no-ops once any weekly entry exists.
    /// </summary>
    public static async Task SeedTrackerAsync(AppDbContext db)
    {
        if (await db.WeeklyDataEntries.AnyAsync()) return;

        var subSkills = await db.SubSkills.Where(s => s.IsActive).OrderBy(s => s.SectionNumber).ThenBy(s => s.SortOrder).ToListAsync();
        if (subSkills.Count < 3) return; // taxonomy not seeded

        var programs = await db.Programs.ToListAsync();
        var staff = await db.Staff.ToListAsync();
        var leadStaffId = staff.FirstOrDefault(s => s.Role == StaffRole.Coordinator)?.Id ?? staff.FirstOrDefault()?.Id;
        var participants = await db.Participants
            .Where(p => p.Status == ParticipantStatus.Active || p.Status == ParticipantStatus.Attention)
            .ToListAsync();

        var now = DateTime.UtcNow.Date;
        var prevMonthDate = now.AddMonths(-1);
        var prevKey = $"{prevMonthDate:yyyy-MM}";
        var curKey = $"{now:yyyy-MM}";

        // Pick three focus sub-skills (spread across sections) shared by all programs.
        var focus = subSkills
            .GroupBy(s => s.SectionNumber)
            .Select(g => g.First())
            .Take(3)
            .ToList();
        if (focus.Count < 3) focus = subSkills.Take(3).ToList();

        var thresholds = await db.ScoreThresholds.OrderByDescending(t => t.MinAverage).ToListAsync();
        ProgressLevel Derive(double avg)
        {
            foreach (var t in thresholds)
                if (avg >= t.MinAverage) return t.Level;
            return ProgressLevel.Novice;
        }

        var rng = new Random(424242);

        // Focus skills: previous month weeks 1–4, current month weeks 1–2.
        foreach (var prog in programs)
        {
            foreach (var (key, weeks) in new[] { (prevKey, 4), (curKey, 2) })
                for (var week = 1; week <= weeks; week++)
                    foreach (var f in focus)
                        db.WeeklyFocusSkills.Add(new WeeklyFocusSkill
                        {
                            ProgramId = prog.Id,
                            MonthKey = key,
                            WeekNumber = week,
                            SubSkillId = f.Id,
                        });
        }

        // Previous month: weekly scores, confirmed snapshots, monthly summaries.
        var prevMonthFirst = new DateTime(prevMonthDate.Year, prevMonthDate.Month, 1);
        foreach (var p in participants)
        {
            var participantAvgs = new List<double>();
            foreach (var f in focus)
            {
                var scores = new List<int>();
                for (var week = 1; week <= 4; week++)
                {
                    // Trend upward across the month with light noise; scores 1–3.
                    var baseScore = 1 + Math.Min(2, (week - 1) * 0.6 + rng.NextDouble());
                    var score = Math.Clamp((int)Math.Round(baseScore), 1, 3);
                    scores.Add(score);
                    db.WeeklyDataEntries.Add(new WeeklyDataEntry
                    {
                        ParticipantId = p.Id,
                        SubSkillId = f.Id,
                        MonthKey = prevKey,
                        WeekNumber = week,
                        WeekDate = prevMonthFirst.AddDays((week - 1) * 7),
                        Score = (DataScore)score,
                        RecordedByStaffMemberId = leadStaffId,
                    });
                }
                var avg = scores.Average();
                participantAvgs.Add(avg);
                var level = Derive(avg);
                db.MonthlyProgressSnapshots.Add(new MonthlyProgressSnapshot
                {
                    ParticipantId = p.Id,
                    SubSkillId = f.Id,
                    MonthKey = prevKey,
                    Level = level,
                    SuggestedLevel = level,
                    SummedScore = scores.Sum(),
                    ScoredWeekCount = scores.Count,
                    IsConfirmed = true,
                    ConfirmedByStaffMemberId = leadStaffId,
                });
            }

            var primary = Derive(participantAvgs.Count > 0 ? participantAvgs.Average() : 0);
            var first = p.FullName.Split(' ')[0];
            db.MonthlySummaries.Add(new MonthlySummary
            {
                ParticipantId = p.Id,
                MonthKey = prevKey,
                PrimaryLevel = primary,
                ProgressNarrative = $"{first} showed steady growth this month, especially in group focus skills. Prompting decreased week over week.",
                GoalsCarryOver = true,
            });
        }

        // Current month: per-Star plans (the planning tab is forward-looking).
        var priorityArea = await db.ObjectiveAreas.FirstOrDefaultAsync(a => a.Id == focus[0].ObjectiveAreaId);
        foreach (var p in participants)
        {
            var first = p.FullName.Split(' ')[0];
            db.PerStarPlans.Add(new PerStarPlan
            {
                ParticipantId = p.Id,
                AssignedStaffId = leadStaffId,
                MonthKey = curKey,
                PrimaryTier = p.Status == ParticipantStatus.Attention ? ProgressLevel.Novice : ProgressLevel.Intermediate,
                PriorityObjectiveAreaId = priorityArea?.Id,
                PrioritySubSkillId = focus[0].Id,
                MonthlyGoal = $"{first} will take on a lead role in one ensemble segment with reduced prompting.",
                HowIllSupport = "Pre-teach the segment 1:1, then fade to a visual cue during full-group runs.",
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Fills out the Year Calendar: a programming theme for every month (the migration seeds only
    /// a sample) plus a handful of key arts dates. Inserts only months not already present, so it
    /// composes with the migration seed and is safe to re-run.
    /// </summary>
    public static async Task SeedYearCalendarAsync(AppDbContext db)
    {
        var existingThemeMonths = await db.CalendarThemes.Select(t => t.Month).ToListAsync();
        var themes = new (int Month, string Title, string Subtitle, string Phase)[]
        {
            (1,  "New Beginnings",     "Ensemble-building and routines",       "Foundations"),
            (2,  "Voice & Story",      "Character and narrative work",         "Exploration"),
            (3,  "Spotlight",          "Solo and small-group performance",     "Rehearsal"),
            (4,  "Spring Production",  "Full-show rehearsal intensive",        "Rehearsal"),
            (5,  "Showcase",           "Spring performances and reflection",   "Performance"),
            (6,  "Devising",           "Student-created scenes",               "Exploration"),
            (7,  "Summer Intensive",   "Skills labs and games",                "Foundations"),
            (8,  "Community Stage",    "Site-based mini-performances",         "Performance"),
            (9,  "Fall Kickoff",       "New cohort onboarding",                "Foundations"),
            (10, "Movement & Music",   "Physical theatre and rhythm",          "Exploration"),
            (11, "Story to Stage",     "Adapting text for performance",        "Rehearsal"),
            (12, "Winter Showcase",    "End-of-year celebration",              "Performance"),
        };
        foreach (var t in themes)
        {
            if (existingThemeMonths.Contains(t.Month)) continue;
            db.CalendarThemes.Add(new CalendarTheme
            {
                Month = t.Month,
                ThemeTitle = t.Title,
                ThemeSubtitle = t.Subtitle,
                ProductionPhase = t.Phase,
                ProgrammingNotes = $"Focus programming around \"{t.Title}\" — align games and focus skills to the month's arc.",
            });
        }

        if (await db.KeyArtsDates.CountAsync() < 6)
        {
            var dates = new (int Month, int Sort, string DateText, string Observance, string Type, string TieIn)[]
            {
                (3,  20, "Mar 27", "World Theatre Day",            "Arts",      "Ensemble performance games"),
                (4,  20, "Apr 15", "Spring Showcase",              "Program",   "Full production performance"),
                (5,  20, "May 1",  "Older Americans / Community",  "Community", "Intergenerational performance"),
                (9,  20, "Sep 15", "Fall Cohort Begins",           "Program",   "Onboarding and auditions"),
                (12, 20, "Dec 12", "Winter Showcase",              "Program",   "End-of-year celebration"),
            };
            foreach (var d in dates)
                db.KeyArtsDates.Add(new KeyArtsDate
                {
                    Month = d.Month,
                    SortOrder = d.Sort,
                    DateText = d.DateText,
                    Observance = d.Observance,
                    ObservanceType = d.Type,
                    ProgrammingTieIn = d.TieIn,
                });
        }

        await db.SaveChangesAsync();
    }
}
