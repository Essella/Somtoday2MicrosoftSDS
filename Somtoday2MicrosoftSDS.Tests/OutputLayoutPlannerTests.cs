using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class OutputLayoutPlannerTests
{
    private static readonly Guid FirstSchoolUuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondSchoolUuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdSchoolUuid = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(false, false, "sds/output")]
    [InlineData(true, false, "sds/output/SCHOOL")]
    [InlineData(false, true, "sds/output/LOC")]
    [InlineData(true, true, "sds/output/SCHOOL/LOC")]
    public void CreatesConfirmedOutputLayouts(
        bool separateByInstitution,
        bool separateByLocation,
        string expectedPrefix)
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [School(FirstSchoolUuid, "SCHOOL", "LOC")],
            "sds/output",
            separateByInstitution,
            separateByLocation);

        OutputPublicationScope scope = Assert.Single(plan.Scopes);
        Assert.Equal(expectedPrefix, scope.BasePrefix);
        Assert.Equal(FirstSchoolUuid, Assert.Single(scope.SchoolUuids));
        Assert.Equal("LOC", Assert.Single(scope.Locations).Location.Afkorting);
        Assert.Empty(plan.FailedSchoolUuids);
    }

    [Theory]
    [InlineData(false, ".staging", "sds/output/VALID")]
    [InlineData(true, ".StAgInG", "sds/output/VALID/LOC")]
    public void ReservedInstitutionFirstSegmentFailsOnlyThatInstitution(
        bool separateByLocation,
        string reservedAbbreviation,
        string expectedSurvivingPrefix)
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, reservedAbbreviation, "BAD"),
                School(SecondSchoolUuid, "VALID", "LOC")
            ],
            "sds/output",
            separateByInstitution: true,
            separateByLocation);

        OutputPublicationScope survivingScope = Assert.Single(plan.Scopes);
        Assert.Equal(expectedSurvivingPrefix, survivingScope.BasePrefix);
        Assert.Equal(SecondSchoolUuid, Assert.Single(survivingScope.SchoolUuids));
        Assert.Equal(FirstSchoolUuid, Assert.Single(plan.FailedSchoolUuids));
        Assert.Single(plan.Issues);
    }

    [Theory]
    [InlineData(".staging")]
    [InlineData(".STAGING")]
    [InlineData(".StAgInG")]
    public void ReservedLocationFirstSegmentFailsOnlyThatInstitution(string reservedAbbreviation)
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "INVALID", reservedAbbreviation),
                School(SecondSchoolUuid, "VALID", "LOC")
            ],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: true);

        OutputPublicationScope survivingScope = Assert.Single(plan.Scopes);
        Assert.Equal("sds/output/LOC", survivingScope.BasePrefix);
        Assert.Equal(SecondSchoolUuid, Assert.Single(survivingScope.SchoolUuids));
        Assert.Equal(FirstSchoolUuid, Assert.Single(plan.FailedSchoolUuids));
        Assert.Single(plan.Issues);
    }

    [Fact]
    public void StagingAbbreviationsAreAllowedWhenGroupingAddsNoReservedFirstSegment()
    {
        OutputLayoutPlan combinedPlan = OutputLayoutPlanner.Create(
            [School(FirstSchoolUuid, ".staging", ".STAGING")],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: false);
        OutputLayoutPlan nestedLocationPlan = OutputLayoutPlanner.Create(
            [School(FirstSchoolUuid, "SCHOOL", ".STAGING")],
            "sds/output",
            separateByInstitution: true,
            separateByLocation: true);

        Assert.Equal("sds/output", Assert.Single(combinedPlan.Scopes).BasePrefix);
        Assert.Empty(combinedPlan.FailedSchoolUuids);
        Assert.Equal("sds/output/SCHOOL/.STAGING", Assert.Single(nestedLocationPlan.Scopes).BasePrefix);
        Assert.Empty(nestedLocationPlan.FailedSchoolUuids);
    }

    [Fact]
    public void LocationOnlyDisambiguationCanMakeStagingLocationSegmentsSafe()
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "FIRST", ".staging"),
                School(SecondSchoolUuid, "SECOND", ".STAGING")
            ],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: true);

        Assert.Equal(
            ["sds/output/FIRST_.staging", "sds/output/SECOND_.STAGING"],
            plan.Scopes.Select(scope => scope.BasePrefix));
        Assert.Empty(plan.FailedSchoolUuids);
    }

    [Fact]
    public void LocationOnlyLayoutDisambiguatesOnlyCrossInstitutionCollisions()
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "A/1", "LOC"),
                School(SecondSchoolUuid, "B", "loc"),
                School(ThirdSchoolUuid, "C", "OTHER")
            ],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: true);

        Assert.Equal(
            ["sds/output/A_1_LOC", "sds/output/B_loc", "sds/output/OTHER"],
            plan.Scopes.Select(scope => scope.BasePrefix));
        Assert.Empty(plan.FailedSchoolUuids);
    }

    [Fact]
    public void SameInstitutionLocationCollisionFailsThatInstitution()
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [School(FirstSchoolUuid, "SCHOOL", "A/B", "A\\B")],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: true);

        Assert.Empty(plan.Scopes);
        Assert.Contains(FirstSchoolUuid, plan.FailedSchoolUuids);
        Assert.Single(plan.Issues);
    }

    [Fact]
    public void RemainingDisambiguatedPathCollisionFailsOnlyAffectedInstitutions()
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "A", "X"),
                School(SecondSchoolUuid, "B", "X"),
                School(ThirdSchoolUuid, "C", "A_X")
            ],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: true);

        OutputPublicationScope survivingScope = Assert.Single(plan.Scopes);
        Assert.Equal("sds/output/B_X", survivingScope.BasePrefix);
        Assert.Equal([FirstSchoolUuid, ThirdSchoolUuid], plan.FailedSchoolUuids.OrderBy(uuid => uuid));
    }

    [Fact]
    public void InstitutionCollisionFailsAffectedInstitutionsButKeepsOthers()
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "SCHOOL", "A"),
                School(SecondSchoolUuid, "school", "B"),
                School(ThirdSchoolUuid, "OTHER", "C")
            ],
            "sds/output",
            separateByInstitution: true,
            separateByLocation: false);

        OutputPublicationScope survivingScope = Assert.Single(plan.Scopes);
        Assert.Equal("sds/output/OTHER", survivingScope.BasePrefix);
        Assert.Equal([FirstSchoolUuid, SecondSchoolUuid], plan.FailedSchoolUuids.OrderBy(uuid => uuid));
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    public void EmptyLocationListStillPlansNonLocationScopes(
        bool separateByInstitution,
        bool separateByLocation,
        int expectedScopeCount)
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [School(FirstSchoolUuid, "SCHOOL")],
            "sds/output",
            separateByInstitution,
            separateByLocation);

        Assert.Equal(expectedScopeCount, plan.Scopes.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BlankSelectedLocationFailsOnlyItsInstitution(
        bool separateByInstitution,
        bool separateByLocation)
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "INVALID", " "),
                School(SecondSchoolUuid, "VALID", "LOC")
            ],
            "sds/output",
            separateByInstitution,
            separateByLocation);

        OutputPublicationScope survivingScope = Assert.Single(plan.Scopes);
        Assert.Equal(SecondSchoolUuid, Assert.Single(survivingScope.SchoolUuids));
        Assert.Equal("LOC", Assert.Single(survivingScope.Locations).Location.Afkorting);
        Assert.Equal(FirstSchoolUuid, Assert.Single(plan.FailedSchoolUuids));
        Assert.Single(plan.Issues);
    }

    [Fact]
    public void CombinedScopeCanPublishOnlyTheSuccessfulInstitutionSubset()
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "FIRST", "A"),
                School(SecondSchoolUuid, "SECOND", "B")
            ],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: false);

        OutputPublicationScope subset = Assert.Single(plan.Scopes).Excluding(
            new HashSet<Guid> { SecondSchoolUuid });

        Assert.Equal(FirstSchoolUuid, Assert.Single(subset.SchoolUuids));
        Assert.Equal("A", Assert.Single(subset.Locations).Location.Afkorting);
        Assert.Equal("sds/output", subset.BasePrefix);
    }

    [Fact]
    public void LocationOnlyDisambiguationDoesNotShiftAfterAnotherInstitutionIsExcluded()
    {
        OutputLayoutPlan plan = OutputLayoutPlanner.Create(
            [
                School(FirstSchoolUuid, "FIRST", "LOC"),
                School(SecondSchoolUuid, "SECOND", "loc")
            ],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: true);

        OutputPublicationScope firstScope = Assert.Single(
            plan.Scopes.Where(scope => scope.SchoolUuids.Contains(FirstSchoolUuid)));
        OutputPublicationScope remainingScope = firstScope.Excluding(
            new HashSet<Guid> { SecondSchoolUuid });
        OutputPublicationScope replannedWithoutSecondSchool = Assert.Single(OutputLayoutPlanner.Create(
            [School(FirstSchoolUuid, "FIRST", "LOC")],
            "sds/output",
            separateByInstitution: false,
            separateByLocation: true).Scopes);

        Assert.Equal("sds/output/FIRST_LOC", remainingScope.BasePrefix);
        Assert.Equal("sds/output/LOC", replannedWithoutSecondSchool.BasePrefix);
        Assert.Equal(FirstSchoolUuid, Assert.Single(remainingScope.SchoolUuids));
    }

    private static OutputLayoutSchool School(Guid schoolUuid, string abbreviation, params string[] locations)
    {
        return new OutputLayoutSchool(
            schoolUuid,
            abbreviation,
            locations.Select(location => new Vestiging
            {
                Uuid = Guid.NewGuid(),
                Naam = location,
                Afkorting = location
            }).ToArray());
    }
}
