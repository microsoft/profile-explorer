// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorer.Core.Session;

namespace ProfileExplorer.Core.Profile.Data;

/// <summary>
/// Result of loading profile data, including the data and session setup information.
/// </summary>
public class ProfileLoadResult {
  public ProfileData ProfileData { get; set; }
  public ILoadedDocument MainDocument { get; set; }
  public List<ILoadedDocument> OtherDocuments { get; set; }
  public string MainImageName { get; set; }
  
  public ProfileLoadResult(ProfileData profileData, ILoadedDocument mainDocument, 
                          List<ILoadedDocument> otherDocuments, string mainImageName) {
    ProfileData = profileData;
    MainDocument = mainDocument;
    OtherDocuments = otherDocuments;
    MainImageName = mainImageName;
  }
}
