using CalqFramework.Cli;
using CalqFramework.Cli.Serialization;
using CalqFramework.Cmd;
using CalqFramework.Cmd.Shells;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Dvo;

/// <summary>Provides a set of commands for streamlining development workflows.</summary>
class Program {

    /// <summary>
    /// Executes a shell command, stashing any uncommitted changes if the command fails,
    /// then retries the command and restores the stashed changes.
    /// </summary>
    /// <param name="cmd">The shell command to execute.</param>
    /// <exception cref="ShellScriptException">Thrown if the command fails even after stashing.</exception>
    private void RetryWithStashingCMD(string cmd) {
        try {
            RUN(cmd);
        } catch (ShellScriptException) {
            var hasUncommitedChanges = CMD("git diff") != "" ? true : false;
            if (hasUncommitedChanges) {
                RUN("git stash push --include-untracked --quiet");
                try {
                    RUN(cmd);
                } finally {
                    RUN("git stash pop --quiet");
                }
            }
        }
    }

    /// <summary>Initializes a new .NET project scaffold based on the specified template type.</summary>
    /// <param name="type">
    /// The project template identifier to use (e.g. console, classlib, empty).
    /// </param>
    /// <param name="projectFullName">
    /// The full CLR namespace and project name (e.g. MyCompany.MyProject).
    /// </param>
    /// <param name="organization">
    /// Optional GitHub organization used for repository creation.
    /// </param>
    /// <exception cref="ShellScriptException">
    /// Thrown when any invoked shell command (via <c>RUN</c> or <c>CMD</c>) fails and cannot be recovered by stashing or retry logic.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <c>projectFullName</c> does not contain a dot separator to derive the root namespace.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The method performs the following high-level steps:
    /// </para>
    /// <list type="number">
    ///   <item><description>Validate inputs; delegate to <c>dotnet</c> CLI on missing parameters.</description></item>
    ///   <item><description>Derive a kebab-case and snake-case short name from the portion of <c>projectFullName</c> after the first dot.</description></item>
    ///   <item><description>Create and switch into a directory named by the kebab-case short name.</description></item>
    ///   <item><description>Depending on <c>type</c>, invoke <c>dotnet new</c> to scaffold console apps, class libraries, unit tests, and solutions.</description></item>
    ///   <item><description>For class libraries, apply GitHub source link package and update the .csproj&apos;s RootNamespace, PackageId, and Version elements.</description></item>
    ///   <item><description>If <c>organization</c> is specified, fetch and install .gitignore, CI templates, LICENSE, README, and push an initial commit to GitHub.</description></item>
    /// </list>
    /// </remarks>

