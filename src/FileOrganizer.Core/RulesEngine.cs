using System.Globalization;
using System.Text.RegularExpressions;

namespace FileOrganizer.Core;

/// <summary>Evaluates custom rules (§4) against a file.</summary>
public static class RulesEngine
{
    public static bool Matches(OrganizeRule rule, FileInfo file)
    {
        if (!rule.Enabled || rule.Conditions.Count == 0)
            return false;

        var results = rule.Conditions.Select(c => Matches(c, file));
        return rule.Logic == RuleLogic.All ? results.All(x => x) : results.Any(x => x);
    }

    private static bool Matches(RuleCondition c, FileInfo file)
    {
        switch (c.Field)
        {
            case RuleField.Extension:
                var ext = file.Extension.TrimStart('.');
                return CompareText(ext, c);
            case RuleField.FileName:
                return CompareText(file.Name, c);
            case RuleField.SizeBytes:
                return long.TryParse(c.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
                       && (c.Operator == RuleOperator.GreaterThan ? file.Length > size
                           : c.Operator == RuleOperator.LessThan && file.Length < size);
            case RuleField.Created:
                return CompareDate(file.CreationTime, c);
            case RuleField.Modified:
                return CompareDate(file.LastWriteTime, c);
            default:
                return false;
        }
    }

    private static bool CompareText(string actual, RuleCondition c) => c.Operator switch
    {
        RuleOperator.Equals => string.Equals(actual, c.Value, StringComparison.OrdinalIgnoreCase),
        RuleOperator.Contains => actual.Contains(c.Value, StringComparison.OrdinalIgnoreCase),
        RuleOperator.StartsWith => actual.StartsWith(c.Value, StringComparison.OrdinalIgnoreCase),
        RuleOperator.EndsWith => actual.EndsWith(c.Value, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool CompareDate(DateTime actual, RuleCondition c)
    {
        if (!DateTime.TryParse(c.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value))
            return false;
        return c.Operator switch
        {
            RuleOperator.GreaterThan => actual > value,
            RuleOperator.LessThan => actual < value,
            RuleOperator.Equals => actual.Date == value.Date,
            _ => false,
        };
    }

    /// <summary>The date a file is organized by (§5): file timestamps, or a yyyy-MM-dd / dd-MM-yyyy date parsed from the file name.</summary>
    public static DateTime GetFileDate(FileInfo file, DateSource source)
    {
        switch (source)
        {
            case DateSource.Created:
                return file.CreationTime;
            case DateSource.FileName:
                var m = Regex.Match(file.Name, @"(\d{4})[-_](\d{2})[-_](\d{2})|(\d{2})[-_](\d{2})[-_](\d{4})");
                if (m.Success)
                {
                    var parts = m.Value.Split('-', '_').Select(int.Parse).ToArray();
                    var (y, mo, d) = parts[0] > 31 ? (parts[0], parts[1], parts[2]) : (parts[2], parts[1], parts[0]);
                    try { return new DateTime(y, mo, d); } catch (ArgumentOutOfRangeException) { /* fall through */ }
                }
                return file.LastWriteTime;
            default:
                return file.LastWriteTime;
        }
    }
}
