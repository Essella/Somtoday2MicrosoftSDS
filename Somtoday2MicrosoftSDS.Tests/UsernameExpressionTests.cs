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
