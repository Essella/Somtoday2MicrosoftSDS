using SyncIdPreview.Models;

namespace SyncIdPreview.Helpers
{
    internal class SDScsvHelperV1
    {
        private readonly SettingsHelper sh = new SettingsHelper();
        private readonly VestigingModel vestigingModel;

        public SDScsvHelperV1(VestigingModel info)
        {
            vestigingModel = info;
        }

        internal SDScsvV1 ConvertToSDSCSV()
        {
            SDScsvV1 result = new SDScsvV1
            {
                Schools = GetSchools()
            };

            var classesInfo = GetClassesAndEnrollments();

            result.Sections = classesInfo.Sections;
            result.Teachers = classesInfo.Teachers;
            result.Students = classesInfo.Students;
            result.TeacherRosters = classesInfo.TeacherRoster;
            result.StudentEnrollments = classesInfo.StudentEnrollments;

            var guardianInfo = GetGuardiansAndRelationships(classesInfo.Students);

            result.User = guardianInfo.Guardians;
            result.Guardianrelationship = guardianInfo.Guardianrelationships;

            return result;
        }

        private (List<Guardian> Guardians, List<GuardianRelationship> Guardianrelationships) GetGuardiansAndRelationships(List<Student> students)
        {
            List<Guardian> guardians = [];
            List<GuardianRelationship> guardianrelationships = [];
            HashSet<string> studentIds = students.Select(s => s.SISid).ToHashSet(StringComparer.Ordinal);

            foreach (OuderVerzorger ouder in vestigingModel.OuderVerzorgers)
            {
                if (ouder.Leerlingen_van_vestiging?.Count > 0)
                {
                    bool guardianFound = false;

                    foreach (Guid leerling in ouder.Leerlingen_van_vestiging)
                    {
                        string leerlingId = leerling.ToString();
                        if (studentIds.Contains(leerlingId) && !string.IsNullOrEmpty(ouder.Emailadres))
                        {
                            guardianFound = true;
                            guardianrelationships.Add(new GuardianRelationship
                            {
                                SISid = leerlingId,
                                Email = ouder.Emailadres
                            });
                        }
                    }

                    if (guardianFound)
                    {
                        guardians.Add(new Guardian
                        {
                            SISid = ouder.Uuid.ToString(),
                            Email = ouder.Emailadres,
                            FirstName = string.IsNullOrEmpty(ouder.Voorvoegsel) ? (!string.IsNullOrEmpty(ouder.Voorletters) ? ouder.Voorletters : ".") : $"{ouder.Voorvoegsel} {ouder.Achternaam}",
                            Phone = string.IsNullOrEmpty(ouder.Telefoonnummer) ? string.Empty : BusinessLogicHelper.NormaliseerTelefoonnummerNaarE164(ouder.Telefoonnummer),
                            LastName = ouder.Achternaam
                        });
                    }
                }
            }

            return (guardians, guardianrelationships);
        }

        private (List<Section> Sections, List<Teacher> Teachers, List<Student> Students, List<TeacherRoster> TeacherRoster, List<StudentEnrollment> StudentEnrollments) GetClassesAndEnrollments()
        {
            DateTime now = DateTime.Now;
            string currentSchoolyear = now.Month < 8 ? $"{now.Year - 1}-{now.Year}" : $"{now.Year}-{now.Year + 1}";
            List<Section> sections = [];
            List<Teacher> teachers = [];
            List<Student> students = [];
            List<TeacherRoster> teacherRoster = [];
            List<StudentEnrollment> studentEnrollments = [];

            Dictionary<Guid, Medewerker> medewerkersById = ToFirstById(vestigingModel.Medewerkers, m => m.Uuid);
            Dictionary<Guid, Leerling> leerlingenById = ToFirstById(vestigingModel.Leerlingen, s => s.Uuid);
            HashSet<Guid> emittedTeacherIds = [];
            HashSet<Guid> emittedStudentIds = [];

            string vestigingsAfkorting = vestigingModel.Vestiging.Afkorting;
            string vestigingsAfkortingLower = vestigingsAfkorting.ToLower();
            string vestigingUuid = vestigingModel.Vestiging.Uuid.ToString();

            foreach (Lesgroep lesgroep in vestigingModel.Lesgroepen)
            {
                if (!string.IsNullOrEmpty(lesgroep.Naam) && lesgroep.Docenten?.Count > 0 && lesgroep.Leerlingen?.Count > 0)
                {
                    string sectieNaam = BusinessLogicHelper.GetFilteredName(lesgroep.Naam);
                    string sectionId = (lesgroep.Naam.StartsWith(vestigingsAfkorting, StringComparison.CurrentCultureIgnoreCase) ? sectieNaam : vestigingsAfkortingLower + sectieNaam) + currentSchoolyear;

                    Section lg = new Section
                    {
                        SISSchoolid = vestigingUuid,
                        SISid = sectionId,
                        Name = sectieNaam,
                        Number = lesgroep.Uuid.ToString(),
                        CourseName = lesgroep.Vaknaam,
                        CourseDescription = lesgroep.Onderwijssoort
                    };
                    sections.Add(lg);

                    foreach (Guid mw in lesgroep.Docenten)
                    {
                        if (medewerkersById.TryGetValue(mw, out Medewerker currentTeacher))
                        {
                            string teacherId = mw.ToString();
                            teacherRoster.Add(new TeacherRoster
                            {
                                SISTeacherid = teacherId,
                                SISSectionid = lg.SISid
                            });

                            if (emittedTeacherIds.Add(mw))
                            {
                                teachers.Add(new Teacher
                                {
                                    SISid = teacherId,
                                    SISSchoolid = vestigingUuid,
                                    Username = sh.ReplaceTeacherProperty(SettingsHelper.OutputFormatUsernameTeacher, currentTeacher)
                                });
                            }
                        }
                    }

                    foreach (var ll in lesgroep.Leerlingen)
                    {
                        if (leerlingenById.TryGetValue(ll.Uuid, out Leerling currentStudent))
                        {
                            string studentId = ll.Uuid.ToString();
                            studentEnrollments.Add(new StudentEnrollment
                            {
                                SISStudentid = studentId,
                                SISSectionid = lg.SISid
                            });

                            if (emittedStudentIds.Add(ll.Uuid))
                            {
                                students.Add(new Student
                                {
                                    SISid = studentId,
                                    SISSchoolid = vestigingUuid,
                                    Username = sh.ReplaceStudentProperty(SettingsHelper.OutputFormatUsernameStudent, currentStudent)
                                });
                            }
                        }
                    }
                }
            }

            return (sections, teachers, students, teacherRoster, studentEnrollments);
        }

        private List<School> GetSchools()
        {
            return
            [
                new School
                {
                    SISid = vestigingModel.Vestiging.Uuid.ToString(),
                    Name = vestigingModel.Vestiging.Naam
                }
            ];
        }

        private static Dictionary<Guid, TValue> ToFirstById<TValue>(IEnumerable<TValue> values, Func<TValue, Guid> keySelector)
        {
            Dictionary<Guid, TValue> result = [];
            foreach (TValue value in values)
            {
                result.TryAdd(keySelector(value), value);
            }

            return result;
        }
    }
}