    public void Init(string type, [CliName("projectFullName")][CliName("n")] string projectFullName, string? organization = null) {
        if (string.IsNullOrEmpty(type)) {
            RUN($"dotnet {Environment.GetCommandLineArgs().Skip(1)}");
            return;
        }

        if (string.IsNullOrEmpty(projectFullName)) {
            RUN($"dotnet {Environment.GetCommandLineArgs().Skip(1)}");
            return;
        }

        var projectNameWithoutRootNamespace = projectFullName.Substring(projectFullName.IndexOf('.') + 1);

        string projectKebabName = projectNameWithoutRootNamespace;
        projectKebabName = Regex.Replace(projectKebabName, "([a-z0-9])([A-Z])", "$1-$2");
        projectKebabName = Regex.Replace(projectKebabName, "([a-zA-Z0-9])([A-Z][a-z])", "$1-$2");
        projectKebabName = Regex.Replace(projectKebabName, "[. ]", "-");
        projectKebabName = projectKebabName.ToLower();

        string projectSnakeName = projectNameWithoutRootNamespace;
        projectSnakeName = Regex.Replace(projectSnakeName, "([a-z0-9])([A-Z])", "$1_$2");
        projectSnakeName = Regex.Replace(projectSnakeName, "([a-zA-Z0-9])([A-Z][a-z])", "$1_$2");
        projectKebabName = Regex.Replace(projectKebabName, "[. ]", "_");
        projectSnakeName = projectSnakeName.ToLower();

        var user = CMD("git config user.name");
        var projectDir = Path.Combine(PWD, projectKebabName);
        Directory.CreateDirectory(projectDir);
        CD(projectDir);

        switch (type) {
            case "empty":
                break;
            case "Console Application":
            case "console":
            case "Class library":
            case "classlib":
                var testProjectName = projectFullName + "Test";
                var projectFile = Path.Combine(projectFullName, projectFullName + ".csproj");
                var testProjectFile = Path.Combine(testProjectName, testProjectName + ".csproj");
                var solutionFile = projectFullName + ".sln";

                RUN($"dotnet new {type} -n {projectFullName}");
                RUN($"dotnet new xunit -n {testProjectName}");
                RUN($"dotnet new sln -n {projectFullName}");
                RUN($"dotnet add {testProjectFile} reference {projectFile}");
                RUN($"dotnet sln {solutionFile} add {projectFile} {testProjectFile}");

                var xmlDoc = new XmlDocument();
                xmlDoc.Load(projectFile);
                (xmlDoc.SelectSingleNode("/Project/PropertyGroup/RootNamespace") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("RootNamespace"))!).InnerText = projectFullName;
                (xmlDoc.SelectSingleNode("/Project/PropertyGroup/PackageId") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("PackageId"))!).InnerText = projectFullName;
                (xmlDoc.SelectSingleNode("/Project/PropertyGroup/Version") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("Version"))!).InnerText = "0.0.0";
                xmlDoc.Save(projectFile);

                if (type == "Class library" || type == "classlib") {
                    RUN($"dotnet add {projectFile} package Microsoft.SourceLink.GitHub");
                }

                if (!string.IsNullOrEmpty(organization)) {
                    var gitignoreText = new HttpClient().GetStringAsync("https://www.toptal.com/developers/gitignore/api/linux,macos,windows,dotnetcore,monodevelop,visualstudio,visualstudiocode,rider").Result;
                    File.WriteAllText(Path.Combine(PWD, ".gitignore"), gitignoreText);

                    string workflowsDir = Path.Combine(PWD, ".github", "workflows");
                    Directory.CreateDirectory(workflowsDir);
                    var cloneDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(cloneDir);
                    RUN($"gh repo clone calq-framework/.github {cloneDir} -- --depth 1 --branch main");
                    string sourceDir = Path.Combine(cloneDir, "workflow-templates");
                    foreach (var sourcePath in Directory.GetFiles(sourceDir)) {
                        string fileName = Path.GetFileName(sourcePath);
                        string destPath = Path.Combine(workflowsDir, fileName);
                        string contents = File.ReadAllText(sourcePath).Replace("$default-branch", "main");
                        File.WriteAllText(destPath, contents);
                    }
                    Directory.Delete(cloneDir, true);
                }
                break;
            default:
                RUN($"dotnet new {type} -n {projectFullName}");
                break;
        }

        if (!string.IsNullOrEmpty(organization)) {
            if (RepositoryExists($"{organization}/license")) {
                var licenseCloneDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(licenseCloneDir);
                RUN($"gh repo clone {organization}/license {licenseCloneDir} -- --depth 1 --branch main");
                var year = DateTime.Now.Year.ToString();
                var licenseTemplatePath = Path.Combine(licenseCloneDir, "LICENSE.template.txt");
                var licenseContents = File.ReadAllText(licenseTemplatePath).Replace("${__YEAR__}", year);
                File.WriteAllText(Path.Combine(PWD, "LICENSE.txt"), licenseContents);
                Directory.Delete(licenseCloneDir, true);
            }

            File.WriteAllText(Path.Combine(PWD, "README.md"), "");

            RUN("git init --initial-branch=main");
            RUN("git add .");
            RUN("git commit -m \"init\"");

            RUN($"gh repo create {organization}/{projectKebabName} --private --source=. --remote=origin --disable-wiki");
            RUN("git push --set-upstream origin main");
        }

        bool RepositoryExists(string repo) {
            try {
                _ = CMD($"gh repo view {organization}/license");
                return true;
            } catch (ShellScriptException) {
                return false;
            }
        }
    }

    /// <summary>Perform git pull with automatic retry via stashing on conflict.</summary>
    public void Pull() {
        RetryWithStashingCMD("git pull");
    }

    /// <summary>Switch to a git branch, optionally creating it from origin/main.</summary>
    /// <param name="branchName">Target branch name.</param>
    /// <param name="create">Whether to create the branch first.</param>
    public void Switch(string branchName, bool create = false) {
        if (create) {
            RUN("git fetch origin main");
            RUN($"git switch -c {branchName} origin/main");
            RUN($"git push --set-upstream origin {branchName}");
        } else {
            RetryWithStashingCMD($"git switch {branchName}");
            Pull();
        }
    }

    /// <summary>Create or switch to an issue branch based on title or number.</summary>
    /// <param name="titleOrNumber">Issue title or numeric ID.</param>
    public void Issue(string titleOrNumber) {
        if (Regex.Match(titleOrNumber, "^[0-9]+$").Value == titleOrNumber) {
            string branchName = $"issues/{titleOrNumber}";
            try {
                Switch(branchName, false);
            } catch (ShellScriptException) {
                Switch(branchName, true);
            }
            return;
        }

        string createOutput = CMD($"gh issue create --title \"{titleOrNumber}\" --body \"\"");
        string issueNumber = createOutput.Split('/')[^1];

        if (Regex.Match(issueNumber, "^[0-9]+$").Value != issueNumber) {
            throw new Exception("Failed to resolve issue number from 'gh issue create'.");
        }

        Switch($"issues/{issueNumber}", true);
    }

    /// <summary>Merge origin/main into current branch with autostashing.</summary>
    public void Sync() {
        string branchName = CMD("git branch --show-current");
        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }

        string prState = CMD("gh pr status --json state --jq .currentBranch.state");
        if (prState != "OPEN" && prState != string.Empty) {
            Console.WriteLine("ERROR: related pull request has already been closed");
            Environment.Exit(1);
        }

        RUN("git fetch origin main");
        RUN("git merge origin/main --autostash");
    }

    /// <summary>Push branch and create or update a pull request on GitHub.</summary>
    public void Pr() {
        string branchName = CMD("git branch --show-current");
        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }

        string prState = CMD("gh pr status --json state --jq .currentBranch.state");
        if (!string.IsNullOrEmpty(prState) && prState != "OPEN") {
            Console.WriteLine("ERROR: related pull request has already been closed");
            Environment.Exit(1);
        }

        Sync();
        RUN("git push");

        if (string.IsNullOrEmpty(prState)) {
            string issueNumber = branchName.Replace("issues/", string.Empty);
            string issueInfo = CMD($"gh issue view {issueNumber} --json title");
            var issueJson = JsonSerializer.Deserialize<JsonElement>(issueInfo);
            string issueTitle = issueJson.GetProperty("title").GetString()!;

            RUN($"gh pr create --base main --title \"(#{issueNumber}) {issueTitle}\" --body \"\"");
        }
    }

    /// <summary>Merge pull request, delete branch and close associated issue.</summary>
    public void Merge() {
        string branchName = CMD("git branch --show-current");

        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }

        string issueNumber = branchName.StartsWith("issues/")
            ? branchName.Substring("issues/".Length)
            : branchName;

        Pr();
        RUN("gh pr merge --squash");
        RUN($"git push origin --delete {branchName}");
        RUN($"gh issue close {issueNumber}");
        Switch("main", false);
        RUN($"git branch --delete --force {branchName}");
        Pull();
    }

    /// <summary>Restore project packages using lock file enforcement.</summary>
    public void Relock() {
        RUN("dotnet restore --no-cache --force-evaluate --use-lock-file");
    }

    /// <summary>List up to 100 open GitHub issues without assignees.</summary>
    public void Issues() {
        RUN("gh issue list --limit 100 --search \"no:assignee\"");
    }

    static void Main(string[] args) {
        LocalTerminal.Shell = new CommandLine();
        var result = new CommandLineInterface().Execute(new Program());
        if (result is not ResultVoid) {
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
