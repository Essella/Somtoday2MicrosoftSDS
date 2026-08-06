using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class UsernameExpressionTests
{
    private readonly SettingsHelper helper = new();

    [Fact]
    public void BareTeacherAndStudentPropertiesAreNormalizedAndEvaluated()
    {
        Medewerker teacher = new() { Emailadres = "teacher@school.test" };
        Leerling student = new() { Leerlingnummer = 12345 };

        Assert.Equal(
            "teacher@school.test",
            helper.ReplaceTeacherProperty(SettingsHelper.NormalizeFormat("Emailadres"), teacher));
        Assert.Equal(
            "12345",
            helper.ReplaceStudentProperty(SettingsHelper.NormalizeFormat("Leerlingnummer"), student));
    }

    [Fact]
    public void MultipleExpressionsCanBeCombinedWithLiteralText()
    {
        Medewerker teacher = new()
        {
            Voorletters = "J",
            Achternaam = "Jansen"
        };

        Assert.Equal(
            "J.Jansen",
            helper.ReplaceTeacherProperty(
                SettingsHelper.NormalizeFormat("{user.Voorletters}.{user.Achternaam}"),
                teacher));
    }

    [Theory]
    [InlineData("idm-{user.Emailadres}", "idm-Voor.Naam@oud.example")]
    [InlineData("{user.Emailadres}-student", "Voor.Naam@oud.example-student")]
    [InlineData("{user.Emailadres.Split(\"@\")[0]}@school.nl", "Voor.Naam@school.nl")]
    [InlineData("{user.Emailadres.Replace(\"@oud.example\", \"@nieuw.example\")}", "Voor.Naam@nieuw.example")]
    [InlineData("{user.Emailadres.Split(\"@\")[0].Replace(\".\", \"_\").ToLower()}@school.nl", "voor_naam@school.nl")]
    public void IdmTemplatesSupportLiteralsAndCompiledStringOperations(string format, string expected)
    {
        Medewerker teacher = new() { Emailadres = "Voor.Naam@oud.example" };

        string normalized = SettingsHelper.NormalizeFormat(format);

        Assert.Equal(format, normalized);
        Assert.Equal(expected, helper.ReplaceTeacherProperty(normalized, teacher));
    }

    [Fact]
    public void StartupValidationCompilesWithoutExecutingAgainstSyntheticData()
    {
        string dataDependentTeacherFormat = SettingsHelper.NormalizeFormat(
            "{user.Emailadres.Split(\"@\")[1]}");
        string dataDependentStudentFormat = SettingsHelper.NormalizeFormat(
            "{user.Emailadres.Split(\"@\")[1]}");

        Assert.True(helper.ValidateUsernameFormats(
            dataDependentTeacherFormat,
            dataDependentStudentFormat));
    }

    [Theory]
    [InlineData("naam@oud.example", "naam")]
    [InlineData(null, "")]
    public void ConditionalTemplateHandlesOptionalSourceData(string email, string expected)
    {
        Medewerker teacher = new() { Emailadres = email };
        string format = SettingsHelper.NormalizeFormat(
            "{user.Emailadres != null && user.Emailadres.Contains(\"@\") ? user.Emailadres.Split(\"@\")[0] : \"\"}");

        Assert.True(helper.ValidateUsernameFormats(
            format,
            SettingsHelper.NormalizeFormat("Emailadres")));
        Assert.Equal(expected, helper.ReplaceTeacherProperty(format, teacher));
    }

    [Theory]
    [InlineData("prefix-{user.DoesNotExist}")]
    [InlineData("prefix-{user.Emailadres.UnknownMethod()}")]
    [InlineData("prefix-{user.Emailadres")]
    [InlineData("prefix-user.Emailadres}")]
    public void StartupValidationRejectsInvalidTemplateStructureOrExpressions(string format)
    {
        Assert.False(helper.ValidateUsernameFormats(
            SettingsHelper.NormalizeFormat(format),
            SettingsHelper.NormalizeFormat("Emailadres")));
    }

    [Fact]
    public void DocumentedToLowerExpressionIsSupported()
    {
        Leerling student = new() { Emailadres = "J.Jansen@School.nl" };

        Assert.Equal(
            "j.jansen@school.nl",
            helper.ReplaceStudentProperty(
                SettingsHelper.NormalizeFormat("{user.Emailadres.ToLower()}"),
                student));
    }

    [Fact]
    public void DocumentedConcatenationExpressionIsSupported()
    {
        Medewerker teacher = new()
        {
            Voorletters = "J",
            Achternaam = "Jansen"
        };

        Assert.Equal(
            "J.Jansen@school.nl",
            helper.ReplaceTeacherProperty(
                SettingsHelper.NormalizeFormat(
                    "{user.Voorletters + \".\" + user.Achternaam + \"@school.nl\"}"),
                teacher));
    }
}
