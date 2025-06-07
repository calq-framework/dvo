using CalqFramework.Cmd;
using CalqFramework.Cmd.TerminalComponents;

namespace CalqFramework.Dvo {
    internal class DvoTerminalLogger : ITerminalLogger {
        public void LogRun(ShellScript shellScript) {
            if (!shellScript.Script.Contains('\n')) {
                Console.Out.WriteLine($"\nRUN: {shellScript.Script}");
            } else {
                Console.Out.WriteLine($"\nRUN:\n{shellScript.Script}");
            }
        }
    }
}
