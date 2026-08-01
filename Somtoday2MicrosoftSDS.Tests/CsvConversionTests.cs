using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public class CsvConversionTests
{
    private static readonly DateOnly RunDate = new(2026, 8, 1);

    [Fact]
    public void GuardianFieldsAreMappedConsistentlyInV1AndV2()
    {
        OuderVerzorger guardian = CreateGuardian(
            email: "guardian@example.test",
            wantsEmail: true,
            initials: "A.B.",
            prefix: "van",
            surname: "Test");
        guardian.Telefoonnummer = "06 12345678";
        guardian.UitgebreidMobielNummer = "06-12345678";
        guardian.UitgebreidTelefoonnummer = "020 1234567";

        OuderVerzorger guardianWithoutInitials = CreateGuardian(
            email: "no-initials@example.test",
            wantsEmail: true,
            initials: string.Empty,
            prefix: string.Empty,
            surname: "NoInitials");

        VestigingModel model = CreateModel(guardian, guardianWithoutInitials);
        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(model);
        SDScsvV1 v1 = new SDScsvHelperV1(population, RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2(population, RunDate).ConvertToSDSCSV();

        Guardian v1Guardian = Assert.Single(v1.User, user => user.SISid == guardian.Uuid.ToString());
        Assert.Equal("guardian@example.test", v1Guardian.Email);
        Assert.Equal("A.B.", v1Guardian.FirstName);
        Assert.Equal("van Test", v1Guardian.LastName);
        Assert.Equal("+31612345678", v1Guardian.Phone);

        Users v2Guardian = Assert.Single(v2.Users, user => user.sourcedId == guardian.Uuid.ToString());
        Assert.Equal("guardian@example.test", v2Guardian.username);
        Assert.Equal("guardian@example.test", v2Guardian.email);
        Assert.Equal("A.B.", v2Guardian.givenName);
        Assert.Equal("van Test", v2Guardian.familyName);
        Assert.Equal("+31612345678", v2Guardian.phone);

        Assert.Equal(
            string.Empty,
            Assert.Single(v1.User, user => user.SISid == guardianWithoutInitials.Uuid.ToString()).FirstName);
        Assert.Equal(
            string.Empty,
            Assert.Single(v2.Users, user => user.sourcedId == guardianWithoutInitials.Uuid.ToString()).givenName);
    }

    [Theory]
    [InlineData(false, "guardian@example.test")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public void GuardianWithoutConsentOrUsableEmailIsOmittedEverywhere(bool wantsEmail, string email)
    {
        OuderVerzorger guardian = CreateGuardian(
            email,
            wantsEmail,
            initials: "A.",
            prefix: string.Empty,
            surname: "Test");
        VestigingModel model = CreateModel(guardian);

        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(model);
        SDScsvV1 v1 = new SDScsvHelperV1(population, RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2(population, RunDate).ConvertToSDSCSV();
        string guardianId = guardian.Uuid.ToString();

        Assert.DoesNotContain(v1.User, user => user.SISid == guardianId);
        Assert.DoesNotContain(v1.Guardianrelationship, relationship => relationship.Email == email);
        Assert.DoesNotContain(v2.Users, user => user.sourcedId == guardianId);
        Assert.DoesNotContain(v2.Roles, role => role.userSourcedId == guardianId);
        Assert.DoesNotContain(v2.Relationships, relationship => relationship.relationshipUserSourcedId == guardianId);
    }

    [Fact]
    public void GuardianPhoneUsesPreferredNonSecretValidExplicitNumber()
    {
        OuderVerzorger guardian = CreateGuardian(
            "guardian@example.test",
            wantsEmail: true,
            initials: "A.",
            prefix: string.Empty,
            surname: "Test");
        guardian.Telefoonnummer = "020 7654321";
        guardian.UitgebreidMobielNummer = "invalid";
        guardian.UitgebreidTelefoonnummer = "020 1234567";
        guardian.UitgebreidMobielWerkNummer = "010 7654321";

        Assert.Equal("+31201234567", GuardianExportPolicy.GetPhone(guardian));

        guardian.UitgebreidMobielNummer = "06 12345678";
        Assert.Equal("+31612345678", GuardianExportPolicy.GetPhone(guardian));

        guardian.Mobielwerknummer_geheim = true;
        Assert.Equal("+31201234567", GuardianExportPolicy.GetPhone(guardian));

        guardian.Telefoonnummer_geheim = true;
        Assert.Equal(string.Empty, GuardianExportPolicy.GetPhone(guardian));
    }

    [Fact]
    public void GuardianPhoneIgnoresUnmatchedFallbackAndUsesExplicitNumber()
    {
        OuderVerzorger guardian = CreateGuardian(
            "guardian@example.test",
            wantsEmail: true,
            initials: "A.",
            prefix: string.Empty,
            surname: "Test");
        guardian.Telefoonnummer = "088 0000000";
        guardian.UitgebreidMobielWerkNummer = "010 7654321";

        Assert.Equal("+31107654321", GuardianExportPolicy.GetPhone(guardian));
    }

    [Theory]
    [InlineData("06 12345678", "+31612345678")]
    [InlineData("0032 12 34 56 78", "+3212345678")]
    [InlineData("+12", "+12")]
    [InlineData("+1", "")]
    [InlineData("+123456789012345", "+123456789012345")]
    [InlineData("+", "")]
    [InlineData("++31612345678", "")]
    [InlineData("+٣١٦١٢٣٤٥٦٧٨", "")]
    [InlineData("+1234567890123456", "")]
    [InlineData("letters", "")]
    [InlineData("+012345678", "")]
    public void PhoneNormalizationEmitsOnlyExactE164OrEmpty(string input, string expected)
    {
        Assert.Equal(expected, BusinessLogicHelper.NormaliseerTelefoonnummerNaarE164(input));
    }

    [Fact]
    public void InvalidGuardianPhoneIsEmptyInBothVersionsWithoutRemovingGuardianOrRelationships()
    {
        OuderVerzorger guardian = CreateGuardian(
            "guardian@example.test",
            wantsEmail: true,
            initials: "A.",
            prefix: string.Empty,
            surname: "Test");
        guardian.Telefoonnummer = "++31612345678";
        guardian.UitgebreidMobielNummer = "++31612345678";

        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(CreateModel(guardian));
        SDScsvV1 v1 = new SDScsvHelperV1(population, RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2(population, RunDate).ConvertToSDSCSV();
        string guardianId = guardian.Uuid.ToString();

        Assert.Equal(string.Empty, Assert.Single(v1.User, user => user.SISid == guardianId).Phone);
        Assert.Single(v1.Guardianrelationship);
        Assert.Equal(string.Empty, Assert.Single(v2.Users, user => user.sourcedId == guardianId).phone);
        Assert.Single(v2.Relationships);
    }

    [Fact]
    public void V1AndV2UseTheSameRunDateForClassIds()
    {
        VestigingModel model = CreateModel();

        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(model);
        SDScsvV1 v1 = new SDScsvHelperV1(population, RunDate).ConvertToSDSCSV();
        SDScsvV2 v2 = new SDScsvHelperV2(population, RunDate).ConvertToSDSCSV();

        string v1ClassId = Assert.Single(v1.Sections).SISid;
        string v2ClassId = Assert.Single(v2.Classes).sourcedId;
        Assert.Equal(v1ClassId, v2ClassId);
        Assert.EndsWith("2026-2027", v1ClassId, StringComparison.Ordinal);
    }

    [Fact]
    public void V1AndV2PreserveTheirExistingLocationPrefixChecks()
    {
        VestigingModel model = CreateModel();
        model.Vestiging.Afkorting = "A_";
        model.Lesgroepen[0].Naam = "A B";

        ResolvedExportPopulation population = ExportPopulationResolver.Resolve(model);
        string v1ClassId = Assert.Single(
            new SDScsvHelperV1(population, RunDate).ConvertToSDSCSV().Sections).SISid;
        string v2ClassId = Assert.Single(
            new SDScsvHelperV2(population, RunDate).ConvertToSDSCSV().Classes).sourcedId;

        Assert.Equal("a_A_B2026-2027", v1ClassId);
        Assert.Equal("A_B2026-2027", v2ClassId);
    }

    private static VestigingModel CreateModel(params OuderVerzorger[] guardians)
    {
        Guid teacherId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid locationId = Guid.NewGuid();

        foreach (OuderVerzorger guardian in guardians)
        {
            guardian.Leerlingen_van_vestiging = [studentId];
        }

        return new VestigingModel
        {
            Vestiging = new Vestiging
            {
                Uuid = locationId,
                Naam = "Test location",
                Afkorting = "LOC"
            },
            Lesgroepen =
            [
                new Lesgroep
                {
                    Uuid = Guid.NewGuid(),
                    Naam = "Class A",
                    Docenten = [teacherId],
                    Leerlingen = [new LeerlingVestiging { Uuid = studentId }],
                    Vaknaam = "Subject",
                    Onderwijssoort = "Education"
                }
            ],
            Medewerkers =
            [
                new Medewerker
                {
                    Uuid = teacherId,
                    Emailadres = "teacher@example.test"
                }
            ],
            Leerlingen =
            [
                new Leerling
                {
                    Uuid = studentId,
                    Emailadres = "student@example.test"
                }
            ],
            OuderVerzorgers = [.. guardians]
        };
    }

    private static OuderVerzorger CreateGuardian(
        string email,
        bool wantsEmail,
        string initials,
        string prefix,
        string surname)
    {
        return new OuderVerzorger
        {
            Uuid = Guid.NewGuid(),
            Emailadres = email,
            WenstContactViaEMail = wantsEmail,
            Voorletters = initials,
            Voorvoegsel = prefix,
            Achternaam = surname,
            Telefoonnummer = string.Empty,
            UitgebreidMobielNummer = string.Empty,
            UitgebreidTelefoonnummer = string.Empty,
            UitgebreidMobielWerkNummer = string.Empty
        };
    }
}
