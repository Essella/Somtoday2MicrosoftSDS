using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class SDScsvHelperV1
    {
        private readonly SettingsHelper sh = new SettingsHelper();
        private readonly ResolvedExportPopulation population;
        private readonly DateOnly runDate;

        public SDScsvHelperV1(ResolvedExportPopulation population, DateOnly runDate)
        {
            this.population = population;
            this.runDate = runDate;
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

            var guardianInfo = GetGuardiansAndRelationships();

            result.User = guardianInfo.Guardians;
            result.Guardianrelationship = guardianInfo.Guardianrelationships;

            return result;
        }

        private (List<Guardian> Guardians, List<GuardianRelationship> Guardianrelationships) GetGuardiansAndRelationships()
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

        private (List<Section> Sections, List<Teacher> Teachers, List<Student> Students, List<TeacherRoster> TeacherRoster, List<StudentEnrollment> StudentEnrollments) GetClassesAndEnrollments()
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

        private List<School> GetSchools()
        {
            return
            [
                new School
                {
                    SISid = population.Vestiging.Uuid.ToString(),
                    Name = population.Vestiging.Naam
                }
            ];
        }
    }
}
