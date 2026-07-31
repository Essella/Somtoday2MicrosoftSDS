namespace Somtoday2MicrosoftSDS.Models;

internal sealed record ResolvedExportPopulation(
    Vestiging Vestiging,
    IReadOnlyList<ResolvedClass> Classes,
    IReadOnlyList<Medewerker> Teachers,
    IReadOnlyList<Leerling> Students,
    IReadOnlyList<ResolvedGuardian> Guardians);

internal sealed record ResolvedClass(
    Lesgroep Source,
    IReadOnlyList<Medewerker> Teachers,
    IReadOnlyList<Leerling> Students);

internal sealed record ResolvedGuardian(
    OuderVerzorger Source,
    IReadOnlyList<Guid> StudentIds);
