using CalqFramework.Options;
using CalqFramework.Terminal;
using System.Text.RegularExpressions;
using System.Xml;
using static CalqFramework.Terminal.CommandLineUtil;

namespace CalqFramework.Dvo;

class Program {
    private void RetryWithStashingCMD(string cmd) {
        try {
            CMD(cmd);
        } catch (CommandExecutionException) {
            var hasUncommitedChanges = CMD("git diff") != "" ? true : false;
            if (hasUncommitedChanges) {
                CMD("git stash push --include-untracked --quiet");
                try {
                    CMD(cmd);
                } finally {
                    CMD("git stash pop --quiet");
                }
            }
        }
    }

    public void Init(string type, string projectName) {
        if (string.IsNullOrEmpty(type)) {
            CMD($"dotnet {Environment.GetCommandLineArgs()}");
            return;
        }

        if (string.IsNullOrEmpty(projectName)) {
            CMD($"dotnet {Environment.GetCommandLineArgs()}");
            return;
        }

        var projectNameWithoutRootNamespace = projectName.Substring(projectName.IndexOf('.') + 1);

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

        var user = CMD("git config user.name").Trim();
        var projectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repos", user, projectKebabName);
        Directory.CreateDirectory(projectPath);
        Directory.SetCurrentDirectory(projectPath);

        switch (type) {
            case "shell":
                var projectFolder = Path.Combine(projectPath, projectName);
                Directory.CreateDirectory(projectFolder);
                Directory.SetCurrentDirectory(projectFolder);
                File.WriteAllText(projectSnakeName, "");
                CMD($"chmod +x {projectSnakeName}");
                Directory.SetCurrentDirectory(projectPath);
                //File.WriteAllText(".gitignore", CMD("curl https://www.toptal.com/developers/gitignore/api/linux,macos,windows,visualstudiocode"));
                File.WriteAllText(".gitignore", new HttpClient().GetStringAsync("https://www.toptal.com/developers/gitignore/api/linux,macos,windows,visualstudiocode").Result);
                break;
            case "empty":
                break;
            case "Console Application":
            case "console":
            case "Class library":
            case "classlib":
                var testProjectName = projectName + "Test";
                var projectFile = Path.Combine(projectName, projectName + ".csproj");
                var testProjectFile = Path.Combine(testProjectName, testProjectName + ".csproj");
                var solutionFile = projectName + ".sln";

                CMD($"dotnet new {type} -n {projectName}");
                CMD($"dotnet new xunit -n {testProjectName}");
                CMD($"dotnet new sln -n {projectName}");
                CMD($"dotnet add {testProjectFile} reference {projectFile}");
                CMD($"dotnet sln {solutionFile} add {projectFile} {testProjectFile}");

                //var projectFileContents = File.ReadAllText(projectFile);
                //projectFileContents = Regex.Replace(projectFileContents, "(<PropertyGroup>)\r\n    <Nullable>enable</Nullable>", "$1\r\n    <Nullable>enable</Nullable>");
                //projectFileContents = Regex.Replace(projectFileContents, "(</PropertyGroup>)  <RootNamespace>{projectFullName}</RootNamespace>\r\n  $1", "$1  <RootNamespace>{projectFullName}</RootNamespace>\r\n  $1");
                //projectFileContents = Regex.Replace(projectFileContents, "(</PropertyGroup>)  <PackageId>{projectFullName}</PackageId>\r\n  $1", "$1  <PackageId>{projectFullName}</PackageId>\r\n  $1");
                //projectFileContents = Regex.Replace(projectFileContents, "(</PropertyGroup>)  <Version>0.0.0</Version>\r\n  $1", "$1  <Version>0.0.0</Version>\r\n  $1");
                //File.WriteAllText(projectFile, projectFileContents);

                var xmlDoc = new XmlDocument();
                xmlDoc.Load(projectFile);
                (xmlDoc.SelectSingleNode("/Project/PropertyGroup/Nullable") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("Nullable"))!).InnerText = "enable";
                (xmlDoc.SelectSingleNode("/Project/PropertyGroup/RootNamespace") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("RootNamespace"))!).InnerText = projectName;
                (xmlDoc.SelectSingleNode("/Project/PropertyGroup/PackageId") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("PackageId"))!).InnerText = projectName;
                (xmlDoc.SelectSingleNode("/Project/PropertyGroup/Version") ?? xmlDoc.SelectSingleNode("/Project/PropertyGroup")!.AppendChild(xmlDoc.CreateElement("Version"))!).InnerText = "0.0.0";
                xmlDoc.Save(projectFile);

                if (type == "Class library" || type == "classlib") {
                    CMD($"dotnet add {projectFile} package Microsoft.SourceLink.GitHub");
                }

                //File.WriteAllText(".gitignore", CMD("curl https://www.toptal.com/developers/gitignore/api/linux,macos,windows,dotnetcore,monodevelop,visualstudio,visualstudiocode,rider"));
                File.WriteAllText(".gitignore", new HttpClient().GetStringAsync("https://www.toptal.com/developers/gitignore/api/linux,macos,windows,dotnetcore,monodevelop,visualstudio,visualstudiocode,rider").Result);

                Directory.CreateDirectory(Path.Combine(".github", "workflows"));
                var cloneDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(cloneDir);
                CMD($"gh repo clone calq-framework/.github {cloneDir} -- --depth 1 --branch main");
                File.WriteAllText(".github/workflows/stableflow-release.yaml", File.ReadAllText(Path.Combine(cloneDir, "workflow-templates/stableflow-release.yaml")).Replace("$default-branch", "main"));
                Directory.Delete(cloneDir, true);
                break;
            default:
                CMD($"dotnet new {type} -n {projectName}");
                break;
        }

        var licenseCloneDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(licenseCloneDir);
        CMD($"gh repo clone calq-framework/license {licenseCloneDir} -- --depth 1 --branch main");
        var year = DateTime.Now.Year.ToString();
        var copyrightHolder = user;
        var licenseTemplatePath = Path.Combine(licenseCloneDir, "LICENSE.template.txt");
        var licenseContents = File.ReadAllText(licenseTemplatePath)
            .Replace("${__YEAR__}", year)
            .Replace("${__COPYRIGHT_HOLDER__}", copyrightHolder);
        File.WriteAllText("LICENSE.txt", licenseContents);
        Directory.Delete(licenseCloneDir, true);

        File.WriteAllText("README.md", "");

        CMD("git init --initial-branch=main");
        CMD("git add .");
        CMD("git commit -m \"init\"");

        CMD($"gh repo create {projectKebabName} --private --source=. --remote=origin --disable-wiki");
        CMD("git push --set-upstream origin main");

        if (type == "Console Application" || type == "console" || type == "Class library" || type == "classlib") {
            CMD($"gh secret set MAIN_NUGET_PAT --body {Environment.GetEnvironmentVariable("MAIN_NUGET_PAT")}");
            CMD($"gh secret set CALQ_FRAMEWORK_NUGET_PAT --body {Environment.GetEnvironmentVariable("CALQ_FRAMEWORK_NUGET_PAT")}");
        }
    }

    public void Pull() {
        RetryWithStashingCMD("git pull");
    }

    public void Switch(string branchName, bool create = false) {
        if (create) {
            CMD("git fetch origin main");
            CMD($"git switch -c {branchName} origin/main");
            CMD($"git push --set-upstream origin {branchName}");
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
            } catch (CommandExecutionException) {
                Switch(branchName, true);
            }
            return;
        }

        var createOutput = CMD($"gh issue create --title \"{titleOrNumber}\" --body \"\"").Trim(); // https://github.com/$organization/$repository/issues/$issueNumber
        //var issueNumber = Regex.Replace(createOutput, @".*\/", "");
        var issueNumber = createOutput.Split('/')[^1];

        if (Regex.Match(issueNumber, @"^[0-9]+$").Value != issueNumber) {
            throw new Exception("Failed to resolve issue number from 'gh issue create'.");
        }

        Switch($"issues/{issueNumber}", true);
    }

    public void Update() {
        var branchName = CMD("git branch --show-current").Trim();
        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }

        var prState = CMD("gh pr status --json state --jq .currentBranch.state").Trim();
        if (prState != "OPEN" && prState != "") {
            Console.WriteLine("ERROR: related pull request has already been closed");
            Environment.Exit(1);
        }

        CMD("git fetch origin main");
        CMD("git merge origin/main --autostash");
    }

    public void Pr() {
        var branchName = CMD("git branch --show-current").Trim();
        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }
        var prState = CMD("gh pr status --json state --jq .currentBranch.state").Trim();
        if (!string.IsNullOrEmpty(prState) && prState != "OPEN") {
            Console.WriteLine("ERROR: related pull request has already been closed");
            Environment.Exit(1);
        }

        Update();
        CMD("git push");
        if (string.IsNullOrEmpty(prState)) {
            var issueNumber = branchName.Replace("issues/", "");
            //var issueTitle = Regex.Match(CMD($"gh issue view {issueNumber}"), @"title:\s*([^\n]+)").Groups[1].Value;
            var issueInfo = CMD($"gh issue view {issueNumber}");
            var issueTitle = issueInfo.Split('\n').Where(x => x.StartsWith("title")).FirstOrDefault()?.Split(':')[1].Trim();

            if (string.IsNullOrEmpty(issueTitle)) {
                throw new Exception("Failed to resolve issue title from 'gh issue view'.");
            }

            CMD($"gh pr create --base main --title \"(#{issueNumber}) {issueTitle}\" --body \"\"");
        }
    }

    public void Merge() {
        var branchName = CMD("git branch --show-current").Trim();

        if (branchName == "main") {
            Console.WriteLine("ERROR: current branch is set to 'main'");
            Environment.Exit(1);
        }

        var issueNumber = branchName.StartsWith("issues/") ? branchName.Substring("issues/".Length) : branchName;

        Pr();
        CMD("gh pr merge --squash");
        CMD($"git push origin --delete {branchName}");
        CMD($"gh issue close {issueNumber}");
        Switch("main", false);
        CMD($"git branch --delete --force {branchName}");
        Pull();
    }

    public void Relock() {
        CMD("dotnet restore --no-cache --force-evaluate --use-lock-file");
    }

    static void Main(string[] args) {
        CommandLineInterface.Execute(new Program(), args,
            new CliSerializerOptions() {
                SkipUnknown = true,
                BindingAttr = CliSerializerOptions.DefaultLookup | System.Reflection.BindingFlags.IgnoreCase
            }
        );
    }
}
