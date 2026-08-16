using GitCommands;
using GitCommands.Logging;

namespace GitCommandsTests;
public class CommandLogTests
{
    // Sets AppSettings.GitCommandValue to an arbitrary fileName so CommandLogEntry.ColumnLine
    // treats *that* file as "the git command" and strips its config args. GitCommandValue is one
    // of the settings commit 89e02fc6e deliberately keeps registry-only (reads return the
    // caller's default, writes are dropped off Windows), and CommandLogEntry.ColumnLine has no
    // way to be told the git command name other than through AppSettings.GitCommand, so there's
    // no route to this behaviour off Windows without a product-code change (out of scope this
    // round). Every case here relies on the override taking effect, so the whole method is
    // Windows-only; the sibling _should_display_wsl_git_command and _should_display_non_git_command
    // methods below don't depend on the override actually landing (they either target the
    // unconfigurable "git"/"wsl" defaults directly, or expect the non-git fallback either way) and
    // already run everywhere.
    [Platform(Include = "Win")]
    [TestCase("somegit.exe", "", "")]
    [TestCase("somegit.exe", @"verb -c config", "verb")]
    [TestCase("somegit.exe", @"-c config verb", "verb")]
    [TestCase("somegit.exe", @"-c config --no-optional-locks verb", "verb")]
    [TestCase("somegit.exe", @"-c config1 -c config2=x verb", "verb")]
    [TestCase("somegit.exe", @"-c config1 -c config2 x verb", "x verb")]
    [TestCase("something.exe", @"-c config=""value with space"" verb", "verb")]
    [TestCase("gitOnTheRocks", "config list --local --includes --null", "config list --local --includes --null")]
    [TestCase("bitbucket", "--pam --param --parapam", "--pam --param --parapam")]
    [TestCase(@"C:\bitbucket\bucketbit.kexe", "--pam --param --parapam", "--pam --param --parapam")]
    // Not needed and not implemented yet. It just removes '-c core.xxx=""something with \""' at the moment.
    [TestCase("somegit.exe", @"-c core.xxx=""something with \""value1 in escaped quotes\"" \""value2\"""" verb", @"value1 in escaped quotes\"" \""value2\"""" verb")]
    [TestCase(@"C:\yesterday\someday.plexe", @"-c core.xxx=""something with \""value1 in escaped quotes\"" \""value2\"""" verb", @"value1 in escaped quotes\"" \""value2\"""" verb")]
    public void CommanLogEntry_ColumnLine_should_display_native_git_command(string fileName, string arguments, string expectedMainArguments)
    {
        string origGitCommandValue = AppSettings.GitCommand;
        AppSettings.GitCommandValue = fileName;

        CommandLogEntry commandLogEntry = new(fileName, arguments, workingDir: "", startedAt: DateTime.MinValue, isOnMainThread: true);
        commandLogEntry.ColumnLine.Should().Be($"00:00:00.000   running         UI     {CommandLogEntry.NativeGitLogName} {expectedMainArguments}");

        AppSettings.GitCommandValue = origGitCommandValue;
    }

    [TestCase("wsl", @"-d Ubuntu --cd ""\\wsl$\Ubuntu\home\user\repo\project"" git -c config=""value with space"" arg1 arg2", "arg1 arg2")]
    [TestCase(@"wsl -d Ubuntu --cd ""\\wsl$\Ubuntu\home\user\repo\project"" git", @"-c config=""value with space"" arg1 arg2", "arg1 arg2")]
    [TestCase("wsl", @"-d Ubuntu --cd ""\\wsl$\Ubuntu\home\user\repo\project"" git config list --local --includes --null", "config list --local --includes --null")]
    [TestCase(@"wsl -d Ubuntu --cd ""\\wsl$\\Ubuntu\\home\\user\\repo\\project git", "config list --local --includes --null", "config list --local --includes --null")]
    public void CommanLogEntry_ColumnLine_should_display_wsl_git_command(string fileName, string arguments, string expectedMainArguments)
    {
        string origGitCommandValue = AppSettings.GitCommand;
        AppSettings.GitCommandValue = "git";

        CommandLogEntry commandLogEntry = new(fileName, arguments, workingDir: "", startedAt: DateTime.MinValue, isOnMainThread: true);
        commandLogEntry.ColumnLine.Should().Be($"00:00:00.000   running         UI     {CommandLogEntry.WslGitLogName} {expectedMainArguments}");

        AppSettings.GitCommandValue = origGitCommandValue;
    }

    // Depends on the AppSettings.GitCommandValue override too: "notAgit" and the WSL-shaped
    // "anycmd ... git" fileName are deliberately crafted to end with the substring "git" without
    // BEING the (overridden) git command "notanycmd", to prove FileName.EndsWith(gitCmd) doesn't
    // false-positive on a near-miss. Off Windows gitCmd is stuck at the real default "git" (see
    // the method above), so those two cases now genuinely DO end with gitCmd and misclassify -
    // same root cause, same "no route without a product change" conclusion.
    [Platform(Include = "Win")]
    [TestCase("anycmd", @"-d Ubuntu --cd ""\\wsl$\Ubuntu\home\user\repo\project"" git -c config=""value with space"" agr1 arg2", @"-d Ubuntu --cd ""\\wsl$\Ubuntu\home\user\repo\project"" git -c config=""value with space"" agr1 arg2")]
    [TestCase(@"anycmd -d Ubuntu --cd ""\\wsl$\Ubuntu\home\user\repo\project"" git", @"-c config=""value with space"" arg1 arg2", @"-c config=""value with space"" arg1 arg2")]
    [TestCase("notAgit.exe", "", "")]
    [TestCase("notAgit.exe", "69 < 420", "69 < 420")]
    [TestCase("notAgit", "69 < 420", "69 < 420")]
    [TestCase("git", "69 < 420", "69 < 420")]
    public void CommanLogEntry_ColumnLine_should_display_non_git_command(string fileName, string arguments, string expectedMainArguments)
    {
        string origGitCommandValue = AppSettings.GitCommand;
        AppSettings.GitCommandValue = "notanycmd";

        CommandLogEntry commandLogEntry = new(fileName, arguments, workingDir: "", startedAt: DateTime.MinValue, isOnMainThread: true);
        commandLogEntry.ColumnLine.Should().Be($"00:00:00.000   running         UI     {fileName} {expectedMainArguments}");

        AppSettings.GitCommandValue = origGitCommandValue;
    }
}
