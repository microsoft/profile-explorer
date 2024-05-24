// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
namespace IRExplorerUI.Profile;

public sealed class CallTreeProcessor : ProfileSampleProcessor {
  public ProfileCallTree CallTree { get; } = new ProfileCallTree();

  //? TODO: Multi-threading disabled until merging of trees is impl.
  protected override int DefaultThreadCount => 1;

  public static ProfileCallTree Compute(ProfileData profile, ProfileSampleFilter filter,
                                        int maxChunks = int.MaxValue) {
    var funcProcessor = new CallTreeProcessor();
    funcProcessor.ProcessSampleChunk(profile, filter, 1);
    return funcProcessor.CallTree;
  }

  protected override void ProcessSample(ProfileSample sample, ResolvedProfileStack stack,
                                        int sampleIndex, object chunkData) {
    CallTree.UpdateCallTree(sample, stack);
  }
}
