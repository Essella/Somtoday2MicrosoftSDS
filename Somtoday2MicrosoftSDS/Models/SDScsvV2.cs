using CsvHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Somtoday2MicrosoftSDS.Models
{
    internal sealed class SDScsvV2
    {

        // https://learn.microsoft.com/en-us/schooldatasync/sds-v2.1-csv-file-format
        public List<Classes> Classes { get; set; } = [];          //optional/required
        public List<Enrollments> Enrollments { get; set; } = [];  //optional/required
        public List<Orgs> Orgs { get; set; } = [];    //required
        public List<Relationships> Relationships { get; set; } = [];  //optional/required when syncing guardians
        public List<Roles> Roles { get; set; } = [];  //required
        public List<Users> Users { get; set; } = [];  //required

//public List<Courses> Courses { get; set; }          //optional (If your Classes.csv data contains links to courses, the corresponding data should be provided to avoid error messages when processing data.) 

    }

    /*
    public class AcademicSessions
    {
        public string sourcedId { get; set; }
        public string title { get; set; }
        public string type { get; set; }
        public int schoolYear { get; set; }
        public DateTime startDate { get; set; }
        public DateTime endDate { get; set; }
    }

    public class AcademicSessionsClassMap : ClassMap<AcademicSessions>
    {
        public AcademicSessionsClassMap()
        {
            Map(m => m.sourcedId).Name("sourcedId");
            Map(m => m.title).Name("title");
            Map(m => m.type).Name("type");
            Map(m => m.schoolYear).Name("schoolYear");
            Map(m => m.startDate).Name("startDate");
            Map(m => m.endDate).Name("endDate");
        }
    }
    */

    [CsvRecord]
    public partial class Classes
    {
        [CsvColumn("sourcedId", Index = 0)]
        public string sourcedId { get; set; }
        [CsvColumn("orgSourcedId", Index = 1)]
        public string orgSourcedId { get; set; }
        [CsvColumn("title", Index = 2)]
        public string title { get; set; }
        [CsvColumn("sessionSourcedIds", Index = 3)]
        public string sessionSourcedIds { get; set; }
        [CsvColumn("courseSourcedId", Index = 4)]
        public string courseSourcedId { get; set; }
    }



    [CsvRecord]
    public partial class Courses
    {
        [CsvColumn("sourcedId", Index = 0)]
        public string sourcedId { get; set; }
        [CsvColumn("orgSourcedId", Index = 1)]
        public int orgSourcedId { get; set; }
        [CsvColumn("title", Index = 2)]
        public string title { get; set; }
        [CsvColumn("schoolYearSourcedId", Index = 3)]
        public string schoolYearSourcedId { get; set; }
        [CsvColumn("code", Index = 4)]
        public string code { get; set; }
        [CsvColumn("subject", Index = 5)]
        public int? subject { get; set; }
        [CsvColumn("grade", Index = 6)]
        public string grade { get; set; }
    }



    /*
    public class Demographics
    {
        public int userSourcedId { get; set; }
        public string sex { get; set; }
        public DateTime birthDate { get; set; }
        public string birthCity { get; set; }
        public string birthState { get; set; }
        public string birthCountry { get; set; }
        public string ethnicityCodes { get; set; }
        public string raceCodes { get; set; }
    }

    public class DemographicsClassMap : ClassMap<Demographics>
    {
        public DemographicsClassMap()
        {
            Map(m => m.userSourcedId).Name("userSourcedId");
            Map(m => m.sex).Name("sex");
            Map(m => m.birthDate).Name("birthDate");
            Map(m => m.birthCity).Name("birthCity");
            Map(m => m.birthState).Name("birthState");
            Map(m => m.birthCountry).Name("birthCountry");
            Map(m => m.ethnicityCodes).Name("ethnicityCodes");
            Map(m => m.raceCodes).Name("raceCodes");
        }
    }
    */

    [CsvRecord]
    public partial class Enrollments
    {
        [CsvColumn("classSourcedId", Index = 0)]
        public string classSourcedId { get; set; }
        [CsvColumn("userSourcedId", Index = 1)]
        public string userSourcedId { get; set; }
        [CsvColumn("role", Index = 2)]
        public string role { get; set; }
    }



    [CsvRecord]
    public partial class Orgs
    {
        [CsvColumn("sourcedId", Index = 0)]
        public string sourcedId { get; set; }
        [CsvColumn("name", Index = 1)]
        public string name { get; set; }
        [CsvColumn("type", Index = 2)]
        public string type { get; set; }
        [CsvColumn("parentSourcedId", Index = 3)]
        public int? parentSourcedId { get; set; }

        /*public enum type
        {
            school,
            department,
            district,
            local,
            state,
            national,
            departmentOfEducation,
            ministryOfEducation,
            university,
            college,
            campus,
            adultEducation,
            municipality,
            academicTrust,
            localAuthority,
            region,
            division,
            province,
            researchCenter,
            program
        }*/
    }



    [CsvRecord]
    public partial class Relationships
    {
        [CsvColumn("userSourcedId", Index = 0)]
        public string userSourcedId { get; set; }
        [CsvColumn("relationshipUserSourcedId", Index = 1)]
        public string relationshipUserSourcedId { get; set; }
        [CsvColumn("relationshipRole", Index = 2)]
        public string relationshipRole { get; set; }
    }



    [CsvRecord]
    public partial class Roles
    {
        [CsvColumn("userSourcedId", Index = 0)]
        public string userSourcedId { get; set; }
        [CsvColumn("orgSourcedId", Index = 1)]
        public string orgSourcedId { get; set; }
        [CsvColumn("role", Index = 2)]
        public string role { get; set; }
        //public string sessionSourcedId { get; set; }
        //public string grade { get; set; }
        //public bool isPrimary { get; set; }
        //public DateTime roleStartDate { get; set; }
        //public DateTime roleEndDate { get; set; }
    }



    /*
    public class UserFlags
    {
        public int userSourcedId { get; set; }
        public string flag { get; set; }
    }

    public class UserFlagsClassMap : ClassMap<UserFlags>
    {
        public UserFlagsClassMap()
        {
            Map(m => m.userSourcedId).Name("userSourcedId");
            Map(m => m.flag).Name("flag");
        }
    }
    */

    [CsvRecord]
    public partial class Users
    {
        [CsvColumn("sourcedId", Index = 0)]
        public string sourcedId { get; set; }
        [CsvColumn("username", Index = 1)]
        public string username { get; set; }
        [CsvColumn("givenName", Index = 2)]
        public string givenName { get; set; }
        [CsvColumn("familyName", Index = 3)]
        public string familyName { get; set; }
        [CsvColumn("password", Index = 4)]
        public string password { get; set; }
        [CsvColumn("activeDirectoryMatchId", Index = 5)]
        public string activeDirectoryMatchId { get; set; }
        [CsvColumn("email", Index = 6)]
        public string email { get; set; }
        [CsvColumn("phone", Index = 7)]
        public string phone { get; set; }
        [CsvColumn("sms", Index = 8)]
        public string sms { get; set; }
    }




}
