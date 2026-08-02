using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers;

internal static class ExportPopulationResolver
{
    internal static ResolvedExportPopulation Resolve(VestigingModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Dictionary<Guid, Medewerker> teachersById = ToFirstById(model.Medewerkers, teacher => teacher.Uuid);
        Dictionary<Guid, Leerling> studentsById = ToFirstById(model.Leerlingen, student => student.Uuid);
        List<ResolvedClass> classes = [];
        HashSet<Guid> includedTeacherIds = [];
        HashSet<Guid> includedStudentIds = [];

        foreach (Lesgroep sourceClass in model.Lesgroepen)
        {
            if (string.IsNullOrWhiteSpace(sourceClass.Naam))
            {
                continue;
            }

            List<Medewerker> resolvedTeachers = [];
            foreach (Guid teacherId in sourceClass.Docenten ?? [])
            {
                if (teachersById.TryGetValue(teacherId, out Medewerker teacher))
                {
                    resolvedTeachers.Add(teacher);
                }
            }

            List<Leerling> resolvedStudents = [];
            foreach (LeerlingVestiging studentReference in sourceClass.Leerlingen ?? [])
            {
                if (studentReference is not null && studentsById.TryGetValue(studentReference.Uuid, out Leerling student))
                {
                    resolvedStudents.Add(student);
                }
            }

            if (resolvedTeachers.Count == 0 || resolvedStudents.Count == 0)
            {
                continue;
            }

            classes.Add(new ResolvedClass(sourceClass, resolvedTeachers, resolvedStudents));
            includedTeacherIds.UnionWith(resolvedTeachers.Select(teacher => teacher.Uuid));
            includedStudentIds.UnionWith(resolvedStudents.Select(student => student.Uuid));
        }

        List<Medewerker> teachers = SelectFirstIncludedById(
            model.Medewerkers,
            includedTeacherIds,
            teacher => teacher.Uuid);
        List<Leerling> students = SelectFirstIncludedById(
            model.Leerlingen,
            includedStudentIds,
            student => student.Uuid);
        (List<ResolvedGuardian> guardians, int guardiansExcludedForMissingName) = ResolveGuardians(
            model.OuderVerzorgers,
            includedStudentIds);

        return new ResolvedExportPopulation(
            model.Vestiging,
            classes,
            teachers,
            students,
            guardians,
            guardiansExcludedForMissingName);
    }

    private static (List<ResolvedGuardian> Guardians, int GuardiansExcludedForMissingName) ResolveGuardians(
        IEnumerable<OuderVerzorger> sourceGuardians,
        HashSet<Guid> includedStudentIds)
    {
        List<ResolvedGuardian> guardians = [];
        int guardiansExcludedForMissingName = 0;

        foreach (OuderVerzorger guardian in sourceGuardians)
        {
            if (!GuardianExportPolicy.HasUsableContact(guardian))
            {
                continue;
            }

            List<Guid> studentIds = [];
            foreach (Guid studentId in guardian.Leerlingen_van_vestiging ?? [])
            {
                if (includedStudentIds.Contains(studentId))
                {
                    studentIds.Add(studentId);
                }
            }

            if (studentIds.Count == 0)
            {
                continue;
            }

            if (!GuardianExportPolicy.HasUsableName(guardian))
            {
                guardiansExcludedForMissingName++;
                continue;
            }

            guardians.Add(new ResolvedGuardian(guardian, studentIds));
        }

        return (guardians, guardiansExcludedForMissingName);
    }

    private static Dictionary<Guid, TValue> ToFirstById<TValue>(
        IEnumerable<TValue> values,
        Func<TValue, Guid> keySelector)
    {
        Dictionary<Guid, TValue> result = [];
        foreach (TValue value in values)
        {
            result.TryAdd(keySelector(value), value);
        }

        return result;
    }

    private static List<TValue> SelectFirstIncludedById<TValue>(
        IEnumerable<TValue> values,
        HashSet<Guid> includedIds,
        Func<TValue, Guid> keySelector)
    {
        List<TValue> result = [];
        HashSet<Guid> emittedIds = [];

        foreach (TValue value in values)
        {
            Guid id = keySelector(value);
            if (includedIds.Contains(id) && emittedIds.Add(id))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
