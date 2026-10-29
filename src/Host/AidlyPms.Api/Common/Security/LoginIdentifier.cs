namespace AidlyPms.Api.Common.Security;

public static class LoginIdentifier
{
    public static string[] PhoneCandidates(string identifier)
    {
        if (identifier.Any(c => !char.IsAsciiDigit(c) && c is not '+' and not '-' and not '(' and not ')' && !char.IsWhiteSpace(c))) return [];
        var digits = new string(identifier.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 7) return [];
        var local = digits.StartsWith("880", StringComparison.Ordinal) && digits.Length == 13 ? digits[2..] : digits;
        return local.StartsWith('0') && local.Length == 11 ? [local, "88" + local] : [digits];
    }
}
