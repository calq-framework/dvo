using CalqFramework.Cli;
using CalqFramework.Cli.Serialization;
using CalqFramework.Cmd;
using CalqFramework.Cmd.Shells;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Dvo;

class Program {
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

    public void Init(string type, [CliName("n")][CliName("projectFullName")] string projectFullName, string? organization = null) {
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

    public void Pull() {
        RetryWithStashingCMD("git pull");
    }

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

    public void Issue(string titleOrNumber) {
        if (Regex.Match(titleOrNumber, @"^[0-9]+$").Value == titleOrNumber) {
            var branchName = $"issues/{titleOrNumber}";
            try {
                Switch(branchName, false);
            } catch (ShellScriptException) {
                Switch(branchName, true);
            }
            return;
        }

        var createOutput = CMD($"gh issue create --title \"{titleOrNumber}\" --body \"\""); // https://github.com/$organization/$repository/issues/$issueNumber
        //var issueNumber = Regex.Replace(createOutput, @".*\/", "");
        var issueNumber = createOutput.Split('/')[^1];

        if (Regex.Match(issueNumber, @"^[0-9]+$").Value != issueNumber) {
            throw new Exception("Failed to resolve issue number from 'gh issue create'.");
        }

        Switch($"issues/{issueNumber}", true);
    }

    public void Sync() {
        var branchName = CMD("git branch --show-current");
        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }

        var prState = CMD("gh pr status --json state --jq .currentBranch.state");
        if (prState != "OPEN" && prState != "") {
            Console.WriteLine("ERROR: related pull request has already been closed");
            Environment.Exit(1);
        }

        RUN("git fetch origin main");
        RUN("git merge origin/main --autostash");
    }

    public void Pr() {
        var branchName = CMD("git branch --show-current");
        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }
        var prState = CMD("gh pr status --json state --jq .currentBranch.state");
        if (!string.IsNullOrEmpty(prState) && prState != "OPEN") {
            Console.WriteLine("ERROR: related pull request has already been closed");
            Environment.Exit(1);
        }

        Sync();
        RUN("git push");
        if (string.IsNullOrEmpty(prState)) {
            var issueNumber = branchName.Replace("issues/", "");
            //var issueTitle = Regex.Match(CMD($"gh issue view {issueNumber}"), @"title:\s*([^\n]+)").Groups[1].Value;
            var issueInfo = CMD($"gh issue view {issueNumber}");
            var issueTitle = issueInfo.Split('\n').Where(x => x.StartsWith("title")).FirstOrDefault()?.Split(':')[1].Trim();

            if (string.IsNullOrEmpty(issueTitle)) {
                throw new Exception("Failed to resolve issue title from 'gh issue view'.");
            }

            RUN($"gh pr create --base main --title \"(#{issueNumber}) {issueTitle}\" --body \"\"");
        }
    }

    public void Merge() {
        var branchName = CMD("git branch --show-current");

        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }

        var issueNumber = branchName.StartsWith("issues/") ? branchName.Substring("issues/".Length) : branchName;

        Pr();
        RUN("gh pr merge --squash");
        RUN($"git push origin --delete {branchName}");
        RUN($"gh issue close {issueNumber}");
        Switch("main", false);
        RUN($"git branch --delete --force {branchName}");
        Pull();
    }

    public void Relock() {
        RUN("dotnet restore --no-cache --force-evaluate --use-lock-file");
    }

    public void Issues() {
        RUN("gh issue list --limit 100 --search 'no: assignee'");
    }

    static void Main(string[] args) {
        LocalTerminal.Shell = new CommandLine();
        var result = new CommandLineInterface().Execute(new Program());
        if (result is not ResultVoid) {
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
