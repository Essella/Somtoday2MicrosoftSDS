using System.Text;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class SDScsvHelperV2
    {
        private readonly SettingsHelper sh = new SettingsHelper();
        private readonly ResolvedExportPopulation population;
        private readonly DateOnly runDate;

        public SDScsvHelperV2(ResolvedExportPopulation population, DateOnly runDate)
        {
            this.population = population;
            this.runDate = runDate;
        }

        internal SDScsvV2 ConvertToSDSCSV()
        {
            SDScsvV2 result = new SDScsvV2
            {
                Orgs = GetOrgs(),
                Users = GetUsers(),
                Roles = GetRoles()
            };

            var classesInfo = GetClassesAndEnrolements();
            result.Classes = classesInfo.Classes;
            result.Enrollments = classesInfo.Enrollments;

            result.Relationships = GetRelationships();

            return result;
        }

        private List<Relationships> GetRelationships()
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

        private (List<Classes> Classes, List<Enrollments> Enrollments) GetClassesAndEnrolements()
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

        private List<Roles> GetRoles()
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

        private List<Users> GetUsers()
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

        private List<Orgs> GetOrgs()
        {
            return
            [
                new Orgs
                {
                    sourcedId = population.Vestiging.Uuid.ToString(),
                    name = population.Vestiging.Naam,
                    type = "school"
                }
            ];
        }

        private string GetVestigingsIds()
        {
            StringBuilder result = new StringBuilder(population.Vestiging.Afkorting.Length * 3);
            foreach (char c in population.Vestiging.Afkorting)
            {
                int x = c;
                result.Append(x.ToString("000"));
            }

            return result.ToString().TrimStart('0');
        }
    }
}
