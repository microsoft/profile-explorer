// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProfileExplorerCore;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Compilers.Architecture;
using ProfileExplorerCore.Compilers.ASM;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.IR.Tags;
using ProfileExplorerCore.Profile.Data;
using ProfileExplorerCore.Profile.ETW;
using ProfileExplorerCore.Profile.Processing;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Session;
using ProfileExplorerCore.Settings;
using ProfileExplorerCore.Utilities;
using Xunit;

namespace ProfileExplorerCoreTests;

public class ProfileLoadWindowTests
{
    [Fact]
    public async Task LoadTraceAndProcess_NoErrorsOrExceptions()
    {
        // Arrange
        var session = new BaseSession();
        var window = new ProfileLoadWindow(session);
        string etlPath = @"C:\Users\benjaming.REDMOND\OneDrive - Microsoft\Documents\My Documents\Tracing\trace.etl";
        int processId = 34376;

        // Remove symbol server search paths for faster test
        window.SymbolSettings.SymbolPaths.Clear();
        window.SymbolSettings.UseEnvironmentVarSymbolPaths = false;
        // Optionally, also clear any default symbol servers if present
        // window.SymbolSettings.ClearRejectedFiles();

        // Act & Assert
        var processList = await window.LoadProcessList(etlPath);
        Assert.NotNull(processList);
        window.SelectProcess(processId);
        var report = await window.LoadProfileTrace();
        Assert.NotNull(report);

        foreach (var pair in session.ProfileData.ModuleWeights) {
          double weightPercentage = session.ProfileData.ScaleModuleWeight(pair.Value);
          Console.WriteLine($"{session.ProfileData.Modules[pair.Key].ModuleName}: {weightPercentage}%, {pair.Value}");
        }
  }
}

