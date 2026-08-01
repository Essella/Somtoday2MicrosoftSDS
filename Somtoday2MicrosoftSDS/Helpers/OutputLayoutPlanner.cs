namespace Somtoday2MicrosoftSDS.Helpers;

internal sealed record OutputLayoutSchool(
    Guid SchoolUuid,
    string InstitutionAbbreviation,
    IReadOnlyList<Vestiging> Locations);

internal sealed record OutputLayoutLocation(
    Guid SchoolUuid,
    Vestiging Location);

internal sealed record OutputPublicationScope(
    string BasePrefix,
    IReadOnlyList<Guid> SchoolUuids,
    IReadOnlyList<OutputLayoutLocation> Locations)
{
    internal OutputPublicationScope Excluding(IReadOnlySet<Guid> unavailableSchoolUuids)
    {
        return new OutputPublicationScope(
            BasePrefix,
            SchoolUuids.Where(schoolUuid => !unavailableSchoolUuids.Contains(schoolUuid)).ToArray(),
            Locations.Where(location => !unavailableSchoolUuids.Contains(location.SchoolUuid)).ToArray());
    }
}

internal sealed record OutputLayoutIssue(
    IReadOnlyList<Guid> SchoolUuids,
    string Message);

internal sealed record OutputLayoutPlan(
    IReadOnlyList<OutputPublicationScope> Scopes,
    IReadOnlySet<Guid> FailedSchoolUuids,
    IReadOnlyList<OutputLayoutIssue> Issues);

internal static class OutputLayoutPlanner
{
    internal static OutputLayoutPlan Create(
        IEnumerable<OutputLayoutSchool> schools,
        string outputPrefix,
        bool separateByInstitution,
        bool separateByLocation)
    {
        ArgumentNullException.ThrowIfNull(schools);

        List<CandidateSchool> candidates = [];
        HashSet<Guid> failedSchoolUuids = [];
        List<OutputLayoutIssue> issues = [];

        foreach (OutputLayoutSchool school in schools)
        {
            try
            {
                string institutionSegment = BlobPathHelper.SanitizeSegment(
                    school.InstitutionAbbreviation,
                    "institution abbreviation");
                List<CandidateLocation> locations = school.Locations
                    .Select(location => new CandidateLocation(
                        school.SchoolUuid,
                        institutionSegment,
                        BlobPathHelper.SanitizeSegment(location.Afkorting, "location abbreviation"),
                        location))
                    .ToList();

                candidates.Add(new CandidateSchool(
                    school.SchoolUuid,
                    institutionSegment,
                    locations));
            }
            catch (ArgumentException ex)
            {
                failedSchoolUuids.Add(school.SchoolUuid);
                issues.Add(new OutputLayoutIssue([school.SchoolUuid], ex.Message));
            }
        }

        if (!separateByInstitution && !separateByLocation)
        {
            CandidateSchool[] successfulSchools = candidates
                .Where(school => !failedSchoolUuids.Contains(school.SchoolUuid))
                .ToArray();

            IReadOnlyList<OutputPublicationScope> scopes = successfulSchools.Length == 0
                ? []
                :
                [
                    new OutputPublicationScope(
                        outputPrefix,
                        successfulSchools.Select(school => school.SchoolUuid).ToArray(),
                        successfulSchools
                            .SelectMany(school => school.Locations)
                            .Select(ToOutputLocation)
                            .ToArray())
                ];

            return new OutputLayoutPlan(scopes, failedSchoolUuids, issues);
        }

        if (separateByInstitution && !separateByLocation)
        {
            foreach (IGrouping<string, CandidateSchool> collision in candidates
                .GroupBy(
                    school => BlobPathHelper.Combine(outputPrefix, school.InstitutionSegment),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                Guid[] affectedSchools = collision.Select(school => school.SchoolUuid).ToArray();
                failedSchoolUuids.UnionWith(affectedSchools);
                issues.Add(new OutputLayoutIssue(
                    affectedSchools,
                    $"Multiple institutions map to blob prefix '{collision.Key}'"));
            }

            OutputPublicationScope[] scopes = candidates
                .Where(school => !failedSchoolUuids.Contains(school.SchoolUuid))
                .Select(school => new OutputPublicationScope(
                    BlobPathHelper.Combine(outputPrefix, school.InstitutionSegment),
                    [school.SchoolUuid],
                    school.Locations.Select(ToOutputLocation).ToArray()))
                .ToArray();

            return new OutputLayoutPlan(scopes, failedSchoolUuids, issues);
        }

        List<PlannedLocation> plannedLocations = CreateLocationPlans(
            candidates.SelectMany(school => school.Locations),
            outputPrefix,
            separateByInstitution);

        foreach (IGrouping<string, PlannedLocation> collision in plannedLocations
            .GroupBy(location => location.BasePrefix, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            Guid[] affectedSchools = collision
                .Select(location => location.Candidate.SchoolUuid)
                .Distinct()
                .ToArray();
            failedSchoolUuids.UnionWith(affectedSchools);
            issues.Add(new OutputLayoutIssue(
                affectedSchools,
                $"Multiple locations map to blob prefix '{collision.Key}'"));
        }

        OutputPublicationScope[] locationScopes = plannedLocations
            .Where(location => !failedSchoolUuids.Contains(location.Candidate.SchoolUuid))
            .Select(location => new OutputPublicationScope(
                location.BasePrefix,
                [location.Candidate.SchoolUuid],
                [ToOutputLocation(location.Candidate)]))
            .ToArray();

        return new OutputLayoutPlan(locationScopes, failedSchoolUuids, issues);
    }

    private static List<PlannedLocation> CreateLocationPlans(
        IEnumerable<CandidateLocation> locations,
        string outputPrefix,
        bool separateByInstitution)
    {
        CandidateLocation[] candidates = locations.ToArray();
        HashSet<string> crossInstitutionCollisions = separateByInstitution
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : candidates
                .GroupBy(location => location.LocationSegment, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(location => location.SchoolUuid).Distinct().Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .Select(location =>
            {
                string locationSegment = crossInstitutionCollisions.Contains(location.LocationSegment)
                    ? $"{location.InstitutionSegment}_{location.LocationSegment}"
                    : location.LocationSegment;
                string prefix = separateByInstitution
                    ? BlobPathHelper.Combine(
                        outputPrefix,
                        location.InstitutionSegment,
                        location.LocationSegment)
                    : BlobPathHelper.Combine(outputPrefix, locationSegment);

                return new PlannedLocation(location, prefix);
            })
            .ToList();
    }

    private static OutputLayoutLocation ToOutputLocation(CandidateLocation location)
    {
        return new OutputLayoutLocation(location.SchoolUuid, location.Location);
    }

    private sealed record CandidateSchool(
        Guid SchoolUuid,
        string InstitutionSegment,
        IReadOnlyList<CandidateLocation> Locations);

    private sealed record CandidateLocation(
        Guid SchoolUuid,
        string InstitutionSegment,
        string LocationSegment,
        Vestiging Location);

    private sealed record PlannedLocation(
        CandidateLocation Candidate,
        string BasePrefix);
}
