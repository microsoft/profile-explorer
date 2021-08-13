using System.Threading.Tasks;
using IRExplorerCore;

namespace IRExplorerUI.Profile {
    public enum ProfileLoadStage {
        TraceLoading,
        SymbolLoading,
        TraceProcessing
    }

    public class ProfileLoadProgress {
        public ProfileLoadProgress(ProfileLoadStage stage) {
            Stage = stage;
        }

        public ProfileLoadStage Stage { get; set; }
        public int Total { get; set; }
        public int Current { get; set; }
    }

    public delegate void ProfileLoadProgressHandler(ProfileLoadProgress info);

    public interface IProfileDataProvider {
        ProfileData LoadTrace(string tracePath, string imageName, string symbolPath,
                              bool markInlinedFunctions, ProfileLoadProgressHandler progressCallback,
                              CancelableTask cancelableTask = null);

        Task<ProfileData> LoadTraceAsync(string tracePath, string imageName, string symbolPath,
            bool markInlinedFunctions, ProfileLoadProgressHandler progressCallback,
            CancelableTask cancelableTask = null);
    }
}
