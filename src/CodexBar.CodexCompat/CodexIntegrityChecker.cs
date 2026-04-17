using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed class CodexIntegrityChecker
{
    public ValidationReport Validate(CodexHomeState home)
    {
        var report = new ValidationReport();

        if (!Directory.Exists(home.RootPath))
        {
            report.Errors.Add($"CODEX_HOME does not exist: {home.RootPath}");
            return report;
        }

        if (!File.Exists(home.ConfigPath))
        {
            report.Errors.Add($"Missing config.toml: {home.ConfigPath}");
        }

        if (!File.Exists(home.AuthPath))
        {
            report.Errors.Add($"Missing auth.json: {home.AuthPath}");
        }

        if (!Directory.Exists(home.SessionsPath))
        {
            report.Warnings.Add($"sessions directory is absent: {home.SessionsPath}");
        }

        if (!Directory.Exists(home.ArchivedSessionsPath))
        {
            report.Warnings.Add($"archived_sessions directory is absent: {home.ArchivedSessionsPath}");
        }

        return report;
    }
}

