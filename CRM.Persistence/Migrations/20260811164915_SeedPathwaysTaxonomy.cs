using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedPathwaysTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seed = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "ObjectiveAreas",
                columns: new[] { "Id", "Name", "Slug", "ColorHex", "SortOrder", "Track", "AnnualGoal", "SixMonthBenchmark", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("33333333-3333-3333-3333-000000000001"), "Collaborative Social Skills", "pw-collaborative-social-skills", "#b3541e", 100, 1, "Student will improve collaborative social skills within creative arts settings by participating with peers, following shared routines, and demonstrating social reciprocity during rehearsals/classes and productions as measured by monthly data.", "Student will demonstrate increased peer participation, turn-taking, and collaborative tolerance in structured arts activities with reduced support and improved duration/endurance across the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000002"), "Communication & Creative Self-Expression", "pw-communication-creative-self-expression", "#2e86ab", 101, 1, "Student will increase functional communication and creative self-expression within preferred or emerging Shining Stars pillars by expressing choices, using verbal/non-verbal communication, and participating in artistic tasks/performance routines as measured by monthly data.", "Student will show improved expressive communication and artistic participation with increased independence, carryover, and duration of engagement in selected creative activities over the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000003"), "Inclusion, Community Engagement & Personal Growth", "pw-inclusion-community-engagement-personal-growth", "#4c9e4f", 102, 1, "Student will increase inclusion, confidence, and personal growth by participating in class routines, engaging with the broader community, trying new tasks, and demonstrating growing independence across Shining Stars activities as measured by monthly data.", "Student will show improved belonging, confidence, self-advocacy, and independence with routines/transitions with stronger participation and longer duration across the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000004"), "Pillars, Capstones & Vocational Readiness", "pw-pillars-capstones-vocational-readiness", "#7d5ba6", 103, 1, "Student will build artistic, production, and pre-vocational/vocational skills by participating in individualized pillar-based activities, capstones, and production responsibilities with measurable progress in independence, quality, and duration.", "Student will demonstrate measurable growth in at least one primary pillar and in general production/capstone readiness, with improved consistency, role follow-through, and duration of participation over the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000005"), "Theater & Acting", "pw-theater-acting", "#8a5fc4", 104, 1, "Student will grow theater/acting skills through role-play, ensemble work, character engagement, and rehearsal/performance participation with increasing independence and stamina.", "Student will show improved participation in theater tasks, rehearsal expectations, and performance stamina during the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000006"), "Music & Vocals", "pw-music-vocals", "#d1495b", 105, 1, "Student will grow music/vocal skills through rhythm, singing, timing, and participation in music-based class/performance opportunities with increasing independence and stamina.", "Student will show improved rhythm/vocal participation, readiness for group or solo parts, and sustained participation during the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000007"), "Dance & Movement", "pw-dance-movement", "#e07b39", 106, 1, "Student will grow dance/movement skills through adaptive choreography, body awareness, inclusive movement work, and participation in staged pieces with increasing independence and stamina.", "Student will show improved movement participation, sequencing, and endurance during the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000008"), "Visual Arts & Design", "pw-visual-arts-design", "#378add", 107, 1, "Student will grow visual arts/design skills by participating in set/prop/costume-related art tasks and creative projects with improved follow-through, quality, and task endurance.", "Student will show improved completion, creative contribution, and sustained participation in visual arts activities during the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000009"), "Animation", "pw-animation", "#00a8a8", 108, 1, "Student will grow animation/digital creation skills through storyboarding, design, technology use, and contribution to digital showcase elements with increasing independence and task stamina.", "Student will show improved ability to participate in digital creation tasks and use tools/technology with greater independence and duration during the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000010"), "Production (Pre & Post)", "pw-production-pre-post", "#6b6960", 109, 1, "Student will increase production and pre-vocational/vocational readiness by completing stage/crew/media responsibilities, following checklists, and contributing to productions with improved independence and endurance.", "Student will show improved checklist completion, crew-role consistency, and sustained participation in production tasks during the first 6 months.", seed, seed },
                    { new Guid("33333333-3333-3333-3333-000000000011"), "Fashion Design", "pw-fashion-design", "#c2559d", 110, 1, "Student will grow fashion design and garment construction skills by safely participating in apparel design and costume-related wardrobe tasks with target consistency and designated staff prompts.", "Student will build baseline wardrobe awareness by participating in introductory styling, textile exploration, or costume-sorting activities for a sustained duration with designated staff support.", seed, seed },
                });

            migrationBuilder.InsertData(
                table: "SubSkills",
                columns: new[] { "Id", "ObjectiveAreaId", "Name", "Slug", "SectionNumber", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("44444444-4444-4444-4444-000000000001"), new Guid("33333333-3333-3333-3333-000000000001"), "Engages in group collaboration and ensemble participation", "pw-engages-in-group-collaboration-and-ensemble-participation", 1, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000002"), new Guid("33333333-3333-3333-3333-000000000001"), "Demonstrates emotional regulation and empathy during creative activities", "pw-demonstrates-emotional-regulation-and-empathy-during-creative-activities", 1, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000003"), new Guid("33333333-3333-3333-3333-000000000001"), "Shows perspective taking, flexibility, and adaptability when routines or plans change", "pw-shows-perspective-taking-flexibility-and-adaptability-when-routines-or-plans-change", 1, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000004"), new Guid("33333333-3333-3333-3333-000000000001"), "Initiates, responds to, and sustains peer interaction / turn-taking", "pw-initiates-responds-to-and-sustains-peer-interaction-turn-taking", 1, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000005"), new Guid("33333333-3333-3333-3333-000000000001"), "Sustains collaborative participation for target duration / stamina during group work", "pw-sustains-collaborative-participation-for-target-duration-stamina-during-group-work", 1, 5, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000006"), new Guid("33333333-3333-3333-3333-000000000002"), "Uses verbal communication and expressive choice-making during class activities", "pw-uses-verbal-communication-and-expressive-choice-making-during-class-activities", 2, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000007"), new Guid("33333333-3333-3333-3333-000000000002"), "Uses non-verbal communication / AAC, gesture, or movement-based expression", "pw-uses-non-verbal-communication-aac-gesture-or-movement-based-expression", 2, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000008"), new Guid("33333333-3333-3333-3333-000000000002"), "Demonstrates script reading, recall, or adaptive participation methods as applicable", "pw-demonstrates-script-reading-recall-or-adaptive-participation-methods-as-applicable", 2, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000009"), new Guid("33333333-3333-3333-3333-000000000002"), "Shows artistic self-expression within chosen pillar or exploratory activity", "pw-shows-artistic-self-expression-within-chosen-pillar-or-exploratory-activity", 2, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000010"), new Guid("33333333-3333-3333-3333-000000000002"), "Sustains expressive participation for target duration / stamina within creative tasks", "pw-sustains-expressive-participation-for-target-duration-stamina-within-creative-tasks", 2, 5, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000011"), new Guid("33333333-3333-3333-3333-000000000003"), "Participates in class routines and demonstrates sense of belonging", "pw-participates-in-class-routines-and-demonstrates-sense-of-belonging", 3, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000012"), new Guid("33333333-3333-3333-3333-000000000003"), "Engages in public interaction and community participation opportunities", "pw-engages-in-public-interaction-and-community-participation-opportunities", 3, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000013"), new Guid("33333333-3333-3333-3333-000000000003"), "Demonstrates confidence, self-advocacy, and willingness to try new tasks", "pw-demonstrates-confidence-self-advocacy-and-willingness-to-try-new-tasks", 3, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000014"), new Guid("33333333-3333-3333-3333-000000000003"), "Shows independence with routines, transitions, and follow-through", "pw-shows-independence-with-routines-transitions-and-follow-through", 3, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000015"), new Guid("33333333-3333-3333-3333-000000000003"), "Sustains participation / endurance for target duration across inclusion activities", "pw-sustains-participation-endurance-for-target-duration-across-inclusion-activities", 3, 5, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000016"), new Guid("33333333-3333-3333-3333-000000000004"), "Undeclared / General exploration across different creative mediums", "pw-undeclared-general-exploration-across-different-creative-mediums", 4, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000017"), new Guid("33333333-3333-3333-3333-000000000004"), "Explores different mediums of art and performance through the week", "pw-explores-different-mediums-of-art-and-performance-through-the-week", 4, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000018"), new Guid("33333333-3333-3333-3333-000000000004"), "Assists and shadows other majors, staff, or peers as appropriate", "pw-assists-and-shadows-other-majors-staff-or-peers-as-appropriate", 4, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000019"), new Guid("33333333-3333-3333-3333-000000000004"), "Supports productions, showcases, and events with miscellaneous roles", "pw-supports-productions-showcases-and-events-with-miscellaneous-roles", 4, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000020"), new Guid("33333333-3333-3333-3333-000000000004"), "Sustains general exploration / participation for target duration", "pw-sustains-general-exploration-participation-for-target-duration", 4, 5, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000021"), new Guid("33333333-3333-3333-3333-000000000005"), "Role-play, improvisation, and ensemble building", "pw-role-play-improvisation-and-ensemble-building", 5, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000022"), new Guid("33333333-3333-3333-3333-000000000005"), "Script analysis, stage presence, and character engagement", "pw-script-analysis-stage-presence-and-character-engagement", 5, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000023"), new Guid("33333333-3333-3333-3333-000000000005"), "Live productions, showcases, and performance stamina / duration", "pw-live-productions-showcases-and-performance-stamina-duration", 5, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000024"), new Guid("33333333-3333-3333-3333-000000000005"), "Follows acting directions, cues, and rehearsal expectations", "pw-follows-acting-directions-cues-and-rehearsal-expectations", 5, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000025"), new Guid("33333333-3333-3333-3333-000000000006"), "Call-and-response, rhythm, and vocal participation", "pw-call-and-response-rhythm-and-vocal-participation", 6, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000026"), new Guid("33333333-3333-3333-3333-000000000006"), "Group singing and solo performance readiness", "pw-group-singing-and-solo-performance-readiness", 6, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000027"), new Guid("33333333-3333-3333-3333-000000000006"), "Performance carryover into productions and showcases", "pw-performance-carryover-into-productions-and-showcases", 6, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000028"), new Guid("33333333-3333-3333-3333-000000000006"), "Vocal confidence, timing, and sustained participation / duration", "pw-vocal-confidence-timing-and-sustained-participation-duration", 6, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000029"), new Guid("33333333-3333-3333-3333-000000000007"), "Adaptive choreography and inclusive ensemble work", "pw-adaptive-choreography-and-inclusive-ensemble-work", 7, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000030"), new Guid("33333333-3333-3333-3333-000000000007"), "Expression through rhythm, movement, and body awareness", "pw-expression-through-rhythm-movement-and-body-awareness", 7, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000031"), new Guid("33333333-3333-3333-3333-000000000007"), "Stage musicals and performance-piece participation / duration", "pw-stage-musicals-and-performance-piece-participation-duration", 7, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000032"), new Guid("33333333-3333-3333-3333-000000000007"), "Follows movement sequences and transitions safely", "pw-follows-movement-sequences-and-transitions-safely", 7, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000033"), new Guid("33333333-3333-3333-3333-000000000008"), "Set painting, prop building, and visual storytelling", "pw-set-painting-prop-building-and-visual-storytelling", 8, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000034"), new Guid("33333333-3333-3333-3333-000000000008"), "Costume crafts and stage artwork", "pw-costume-crafts-and-stage-artwork", 8, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000035"), new Guid("33333333-3333-3333-3333-000000000008"), "Translates creativity into stage environments", "pw-translates-creativity-into-stage-environments", 8, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000036"), new Guid("33333333-3333-3333-3333-000000000008"), "Completes visual arts tasks with quality, follow-through, and duration", "pw-completes-visual-arts-tasks-with-quality-follow-through-and-duration", 8, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000037"), new Guid("33333333-3333-3333-3333-000000000009"), "Storyboarding and character design", "pw-storyboarding-and-character-design", 9, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000038"), new Guid("33333333-3333-3333-3333-000000000009"), "Simple animation and digital creation techniques", "pw-simple-animation-and-digital-creation-techniques", 9, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000039"), new Guid("33333333-3333-3333-3333-000000000009"), "Contributes to pre-show reels, projections, or digital shorts", "pw-contributes-to-pre-show-reels-projections-or-digital-shorts", 9, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000040"), new Guid("33333333-3333-3333-3333-000000000009"), "Uses technology and tools with support or independence for target duration", "pw-uses-technology-and-tools-with-support-or-independence-for-target-duration", 9, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000041"), new Guid("33333333-3333-3333-3333-000000000010"), "Planning, stage management, and assigned production roles", "pw-planning-stage-management-and-assigned-production-roles", 10, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000042"), new Guid("33333333-3333-3333-3333-000000000010"), "Filming rehearsals and performances", "pw-filming-rehearsals-and-performances", 10, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000043"), new Guid("33333333-3333-3333-3333-000000000010"), "Post-production editing (video/audio, highlight reels, PSAs)", "pw-post-production-editing-video-audio-highlight-reels-psas", 10, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000044"), new Guid("33333333-3333-3333-3333-000000000010"), "Completes checklist, crew tasks, role responsibilities, and duration expectations", "pw-completes-checklist-crew-tasks-role-responsibilities-and-duration-expectations", 10, 4, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000045"), new Guid("33333333-3333-3333-3333-000000000011"), "Translating creative ideas onto fashion figures or templates to conceptualize style, color, and character silhouette.", "pw-translating-creative-ideas-onto-fashion-figures-or-templates-to-conceptualize-style-color-and-character-silhouette", 11, 1, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000046"), new Guid("33333333-3333-3333-3333-000000000011"), "Basic hand/machine sewing, fabric cutting, measuring, or assembling textile pieces into functional wearable art.", "pw-basic-hand-machine-sewing-fabric-cutting-measuring-or-assembling-textile-pieces-into-functional-wearable-art", 11, 2, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000047"), new Guid("33333333-3333-3333-3333-000000000011"), "Applying fabric paints, distressing, dying, or adding textures/trims to garments to fit the aesthetic of a production or line.", "pw-applying-fabric-paints-distressing-dying-or-adding-textures-trims-to-garments-to-fit-the-aesthetic-of-a-production-or-line", 11, 3, true, seed, seed },
                    { new Guid("44444444-4444-4444-4444-000000000048"), new Guid("33333333-3333-3333-3333-000000000011"), "Organizing apparel, fitting garments to individuals, maintaining tools safely, and completing fashion tasks with quality, follow-through, and target d", "pw-organizing-apparel-fitting-garments-to-individuals-maintaining-tools-safely-and-completing-fashion-tasks-with-quality-follow-through-and-target-dur", 11, 4, true, seed, seed },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000001"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000002"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000003"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000004"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000005"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000006"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000007"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000008"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000009"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000010"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000011"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000012"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000013"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000014"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000015"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000016"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000017"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000018"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000019"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000020"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000021"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000022"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000023"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000024"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000025"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000026"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000027"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000028"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000029"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000030"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000031"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000032"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000033"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000034"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000035"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000036"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000037"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000038"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000039"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000040"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000041"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000042"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000043"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000044"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000045"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000046"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000047"));
            migrationBuilder.DeleteData(table: "SubSkills", keyColumn: "Id", keyValue: new Guid("44444444-4444-4444-4444-000000000048"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000001"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000002"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000003"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000004"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000005"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000006"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000007"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000008"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000009"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000010"));
            migrationBuilder.DeleteData(table: "ObjectiveAreas", keyColumn: "Id", keyValue: new Guid("33333333-3333-3333-3333-000000000011"));
        }
    }
}
