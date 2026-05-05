using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SyncIdPreview.Helpers
{
    public static partial class BusinessLogicHelper
    {
        public static string NormaliseerTelefoonnummerNaarE164(string invoer)
        {
            if (string.IsNullOrEmpty(invoer))
            {
                return string.Empty;
            }

            string genormaliseerd = KeepDigitsAndPlus(invoer);

            // Als het nummer 9 cijfers bevat en niet met '0' begint, aannemen dat het een mobiel nummer zonder voorloopnul is
            if (genormaliseerd.Length == 9 && !genormaliseerd.StartsWith('0'))
            {
                genormaliseerd = "0" + genormaliseerd;
            }

            // Als het nummer begint met 00, vervang dit door een +
            if (genormaliseerd.StartsWith("00", StringComparison.Ordinal))
            {
                genormaliseerd = "+" + genormaliseerd[2..];
            }
            else if (genormaliseerd.StartsWith('0'))
            {
                // Als het nummer begint met een 0, vervang deze door de Nederlandse landcode +31
                genormaliseerd = "+31" + genormaliseerd[1..];
            }
            else if (!genormaliseerd.StartsWith('+'))
            {
                // Als het nummer niet met een '+' begint en ook niet met '00' of '0', is het waarschijnlijk incorrect
                genormaliseerd = string.Empty;
            }

            ReadOnlySpan<char> nummerZonderPlus = genormaliseerd.AsSpan();
            while (!nummerZonderPlus.IsEmpty && nummerZonderPlus[0] == '+')
            {
                nummerZonderPlus = nummerZonderPlus[1..];
            }

            if (nummerZonderPlus.Length > 15 || !ContainsOnlyDigits(nummerZonderPlus))
            {
                genormaliseerd = string.Empty;
            }

            return genormaliseerd;
        }

        public static string GetFilteredName(string input)
        {
            // Alles met een spatie of verboden teken voor OneDrive wordt omgezet naar _
            string temp = InvalidOneDriveNameCharsRegex().Replace(input, "_");
            return RemoveDiacritics(temp);
        }

        public static string RemoveDiacritics(string text)
        {
            string normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(normalizedString.Length);

            foreach (char c in normalizedString)
            {
                UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string KeepDigitsAndPlus(ReadOnlySpan<char> input)
        {
            var builder = new StringBuilder(input.Length);

            foreach (char c in input)
            {
                if (char.IsDigit(c) || c == '+')
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static bool ContainsOnlyDigits(ReadOnlySpan<char> value)
        {
            foreach (char c in value)
            {
                if (!char.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        [GeneratedRegex(@"[^\S]|[\~\""\#\%\&\*\:\<\>\?\/\\{\|}\.\[\]]")]
        private static partial Regex InvalidOneDriveNameCharsRegex();
    }
}
