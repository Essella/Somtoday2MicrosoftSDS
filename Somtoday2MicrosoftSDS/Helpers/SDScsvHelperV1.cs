using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class SDScsvHelperV1
    {
        private readonly SettingsHelper sh = new SettingsHelper();
        private readonly IReadOnlyList<ResolvedExportPopulation> populations;
        private readonly DateOnly runDate;

        public SDScsvHelperV1(ResolvedExportPopulation population, DateOnly runDate)
            : this([population], runDate)
        {
        }

        public SDScsvHelperV1(
            IReadOnlyList<ResolvedExportPopulation> populations,
            DateOnly runDate)
        {
            this.populations = populations ?? throw new ArgumentNullException(nameof(populations));
            this.runDate = runDate;
        }

        internal SDScsvV1 ConvertToSDSCSV()
        {
            SDScsvV1 result = new();
            Dictionary<string, School> schools = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Guid> sectionSourceIds = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Guardian> guardians = new(StringComparer.Ordinal);
            HashSet<string> teacherRows = new(StringComparer.Ordinal);
            HashSet<string> studentRows = new(StringComparer.Ordinal);
            HashSet<string> teacherRosters = new(StringComparer.Ordinal);
            HashSet<string> studentEnrollments = new(StringComparer.Ordinal);
            HashSet<string> guardianRelationships = new(StringComparer.Ordinal);

            foreach (ResolvedExportPopulation population in populations)
            {
                School school = GetSchool(population);
                if (schools.TryGetValue(school.SISid, out School existingSchool))
                {
                    if (!string.Equals(existingSchool.Name, school.Name, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "One Somtoday location maps to conflicting SDS V1 school records");
                    }
                }
                else
                {
                    schools.Add(school.SISid, school);
                    result.Schools.Add(school);
                }

                var classesInfo = GetClassesAndEnrollments(population);
                for (int index = 0; index < classesInfo.Sections.Count; index++)
                {
                    Section section = classesInfo.Sections[index];
                    Guid sourceClassUuid = population.Classes[index].Source.Uuid;
                    if (sectionSourceIds.TryGetValue(section.SISid, out Guid existingSourceClassUuid))
                    {
                        if (existingSourceClassUuid != sourceClassUuid)
                        {
                            throw new InvalidOperationException(
                                "Multiple Somtoday classes map to the same SDS V1 class identifier");
                        }
                    }
                    else
                    {
                        sectionSourceIds.Add(section.SISid, sourceClassUuid);
                        result.Sections.Add(section);
                    }
                }

                foreach (Teacher teacher in classesInfo.Teachers)
                {
                    if (teacherRows.Add(CompositeKey(teacher.SISid, teacher.SISSchoolid)))
                    {
                        result.Teachers.Add(teacher);
                    }
                }

                foreach (Student student in classesInfo.Students)
                {
                    if (studentRows.Add(CompositeKey(student.SISid, student.SISSchoolid)))
                    {
                        result.Students.Add(student);
                    }
                }

                foreach (TeacherRoster roster in classesInfo.TeacherRoster)
                {
                    if (teacherRosters.Add(CompositeKey(roster.SISSectionid, roster.SISTeacherid)))
                    {
                        result.TeacherRosters.Add(roster);
                    }
                }

                foreach (StudentEnrollment enrollment in classesInfo.StudentEnrollments)
                {
                    if (studentEnrollments.Add(CompositeKey(enrollment.SISSectionid, enrollment.SISStudentid)))
                    {
                        result.StudentEnrollments.Add(enrollment);
                    }
                }

                var guardianInfo = GetGuardiansAndRelationships(population);
                foreach (Guardian guardian in guardianInfo.Guardians)
                {
                    if (guardians.TryGetValue(guardian.SISid, out Guardian existingGuardian))
                    {
                        if (!GuardianRowsEqual(existingGuardian, guardian))
                        {
                            throw new InvalidOperationException(
                                "One Somtoday guardian maps to conflicting SDS V1 user records");
                        }
                    }
                    else
                    {
                        guardians.Add(guardian.SISid, guardian);
                        result.User.Add(guardian);
                    }
                }

                foreach (GuardianRelationship relationship in guardianInfo.Guardianrelationships)
                {
                    if (guardianRelationships.Add(CompositeKey(
                        relationship.SISid,
                        relationship.Email,
                        relationship.Role)))
                    {
                        result.Guardianrelationship.Add(relationship);
                    }
                }
            }

            return result;
        }

        private static string CompositeKey(params string[] values)
        {
            return string.Join('\u001f', values);
        }

        private static bool GuardianRowsEqual(Guardian first, Guardian second)
        {
            return string.Equals(first.Email, second.Email, StringComparison.Ordinal)
                && string.Equals(first.FirstName, second.FirstName, StringComparison.Ordinal)
                && string.Equals(first.LastName, second.LastName, StringComparison.Ordinal)
                && string.Equals(first.Phone, second.Phone, StringComparison.Ordinal);
        }

        private static (List<Guardian> Guardians, List<GuardianRelationship> Guardianrelationships) GetGuardiansAndRelationships(
            ResolvedExportPopulation population)
        {
            List<Guardian> guardians = [];
            List<GuardianRelationship> guardianrelationships = [];

            foreach (ResolvedGuardian resolvedGuardian in population.Guardians)
            {
                OuderVerzorger guardian = resolvedGuardian.Source;
                foreach (Guid studentId in resolvedGuardian.StudentIds)
                {
                    guardianrelationships.Add(new GuardianRelationship
                    {
                        SISid = studentId.ToString(),
                        Email = guardian.Emailadres
                    });
                }

                guardians.Add(new Guardian
                {
                    SISid = guardian.Uuid.ToString(),
                    Email = guardian.Emailadres,
                    FirstName = guardian.Voorletters ?? string.Empty,
                    Phone = GuardianExportPolicy.GetPhone(guardian),
                    LastName = GuardianExportPolicy.GetFamilyName(guardian)
                });
            }

            return (guardians, guardianrelationships);
        }

        private (List<Section> Sections, List<Teacher> Teachers, List<Student> Students, List<TeacherRoster> TeacherRoster, List<StudentEnrollment> StudentEnrollments) GetClassesAndEnrollments(
            ResolvedExportPopulation population)
        {
            string currentSchoolyear = AmsterdamTimeHelper.GetSchoolYear(runDate);
            List<Section> sections = [];
            List<Teacher> teachers = [];
            List<Student> students = [];
            List<TeacherRoster> teacherRoster = [];
            List<StudentEnrollment> studentEnrollments = [];

            HashSet<Guid> emittedTeacherIds = [];
            HashSet<Guid> emittedStudentIds = [];

            string vestigingsAfkorting = population.Vestiging.Afkorting;
            string vestigingsAfkortingLower = vestigingsAfkorting.ToLower();
            string vestigingUuid = population.Vestiging.Uuid.ToString();

            foreach (ResolvedClass resolvedClass in population.Classes)
            {
                Lesgroep sourceClass = resolvedClass.Source;
                string sectionName = BusinessLogicHelper.GetFilteredName(sourceClass.Naam);
                string sectionId = (sourceClass.Naam.StartsWith(vestigingsAfkorting, StringComparison.CurrentCultureIgnoreCase) ? sectionName : vestigingsAfkortingLower + sectionName) + currentSchoolyear;

                Section section = new Section
                {
                    SISSchoolid = vestigingUuid,
                    SISid = sectionId,
                    Name = sectionName,
                    Number = sourceClass.Uuid.ToString(),
                    CourseName = sourceClass.Vaknaam,
                    CourseDescription = sourceClass.Onderwijssoort
                };
                sections.Add(section);

                foreach (Medewerker teacher in resolvedClass.Teachers)
                {
                    string teacherId = teacher.Uuid.ToString();
                    teacherRoster.Add(new TeacherRoster
                    {
                        SISTeacherid = teacherId,
                        SISSectionid = section.SISid
                    });

                    if (emittedTeacherIds.Add(teacher.Uuid))
                    {
                        teachers.Add(new Teacher
                        {
                            SISid = teacherId,
                            SISSchoolid = vestigingUuid,
                            Username = sh.ReplaceTeacherProperty(SettingsHelper.OutputFormatUsernameTeacher, teacher)
                        });
                    }
                }

                foreach (Leerling student in resolvedClass.Students)
                {
                    string studentId = student.Uuid.ToString();
                    studentEnrollments.Add(new StudentEnrollment
                    {
                        SISStudentid = studentId,
                        SISSectionid = section.SISid
                    });

                    if (emittedStudentIds.Add(student.Uuid))
                    {
                        students.Add(new Student
                        {
                            SISid = studentId,
                            SISSchoolid = vestigingUuid,
                            Username = sh.ReplaceStudentProperty(SettingsHelper.OutputFormatUsernameStudent, student)
                        });
                    }
                }
            }

            return (sections, teachers, students, teacherRoster, studentEnrollments);
        }

        private static School GetSchool(ResolvedExportPopulation population)
        {
            return new School
            {
                SISid = population.Vestiging.Uuid.ToString(),
                Name = population.Vestiging.Naam
            };
        }
    }
}
