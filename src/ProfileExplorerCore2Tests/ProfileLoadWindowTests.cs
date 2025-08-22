// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProfileExplorerCore2;
using ProfileExplorerCore2.Binary;
using ProfileExplorerCore2.Compilers.Architecture;
using ProfileExplorerCore2.Compilers.ASM;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.IR.Tags;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Profile.ETW;
using ProfileExplorerCore2.Profile.Processing;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Session;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;
using Xunit;

namespace ProfileExplorerCore2Tests;

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

