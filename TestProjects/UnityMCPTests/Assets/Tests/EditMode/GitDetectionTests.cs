using System.Linq;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Dependencies.PlatformDetectors;
using NUnit.Framework;

namespace MCPForUnityTests.Editor
{
    public class GitDetectionTests
    {
        [TestCase("git version 2.45.1.windows.1", "2.45.1.windows.1")]
        [TestCase("git version 2.39.5 (Apple Git-154)", "2.39.5")]
        [TestCase("  git version 2.43.0\n", "2.43.0")]
        public void TryParseGitVersion_ExtractsTheVersionToken(string output, string expected)
        {
            Assert.IsTrue(PlatformDetectorBase.TryParseGitVersion(output, out string version));
            Assert.AreEqual(expected, version);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("'git' is not recognized as an internal or external command")]
        [TestCase("git version ")]
        [TestCase("git version beta")]
        public void TryParseGitVersion_RejectsAnythingThatIsNotAGitVersionLine(string output)
        {
            Assert.IsFalse(PlatformDetectorBase.TryParseGitVersion(output, out _));
        }

        [Test]
        public void CheckAllDependencies_ReportsGitAsOptional()
        {
            var result = DependencyManager.CheckAllDependencies();
            var git = result.Dependencies.FirstOrDefault(d => d.Name == "Git");

            Assert.IsNotNull(git, "The dependency check should always include a Git row");
            Assert.IsFalse(git.IsRequired, "Git must never block setup; only Git-URL installs need it");
            Assert.AreEqual(PlatformDetectorBase.GitInstallUrl, git.InstallationHint);
            if (git.IsAvailable)
            {
                Assert.IsFalse(string.IsNullOrEmpty(git.Version));
                Assert.IsFalse(string.IsNullOrEmpty(git.Path));
            }
            else
            {
                Assert.IsFalse(string.IsNullOrEmpty(git.ErrorMessage));
            }
        }

        [Test]
        public void MissingGit_DoesNotMakeTheSystemNotReady()
        {
            // Mirrors what the setup window computes: Python and uv present, git absent.
            var result = new DependencyCheckResult();
            result.Dependencies.Add(new DependencyStatus("Python") { IsAvailable = true, Version = "3.12.0" });
            result.Dependencies.Add(new DependencyStatus("uv Package Manager") { IsAvailable = true, Version = "0.8.0" });
            result.Dependencies.Add(new DependencyStatus("Git", isRequired: false) { IsAvailable = false, ErrorMessage = "git not found" });

            result.GenerateSummary();

            Assert.IsTrue(result.IsSystemReady);
            Assert.IsTrue(result.HasMissingOptional);
            Assert.IsEmpty(result.GetMissingRequired());
        }
    }
}
