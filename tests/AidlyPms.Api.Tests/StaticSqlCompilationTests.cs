using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AidlyPms.Api.Tests;

/// <summary>
/// CI Static SQL Compilation Test:
/// Extracts all verbatim static SQL @"..." statements from controllers, services, and infrastructure code,
/// transforms parameters (@param -> $n), and verifies they PREPARE cleanly against the PostgreSQL test schema.
/// Catches wrong column names and syntax errors across the entire codebase.
/// </summary>
[Collection("PostgresDatabaseIntegration")]
public class StaticSqlCompilationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string? _baseConnectionString;
    private PostgresTestSchemaHarness? _harness;
    private NpgsqlConnection? _connection;

    public record DiscoveredSqlStatement(string FilePath, int LineNumber, string RawSql, string ConvertedSql);

    public StaticSqlCompilationTests(ITestOutputHelper output)
    {
        _output = output;
        _baseConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
        {
            _output.WriteLine("TEST_POSTGRES_CONNECTION is not set. StaticSqlCompilationTests will run in dry-run mode.");
            return;
        }

        _harness = await PostgresTestSchemaHarness.CreateAsync(_baseConnectionString);
        _connection = new NpgsqlConnection(_harness.IsolatedConnectionString);
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }

        if (_harness != null)
        {
            await _harness.DisposeAsync();
        }
    }

    public static string FindApiSourceRoot()
    {
        var current = AppContext.BaseDirectory;
        for (int i = 0; i < 7; i++)
        {
            var candidate = Path.Combine(current, "src", "Host", "AidlyPms.Api");
            if (Directory.Exists(candidate)) return candidate;

            var candidate2 = Path.Combine(current, "aidlypms-backend", "src", "Host", "AidlyPms.Api");
            if (Directory.Exists(candidate2)) return candidate2;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        throw new DirectoryNotFoundException($"Could not locate 'AidlyPms.Api' source directory from {AppContext.BaseDirectory}.");
    }

    public static List<DiscoveredSqlStatement> ExtractStaticSqlStatements()
    {
        var result = new List<DiscoveredSqlStatement>();
        var rootDir = FindApiSourceRoot();
        var files = Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories);

        var verbatimRegex = new Regex(@"@""(?:[^""]|"""")*""", RegexOptions.Compiled);
        var sqlKeywordsRegex = new Regex(@"\b(SELECT|INSERT|UPDATE|DELETE|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var inListRegex = new Regex(@"\bIN\s*\(?\s*@\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (var file in files)
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/tests/"))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var matches = verbatimRegex.Matches(text);

            foreach (Match m in matches)
            {
                var literal = m.Value;
                // Skip if preceded by $ (interpolated verbatim string $@"" or @$"" where $ precedes)
                if (m.Index > 0 && (text[m.Index - 1] == '$' || (m.Index > 1 && text[m.Index - 2] == '$')))
                {
                    continue;
                }

                // Skip strings containing C# interpolation ({) or IN @list
                if (literal.Contains('{') || inListRegex.IsMatch(literal))
                {
                    continue;
                }

                // Check if string contains SQL statement keywords
                if (!sqlKeywordsRegex.IsMatch(literal))
                {
                    continue;
                }

                // Unescape verbatim string literal: remove leading @" and trailing ", replace "" with "
                var sql = literal.Substring(2, literal.Length - 3).Replace("\"\"", "\"").Trim();

                // Must begin with SQL statement keyword
                var trimmedStart = sql.TrimStart();
                if (!Regex.IsMatch(trimmedStart, @"^(SELECT|INSERT|UPDATE|DELETE|WITH)\b", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                // Trim trailing semicolon
                var statement = sql.TrimEnd(';', ' ', '\r', '\n', '\t');

                // Compute line number
                var lineNumber = 1;
                for (int i = 0; i < m.Index; i++)
                {
                    if (text[i] == '\n') lineNumber++;
                }

                // PREPARE validates one statement. Never execute the remaining statements of a batch.
                foreach (var individual in SplitSqlStatements(statement))
                {
                    var convertedSql = ConvertParametersToPostgreSql(individual);
                    result.Add(new DiscoveredSqlStatement(file, lineNumber, individual, convertedSql));
                }
            }
        }

        return result;
    }

    public static IEnumerable<string> SplitSqlStatements(string sql)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                if (quoted && i + 1 < sql.Length && sql[i + 1] == '\'') { i++; continue; }
                quoted = !quoted;
            }
            if (sql[i] == ';' && !quoted)
            {
                var statement = sql[start..i].Trim();
                if (statement.Length > 0) yield return statement;
                start = i + 1;
            }
        }
        var last = sql[start..].Trim();
        if (last.Length > 0) yield return last;
    }

    public static string ConvertParametersToPostgreSql(string sql)
    {
        // Split by single quotes so we never replace @ inside string literals (e.g. email addresses)
        // Tokens with even indices are outside quotes; odd indices are inside quotes.
        var parts = Regex.Split(sql, @"('(?:''|[^'])*')");

        var paramNames = new List<string>();
        for (int i = 0; i < parts.Length; i += 2)
        {
            var matches = Regex.Matches(parts[i], @"@([a-zA-Z_][a-zA-Z0-9_]*)");
            foreach (Match m in matches)
            {
                var name = m.Groups[1].Value;
                if (!paramNames.Contains(name))
                {
                    paramNames.Add(name);
                }
            }
        }

        // Replace longer parameter names first to prevent partial prefix replacements
        paramNames = paramNames.OrderByDescending(p => p.Length).ToList();

        var paramMap = new Dictionary<string, string>();
        for (int i = 0; i < paramNames.Count; i++)
        {
            paramMap[paramNames[i]] = $"${i + 1}";
        }

        for (int i = 0; i < parts.Length; i += 2)
        {
            parts[i] = Regex.Replace(parts[i], @"@([a-zA-Z_][a-zA-Z0-9_]*)", match =>
            {
                var name = match.Groups[1].Value;
                return paramMap.TryGetValue(name, out var placeholder) ? placeholder : match.Value;
            });
        }

        return string.Join("", parts);
    }

    [Fact]
    public async Task Every_Static_Sql_String_Compiles_Against_Test_Schema()
    {
        var statements = ExtractStaticSqlStatements();
        _output.WriteLine($"Extracted {statements.Count} static SQL statements from codebase.");
        Assert.True(statements.Count >= 200, $"Expected at least 200 static SQL statements, but found {statements.Count}");

        if (_connection == null || _harness == null)
        {
            _output.WriteLine("TEST_POSTGRES_CONNECTION is not set. Skipped live DB PREPARE verification (runs in CI container).");
            return;
        }

        var errors = new List<string>();
        int tested = 0;
        int skippedIndeterminate = 0;

        for (int i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i];
            var prepName = $"stmt_{i}";
            var cleanSql = stmt.ConvertedSql.Trim().TrimEnd(';');

            try
            {
                await _connection.ExecuteAsync($"PREPARE {prepName} AS {cleanSql};");
                await _connection.ExecuteAsync($"DEALLOCATE {prepName};");
                tested++;
            }
            catch (PostgresException ex) when (ex.SqlState == "42P18" || ex.Message.Contains("could not determine data type", StringComparison.OrdinalIgnoreCase))
            {
                // Allowed per specification: fail on any error EXCEPT "could not determine data type"
                skippedIndeterminate++;
                try { await _connection.ExecuteAsync($"DEALLOCATE {prepName};"); } catch { }
            }
            catch (Exception ex)
            {
                var relPath = Path.GetRelativePath(AppContext.BaseDirectory, stmt.FilePath);
                var errDesc = $"[{relPath}:{stmt.LineNumber}] SQL compile error: {ex.Message}\nQuery:\n{cleanSql}\n";
                errors.Add(errDesc);
                Console.Error.WriteLine($"::error file={relPath},line={stmt.LineNumber}::{ex.Message}");
                try { await _connection.ExecuteAsync($"DEALLOCATE {prepName};"); } catch { }
            }
        }

        _output.WriteLine($"Static SQL verification complete: {tested} verified cleanly, {skippedIndeterminate} skipped due to 42P18 indeterminate type, {errors.Count} errors.");

        if (errors.Count > 0)
        {
            Assert.True(errors.Count == 0, $"Static SQL compilation failed for {errors.Count} statement(s):\n\n" + string.Join("\n---\n", errors));
        }
    }
}
