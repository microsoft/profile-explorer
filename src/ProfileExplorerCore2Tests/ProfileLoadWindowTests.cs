// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Profile.Processing;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using Xunit;

namespace ProfileExplorer.CoreTests;

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

