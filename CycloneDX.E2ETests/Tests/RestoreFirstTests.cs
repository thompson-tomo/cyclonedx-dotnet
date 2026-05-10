// This file is part of CycloneDX Tool for .NET
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// SPDX-License-Identifier: Apache-2.0
// Copyright (c) OWASP Foundation. All Rights Reserved.

using System.Threading.Tasks;
using CycloneDX.E2ETests.Builders;
using CycloneDX.E2ETests.Infrastructure;
using Xunit;

namespace CycloneDX.E2ETests.Tests
{
    /// <summary>
    /// Proves the recommended "restore-first" workflow described in docs/best-practices.md:
    /// restore with <c>-p:Configuration=Release</c> first, then run CycloneDX with
    /// <c>--disable-package-restore</c>.  Because the restore evaluates MSBuild
    /// <c>Condition</c> attributes with the correct configuration, packages that are
    /// only referenced under <c>Debug</c> must not appear in the resulting SBOM.
    /// Also demonstrates that compound conditions involving custom MSBuild properties
    /// (e.g. <c>$(UITestsEnabled)</c>) are handled correctly when those properties are
    /// unset during restore.
    /// </summary>
    [Collection("E2E")]
    public sealed class RestoreFirstTests
    {
        private readonly E2EFixture _fixture;

        // TestPkg.A is always present; TestPkg.C is only included for Debug.
        private const string SimpleConditionalXml = """
            <ItemGroup>
              <PackageReference Include="TestPkg.A" Version="1.0.0" />
              <PackageReference Include="TestPkg.C" Version="1.0.0" Condition="'$(Configuration)' == 'Debug'" />
            </ItemGroup>
            """;

        // TestPkg.A is always present; TestPkg.C is included when Debug OR UITestsEnabled=true.
        // This is the "more complicated" case from issue #1028.
        private const string CompoundConditionalXml = """
            <ItemGroup>
              <PackageReference Include="TestPkg.A" Version="1.0.0" />
              <PackageReference Include="TestPkg.C" Version="1.0.0" Condition="'$(Configuration)' == 'Debug' Or '$(UITestsEnabled)' == 'true'" />
            </ItemGroup>
            """;

        public RestoreFirstTests(E2EFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RestoreWithRelease_ThenRunWithDisableRestore_ExcludesDebugOnlyPackage()
        {
            // Restore with Release so that MSBuild evaluates the Condition and excludes
            // TestPkg.C from project.assets.json.  Running the tool with
            // --disable-package-restore means it reads that already-correct assets file
            // and must NOT include TestPkg.C in the SBOM.
            using var solution = await new SolutionBuilder("RestoreFirstReleaseSln")
                .AddProject("MyApp", p => p
                    .WithTargetFramework("net8.0")
                    .WithRawXml(SimpleConditionalXml))
                .BuildAsync(_fixture.NuGetFeedUrl, restoreConfiguration: "Release");

            using var outputDir = solution.CreateOutputDir();

            var result = await _fixture.Runner.RunAsync(
                solution.SolutionFile,
                outputDir.Path,
                new ToolRunOptions
                {
                    NuGetFeedUrl = _fixture.NuGetFeedUrl,
                    DisablePackageRestore = true,
                });

            Assert.True(result.Success, $"Tool failed:\n{result.StdErr}");
            Assert.Contains("TestPkg.A", result.BomContent);
            Assert.DoesNotContain("TestPkg.C", result.BomContent);
        }

        [Fact]
        public async Task CompoundCondition_RestoreWithRelease_ExcludesConditionalPackage()
        {
            // Reproduces the "more complicated" case from issue #1028:
            // the condition is Configuration==Debug OR UITestsEnabled==true.
            // Restoring with -p:Configuration=Release is sufficient because
            // UITestsEnabled is unset (evaluates as empty string, not 'true'),
            // so the compound condition correctly evaluates to false and TestPkg.C
            // is excluded from project.assets.json and therefore from the SBOM.
            using var solution = await new SolutionBuilder("CompoundCondReleaseSln")
                .AddProject("MyApp", p => p
                    .WithTargetFramework("net8.0")
                    .WithRawXml(CompoundConditionalXml))
                .BuildAsync(_fixture.NuGetFeedUrl, restoreConfiguration: "Release");

            using var outputDir = solution.CreateOutputDir();

            var result = await _fixture.Runner.RunAsync(
                solution.SolutionFile,
                outputDir.Path,
                new ToolRunOptions
                {
                    NuGetFeedUrl = _fixture.NuGetFeedUrl,
                    DisablePackageRestore = true,
                });

            Assert.True(result.Success, $"Tool failed:\n{result.StdErr}");
            Assert.Contains("TestPkg.A", result.BomContent);
            Assert.DoesNotContain("TestPkg.C", result.BomContent);
        }
    }
}
