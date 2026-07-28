using System.Text;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class SDScsvHelperV2
    {
        private readonly SettingsHelper sh = new SettingsHelper();
        private readonly VestigingModel vestigingModel;

        public SDScsvHelperV2(VestigingModel info)
        {
            vestigingModel = info;
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
            HashSet<Guid> leerlingIds = vestigingModel.Leerlingen.Select(s => s.Uuid).ToHashSet();

            foreach (OuderVerzorger ouder in vestigingModel.OuderVerzorgers)
            {
                if (ouder.Leerlingen_van_vestiging == null)
                {
                    continue;
                }

                foreach (Guid leerling in ouder.Leerlingen_van_vestiging)
                {
                    // Heeft deze ouder een gekoppelde leerling?
                    if (leerlingIds.Contains(leerling) && !string.IsNullOrEmpty(ouder.Emailadres))
                    {
                        relationships.Add(new Relationships
                        {
                            userSourcedId = leerling.ToString(),
                            relationshipUserSourcedId = ouder.Uuid.ToString(),
                            relationshipRole = "guardian" // https://learn.microsoft.com/en-us/schooldatasync/default-list-of-values#contact-relationship-roles
                        });
                    }
                }
            }

            return relationships;
        }

        private (List<Classes> Classes, List<Enrollments> Enrollments) GetClassesAndEnrolements()
        {
            List<Classes> classes = [];
            List<Enrollments> enrollments = [];

            DateTime now = DateTime.Now;
            string currentSchoolyear = now.Month < 8 ? $"{now.Year - 1}-{now.Year}" : $"{now.Year}-{now.Year + 1}";
            HashSet<Guid> medewerkerIds = vestigingModel.Medewerkers.Select(m => m.Uuid).ToHashSet();
            HashSet<Guid> leerlingIds = vestigingModel.Leerlingen.Select(s => s.Uuid).ToHashSet();
            string vestigingsAfkorting = vestigingModel.Vestiging.Afkorting;
            string vestigingsAfkortingLower = vestigingsAfkorting.ToLower();
            string vestigingUuid = vestigingModel.Vestiging.Uuid.ToString();

            foreach (Lesgroep lesgroep in vestigingModel.Lesgroepen)
            {
                if (lesgroep.Docenten.Count > 0 && lesgroep.Leerlingen.Count > 0)
                {
                    string sectieNaam = BusinessLogicHelper.GetFilteredName(lesgroep.Naam);
                    string classSourcedId = (sectieNaam.StartsWith(vestigingsAfkorting, StringComparison.CurrentCultureIgnoreCase) ? sectieNaam : vestigingsAfkortingLower + sectieNaam) + currentSchoolyear;

                    Classes lg = new Classes
                    {
                        title = sectieNaam,
                        orgSourcedId = vestigingUuid,
                        sourcedId = classSourcedId
                    };

                    classes.Add(lg);
                    foreach (Guid mw in lesgroep.Docenten)
                    {
                        if (medewerkerIds.Contains(mw)) // als de docent voorkomt in de medewerkerlijst.
                        {
                            enrollments.Add(new Enrollments
                            {
                                classSourcedId = lg.sourcedId,
                                userSourcedId = mw.ToString(),
                                role = "teacher" // https://learn.microsoft.com/en-us/schooldatasync/default-list-of-values#enrollment-roles
                            });
                        }
                    }

                    foreach (var ll in lesgroep.Leerlingen)
                    {
                        if (leerlingIds.Contains(ll.Uuid)) // als de leerling voorkomt in de leerlinglijst.
                        {
                            enrollments.Add(new Enrollments
                            {
                                classSourcedId = lg.sourcedId,
                                userSourcedId = ll.Uuid.ToString(),
                                role = "student" // https://learn.microsoft.com/en-us/schooldatasync/default-list-of-values#enrollment-roles
                            });
                        }
                    }
                }
            }

            return (classes, enrollments);
        }

        private List<Roles> GetRoles()
        {
            List<Roles> result = [];
            string vestigingUuid = vestigingModel.Vestiging.Uuid.ToString();

            foreach (Medewerker mw in vestigingModel.Medewerkers)
            {
                result.Add(new Roles
                {
                    orgSourcedId = vestigingUuid,
                    userSourcedId = mw.Uuid.ToString(),
                    role = "staff"
                });
            }

            foreach (Leerling ll in vestigingModel.Leerlingen)
            {
                result.Add(new Roles
                {
                    orgSourcedId = vestigingUuid,
                    userSourcedId = ll.Uuid.ToString(),
                    role = "student"
                });
            }

            foreach (OuderVerzorger ov in vestigingModel.OuderVerzorgers)
            {
                if (!string.IsNullOrEmpty(ov.Emailadres))
                {
                    result.Add(new Roles
                    {
                        orgSourcedId = vestigingUuid,
                        userSourcedId = ov.Uuid.ToString(),
                        role = "other"
                    });
                }
            }

            return result;
        }

        private List<Users> GetUsers()
        {
            List<Users> result = [];
            foreach (Medewerker mw in vestigingModel.Medewerkers)
            {
                result.Add(new Users
                {
                    username = sh.ReplaceTeacherProperty(SettingsHelper.OutputFormatUsernameTeacher, mw),
                    sourcedId = mw.Uuid.ToString()
                });
            }

            foreach (Leerling ll in vestigingModel.Leerlingen)
            {
                result.Add(new Users
                {
                    username = sh.ReplaceStudentProperty(SettingsHelper.OutputFormatUsernameStudent, ll),
                    sourcedId = ll.Uuid.ToString()
                });
            }

            foreach (OuderVerzorger ov in vestigingModel.OuderVerzorgers)
            {
                if (!string.IsNullOrEmpty(ov.Emailadres))
                {
                    result.Add(new Users
                    {
                        username = ov.Emailadres,
                        sourcedId = ov.Uuid.ToString(),
                        phone = BusinessLogicHelper.NormaliseerTelefoonnummerNaarE164(ov.Telefoonnummer)
                    });
                }
            }

            return result;
        }

        private List<Orgs> GetOrgs()
        {
            return
            [
                new Orgs
                {
                    sourcedId = vestigingModel.Vestiging.Uuid.ToString(),
                    name = vestigingModel.Vestiging.Naam,
                    type = "school"
                }
            ];
        }

        private string GetVestigingsIds()
        {
            StringBuilder result = new StringBuilder(vestigingModel.Vestiging.Afkorting.Length * 3);
            foreach (char c in vestigingModel.Vestiging.Afkorting)
            {
                int x = c;
                result.Append(x.ToString("000"));
            }

            return result.ToString().TrimStart('0');
        }
    }
}
