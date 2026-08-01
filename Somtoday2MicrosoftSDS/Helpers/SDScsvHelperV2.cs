using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class SDScsvHelperV2
    {
        private readonly SettingsHelper sh = new SettingsHelper();
        private readonly IReadOnlyList<ResolvedExportPopulation> populations;
        private readonly DateOnly runDate;

        public SDScsvHelperV2(ResolvedExportPopulation population, DateOnly runDate)
            : this([population], runDate)
        {
        }

        public SDScsvHelperV2(
            IReadOnlyList<ResolvedExportPopulation> populations,
            DateOnly runDate)
        {
            this.populations = populations ?? throw new ArgumentNullException(nameof(populations));
            this.runDate = runDate;
        }

        internal SDScsvV2 ConvertToSDSCSV()
        {
            SDScsvV2 result = new();
            Dictionary<string, Orgs> organizations = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Users> users = new(StringComparer.Ordinal);
            Dictionary<string, Guid> classSourceIds = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> roles = new(StringComparer.Ordinal);
            HashSet<string> enrollments = new(StringComparer.Ordinal);
            HashSet<string> relationships = new(StringComparer.Ordinal);

            foreach (ResolvedExportPopulation population in populations)
            {
                Orgs organization = GetOrganization(population);
                if (organizations.TryGetValue(organization.sourcedId, out Orgs existingOrganization))
                {
                    if (!string.Equals(existingOrganization.name, organization.name, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "One Somtoday location maps to conflicting SDS V2.1 organization records");
                    }
                }
                else
                {
                    organizations.Add(organization.sourcedId, organization);
                    result.Orgs.Add(organization);
                }

                foreach (Users user in GetUsers(population))
                {
                    if (users.TryGetValue(user.sourcedId, out Users existingUser))
                    {
                        if (!UserRowsEqual(existingUser, user))
                        {
                            throw new InvalidOperationException(
                                "One Somtoday person maps to conflicting SDS V2.1 user records");
                        }
                    }
                    else
                    {
                        users.Add(user.sourcedId, user);
                        result.Users.Add(user);
                    }
                }

                foreach (Roles role in GetRoles(population))
                {
                    if (roles.Add(CompositeKey(role.userSourcedId, role.orgSourcedId, role.role)))
                    {
                        result.Roles.Add(role);
                    }
                }

                var classesInfo = GetClassesAndEnrolements(population);
                for (int index = 0; index < classesInfo.Classes.Count; index++)
                {
                    Classes sdsClass = classesInfo.Classes[index];
                    Guid sourceClassUuid = population.Classes[index].Source.Uuid;
                    if (classSourceIds.TryGetValue(sdsClass.sourcedId, out Guid existingSourceClassUuid))
                    {
                        if (existingSourceClassUuid != sourceClassUuid)
                        {
                            throw new InvalidOperationException(
                                "Multiple Somtoday classes map to the same SDS V2.1 class identifier");
                        }
                    }
                    else
                    {
                        classSourceIds.Add(sdsClass.sourcedId, sourceClassUuid);
                        result.Classes.Add(sdsClass);
                    }
                }

                foreach (Enrollments enrollment in classesInfo.Enrollments)
                {
                    if (enrollments.Add(CompositeKey(
                        enrollment.classSourcedId,
                        enrollment.userSourcedId,
                        enrollment.role)))
                    {
                        result.Enrollments.Add(enrollment);
                    }
                }

                foreach (Relationships relationship in GetRelationships(population))
                {
                    if (relationships.Add(CompositeKey(
                        relationship.userSourcedId,
                        relationship.relationshipUserSourcedId,
                        relationship.relationshipRole)))
                    {
                        result.Relationships.Add(relationship);
                    }
                }
            }

            return result;
        }

        private static string CompositeKey(params string[] values)
        {
            return string.Join('\u001f', values);
        }

        private static bool UserRowsEqual(Users first, Users second)
        {
            return string.Equals(first.username, second.username, StringComparison.Ordinal)
                && string.Equals(first.givenName, second.givenName, StringComparison.Ordinal)
                && string.Equals(first.familyName, second.familyName, StringComparison.Ordinal)
                && string.Equals(first.password, second.password, StringComparison.Ordinal)
                && string.Equals(first.activeDirectoryMatchId, second.activeDirectoryMatchId, StringComparison.Ordinal)
                && string.Equals(first.email, second.email, StringComparison.Ordinal)
                && string.Equals(first.phone, second.phone, StringComparison.Ordinal)
                && string.Equals(first.sms, second.sms, StringComparison.Ordinal);
        }

        private static List<Relationships> GetRelationships(ResolvedExportPopulation population)
        {
            List<Relationships> relationships = [];
            foreach (ResolvedGuardian resolvedGuardian in population.Guardians)
            {
                foreach (Guid studentId in resolvedGuardian.StudentIds)
                {
                    relationships.Add(new Relationships
                    {
                        userSourcedId = studentId.ToString(),
                        relationshipUserSourcedId = resolvedGuardian.Source.Uuid.ToString(),
                        relationshipRole = "guardian" // https://learn.microsoft.com/en-us/schooldatasync/default-list-of-values#contact-relationship-roles
                    });
                }
            }

            return relationships;
        }

        private (List<Classes> Classes, List<Enrollments> Enrollments) GetClassesAndEnrolements(
            ResolvedExportPopulation population)
        {
            List<Classes> classes = [];
            List<Enrollments> enrollments = [];

            string currentSchoolyear = AmsterdamTimeHelper.GetSchoolYear(runDate);
            string vestigingsAfkorting = population.Vestiging.Afkorting;
            string vestigingsAfkortingLower = vestigingsAfkorting.ToLower();
            string vestigingUuid = population.Vestiging.Uuid.ToString();

            foreach (ResolvedClass resolvedClass in population.Classes)
            {
                Lesgroep sourceClass = resolvedClass.Source;
                string className = BusinessLogicHelper.GetFilteredName(sourceClass.Naam);
                string classSourcedId = (className.StartsWith(vestigingsAfkorting, StringComparison.CurrentCultureIgnoreCase) ? className : vestigingsAfkortingLower + className) + currentSchoolyear;

                Classes sdsClass = new Classes
                {
                    title = className,
                    orgSourcedId = vestigingUuid,
                    sourcedId = classSourcedId
                };

                classes.Add(sdsClass);
                foreach (Medewerker teacher in resolvedClass.Teachers)
                {
                    enrollments.Add(new Enrollments
                    {
                        classSourcedId = sdsClass.sourcedId,
                        userSourcedId = teacher.Uuid.ToString(),
                        role = "teacher" // https://learn.microsoft.com/en-us/schooldatasync/default-list-of-values#enrollment-roles
                    });
                }

                foreach (Leerling student in resolvedClass.Students)
                {
                    enrollments.Add(new Enrollments
                    {
                        classSourcedId = sdsClass.sourcedId,
                        userSourcedId = student.Uuid.ToString(),
                        role = "student" // https://learn.microsoft.com/en-us/schooldatasync/default-list-of-values#enrollment-roles
                    });
                }
            }

            return (classes, enrollments);
        }

        private static List<Roles> GetRoles(ResolvedExportPopulation population)
        {
            List<Roles> result = [];
            string vestigingUuid = population.Vestiging.Uuid.ToString();

            foreach (Medewerker teacher in population.Teachers)
            {
                result.Add(new Roles
                {
                    orgSourcedId = vestigingUuid,
                    userSourcedId = teacher.Uuid.ToString(),
                    role = "staff"
                });
            }

            foreach (Leerling student in population.Students)
            {
                result.Add(new Roles
                {
                    orgSourcedId = vestigingUuid,
                    userSourcedId = student.Uuid.ToString(),
                    role = "student"
                });
            }

            foreach (ResolvedGuardian guardian in population.Guardians)
            {
                result.Add(new Roles
                {
                    orgSourcedId = vestigingUuid,
                    userSourcedId = guardian.Source.Uuid.ToString(),
                    role = "other"
                });
            }

            return result;
        }

        private List<Users> GetUsers(ResolvedExportPopulation population)
        {
            List<Users> result = [];
            foreach (Medewerker teacher in population.Teachers)
            {
                result.Add(new Users
                {
                    username = sh.ReplaceTeacherProperty(SettingsHelper.OutputFormatUsernameTeacher, teacher),
                    sourcedId = teacher.Uuid.ToString()
                });
            }

            foreach (Leerling student in population.Students)
            {
                result.Add(new Users
                {
                    username = sh.ReplaceStudentProperty(SettingsHelper.OutputFormatUsernameStudent, student),
                    sourcedId = student.Uuid.ToString()
                });
            }

            foreach (ResolvedGuardian resolvedGuardian in population.Guardians)
            {
                OuderVerzorger guardian = resolvedGuardian.Source;
                result.Add(new Users
                {
                    username = guardian.Emailadres,
                    sourcedId = guardian.Uuid.ToString(),
                    givenName = guardian.Voorletters ?? string.Empty,
                    familyName = GuardianExportPolicy.GetFamilyName(guardian),
                    email = guardian.Emailadres,
                    phone = GuardianExportPolicy.GetPhone(guardian)
                });
            }

            return result;
        }

        private static Orgs GetOrganization(ResolvedExportPopulation population)
        {
            return new Orgs
            {
                sourcedId = population.Vestiging.Uuid.ToString(),
                name = population.Vestiging.Naam,
                type = "school"
            };
        }

    }
}
