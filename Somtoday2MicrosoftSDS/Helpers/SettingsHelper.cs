using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    public partial class SettingsHelper
    {
        public static string OutputFormatUsernameTeacher { get; private set; } = NormalizeFormat("Emailadres");
        public static string OutputFormatUsernameStudent { get; private set; } = NormalizeFormat("Emailadres");

        private static readonly ConcurrentDictionary<string, Func<Medewerker, string>> TeacherFormatterCache = new();
        private static readonly ConcurrentDictionary<string, Func<Leerling, string>> StudentFormatterCache = new();

        private readonly ILogger<SettingsHelper> _logger;

        public SettingsHelper(ILogger<SettingsHelper> logger = null)
        {
            _logger = logger ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SettingsHelper>();
        }

        public static void Initialize(IConfiguration configuration)
        {
            OutputFormatUsernameTeacher = NormalizeFormat(configuration["UsernameFormat:Teacher"] ?? "Emailadres");
            OutputFormatUsernameStudent = NormalizeFormat(configuration["UsernameFormat:Student"] ?? "Emailadres");
        }

        internal bool ValidateUsernameFormat()
        {
            return ValidateUsernameFormats(
                OutputFormatUsernameTeacher,
                OutputFormatUsernameStudent);
        }

        internal bool ValidateUsernameFormats(string teacherFormat, string studentFormat)
        {
            bool success = true;
            try
            {
                CompileTeacherFormatter(teacherFormat);
            }
            catch (Exception ex)
            {
                success = false;
                _logger?.LogError(
                    "OutputFormatUsernameTeacher validation failed ({Error})",
                    SafeExceptionSummary.Create(ex));
            }

            try
            {
                CompileStudentFormatter(studentFormat);
            }
            catch (Exception ex)
            {
                success = false;
                _logger?.LogError(
                    "OutputFormatUsernameStudent validation failed ({Error})",
                    SafeExceptionSummary.Create(ex));
            }

            return success;
        }

        internal string ReplaceTeacherProperty(string format, Medewerker userobj)
        {
            return ReplaceTeacherUserProperty(format, userobj);
        }

        internal string ReplaceStudentProperty(string format, Leerling userobj)
        {
            return ReplaceStudentUserProperty(format, userobj);
        }

        private static string ReplaceTeacherUserProperty(string value, Medewerker userobj)
        {
            return TeacherFormatterCache.GetOrAdd(value, CreateFormatter<Medewerker>)(userobj);
        }

        private static string ReplaceStudentUserProperty(string value, Leerling userobj)
        {
            return StudentFormatterCache.GetOrAdd(value, CreateFormatter<Leerling>)(userobj);
        }

        private static void CompileTeacherFormatter(string value)
        {
            _ = TeacherFormatterCache.GetOrAdd(value, CreateFormatter<Medewerker>);
        }

        private static void CompileStudentFormatter(string value)
        {
            _ = StudentFormatterCache.GetOrAdd(value, CreateFormatter<Leerling>);
        }

        private static Func<TUser, string> CreateFormatter<TUser>(string format)
        {
            MatchCollection matches = TemplateExpressionRegex().Matches(format);
            if (matches.Count == 0)
            {
                EnsureLiteralHasNoBraces(format);
                return _ => format;
            }

            var segments = new List<Func<TUser, string>>();
            int currentIndex = 0;

            foreach (Match match in matches)
            {
                if (match.Index > currentIndex)
                {
                    string literal = format[currentIndex..match.Index];
                    EnsureLiteralHasNoBraces(literal);
                    segments.Add(_ => literal);
                }

                string expression = match.Groups["exp"].Value;
                segments.Add(CreateExpressionAccessor<TUser>(expression));
                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < format.Length)
            {
                string literal = format[currentIndex..];
                EnsureLiteralHasNoBraces(literal);
                segments.Add(_ => literal);
            }

            return user =>
            {
                var builder = new StringBuilder(format.Length);
                foreach (Func<TUser, string> segment in segments)
                {
                    builder.Append(segment(user));
                }

                return builder.ToString();
            };
        }

        private static void EnsureLiteralHasNoBraces(string literal)
        {
            if (literal.IndexOfAny(['{', '}']) >= 0)
            {
                throw new FormatException("Username template contains an unmatched brace");
            }
        }

        private static Func<TUser, string> CreateExpressionAccessor<TUser>(string expression)
        {
            ParameterExpression parameter = Expression.Parameter(typeof(TUser), "user");
            LambdaExpression parsedExpression = System.Linq.Dynamic.Core.DynamicExpressionParser.ParseLambda(new[] { parameter }, null, expression);
            UnaryExpression boxedBody = Expression.Convert(parsedExpression.Body, typeof(object));
            Func<TUser, object> accessor = Expression.Lambda<Func<TUser, object>>(boxedBody, parameter).Compile();

            return user => (accessor(user) ?? string.Empty).ToString();
        }

        internal static string NormalizeFormat(string configuredValue)
        {
            if (configuredValue.IndexOfAny(['{', '}']) >= 0)
            {
                return configuredValue;
            }

            return "{user." + configuredValue + "}";
        }

        [GeneratedRegex(@"{(?<exp>[^}]+)}")]
        private static partial Regex TemplateExpressionRegex();
    }
}
