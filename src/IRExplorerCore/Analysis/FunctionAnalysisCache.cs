// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis {
    //? TODO: Caching needs some settings like if it should be used, max cached items, etc

    public class FunctionAnalysisCache {
        private static Dictionary<FunctionIR, FunctionAnalysisCache> functionCacheMap_;
        private static bool cacheEnabled_;
        private static object lockObject_ = new object();

        private FunctionIR function_;
        private volatile DominatorAlgorithm dominators_;
        private volatile DominatorAlgorithm postDominators_;
        private volatile CFGReachability reachability_;
        private volatile DominanceFrontier dominanceFrontier_;
        private volatile DominanceFrontier postDominanceFrontier_;

        static FunctionAnalysisCache() {
            functionCacheMap_ = new Dictionary<FunctionIR, FunctionAnalysisCache>();
            cacheEnabled_ = true;
        }

        public static void TestReset() {
            functionCacheMap_ = new Dictionary<FunctionIR, FunctionAnalysisCache>();
            cacheEnabled_ = true;
        }

        public static void DisableCache() {
            cacheEnabled_ = false;
        }

        private FunctionAnalysisCache(FunctionIR function) {
            function_ = function;
        }

        public async Task<DominatorAlgorithm> GetDominatorsAsync() {
            if (dominators_ == null) {
                var result = await ComputeDominators().ConfigureAwait(false);

                if (cacheEnabled_) {
                    Interlocked.Exchange(ref dominators_, result);
                }

                return result;
            }

            return dominators_;
        }

        public async Task<DominatorAlgorithm> GetPostDominatorsAsync() {
            if (postDominators_ == null) {
                var result = await ComputePostDominators().ConfigureAwait(false);

                if (cacheEnabled_) {
                    Interlocked.Exchange(ref postDominators_, result);
                }

                return result;
            }

            return postDominators_;
        }

        public async Task<DominanceFrontier> GetDominanceFrontierAsync() {
            if (dominanceFrontier_ == null) {
                var result = await ComputeDominanceFrontierAsync().ConfigureAwait(false);

                if (cacheEnabled_) {
                    Interlocked.Exchange(ref dominanceFrontier_, result);
                }

                return result;
            }

            return dominanceFrontier_;
        }

        private async Task<DominanceFrontier> ComputeDominanceFrontierAsync() {
            var dominatorAlgorithm = await GetDominatorsAsync().ConfigureAwait(false);
            return new DominanceFrontier(function_, dominatorAlgorithm);
        }

        public async Task<DominanceFrontier> GetPostDominanceFrontierAsync() {
            if (postDominanceFrontier_ == null) {
                var result = await ComputePostDominanceFrontierAsync().ConfigureAwait(false);

                if (cacheEnabled_) {
                    Interlocked.Exchange(ref postDominanceFrontier_, result);
                }

                return result;
            }

            return postDominanceFrontier_;
        }

        private async Task<DominanceFrontier> ComputePostDominanceFrontierAsync() {
            var dominatorAlgorithm = await GetPostDominatorsAsync().ConfigureAwait(false);
            return new DominanceFrontier(function_, dominatorAlgorithm);
        }

        public async Task<CFGReachability> GetReachabilityAsync() {
            if (reachability_ == null) {
                var result = await ComputeReachability().ConfigureAwait(false);

                if (cacheEnabled_) {
                    Interlocked.Exchange(ref reachability_, result);
                }

                return result;
            }

            return reachability_;
        }

        public DominatorAlgorithm GetDominators() {
            return GetDominatorsAsync().Result;
        }

        public DominatorAlgorithm GetPostDominators() {
            return GetPostDominatorsAsync().Result;
        }

        private Task<DominatorAlgorithm> ComputeDominators() {
            return Task.Run(() => new DominatorAlgorithm(function_,
                                                         DominatorAlgorithmOptions.Dominators |
                                                         DominatorAlgorithmOptions.BuildQueryCache |
                                                         DominatorAlgorithmOptions.BuildDominatorTree));
        }

        private Task<DominatorAlgorithm> ComputePostDominators() {
            return Task.Run(() => new DominatorAlgorithm(function_,
                                                         DominatorAlgorithmOptions.PostDominators |
                                                         DominatorAlgorithmOptions.BuildQueryCache |
                                                         DominatorAlgorithmOptions.BuildDominatorTree));
        }

        private Task<CFGReachability> ComputeReachability() {
            return Task.Run(() => new CFGReachability(function_));
        }

        public async Task CacheAll() {
            var domTask = ComputeDominators();
            var postDomTask = ComputePostDominators();
            var reachTask = ComputeReachability();
            await Task.WhenAll(domTask, postDomTask, reachTask);
            Interlocked.Exchange(ref dominators_, await domTask);
            Interlocked.Exchange(ref postDominators_, await postDomTask);
            Interlocked.Exchange(ref reachability_, await reachTask);
        }

        public async Task CacheAllAsync() {
            await CacheAll();
        }

        public void InvalidateAll() {
            dominators_ = null;
            postDominators_ = null;
            reachability_ = null;
        }

        public static FunctionAnalysisCache Get(FunctionIR function) {
            lock (lockObject_) {
                if (functionCacheMap_.TryGetValue(function, out var cache)) {
                    return cache;
                }

                cache = new FunctionAnalysisCache(function);

                if (cacheEnabled_) {
                    functionCacheMap_[function] = cache;
                }

                return cache;
            }
        }

        public static bool Remove(FunctionIR function) {
            lock (lockObject_) {
                return functionCacheMap_.Remove(function);
            }
        }
    }
}
