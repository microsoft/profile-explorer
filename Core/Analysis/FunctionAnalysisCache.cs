// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.IR;

namespace Core.Analysis {
    public class FunctionAnalysisCache {
        FunctionIR function_;
        volatile DominatorAlgorithm dominators_;
        volatile DominatorAlgorithm postDominators_;
        volatile CFGReachability reachability_;

        public FunctionAnalysisCache(FunctionIR function) {
            function_ = function;
        }

        public async Task<DominatorAlgorithm> GetDominatorsAsync() {
            if (dominators_ == null) {
                Interlocked.Exchange(ref dominators_, await ComputeDominators().ConfigureAwait(false));
            }

            return dominators_;
        }

        public async Task<DominatorAlgorithm> GetPostDominatorsAsync() {
            if (postDominators_ == null) {
                Interlocked.Exchange(ref postDominators_, await ComputePostDominators().ConfigureAwait(false));
            }

            return postDominators_;
        }

        public async Task<CFGReachability> GetReachabilityAsync() {
            if (reachability_ == null) {
                Interlocked.Exchange(ref reachability_, await ComputeReachability().ConfigureAwait(false));
            }

            return reachability_;
        }

        public DominatorAlgorithm GetDominators()
        {
            return GetDominatorsAsync().Result;
        }

        public DominatorAlgorithm GetPostDominators()
        {
            return GetPostDominatorsAsync().Result;
        }

        private Task<DominatorAlgorithm> ComputeDominators() {
            return Task.Run(() => new DominatorAlgorithm(function_, DominatorAlgorithmOptions.Dominators |
                                                    DominatorAlgorithmOptions.BuildQueryCache |
                                                    DominatorAlgorithmOptions.BuildDominatorTree));
        }

        private Task<DominatorAlgorithm> ComputePostDominators() {
            return Task.Run(() => new DominatorAlgorithm(function_, DominatorAlgorithmOptions.PostDominators |
                                                    DominatorAlgorithmOptions.BuildQueryCache | 
                                                    DominatorAlgorithmOptions.BuildDominatorTree));
        }


        private Task<CFGReachability> ComputeReachability() {
            return Task.Run(() => new CFGReachability(function_));
        }

        public async void CacheAll() {
            var domTask = ComputeDominators();
            var postDomTask = ComputePostDominators();
            var reachTask = ComputeReachability();

            Task.WaitAll(domTask, postDomTask, reachTask);
            Interlocked.Exchange(ref dominators_, await domTask);
            Interlocked.Exchange(ref postDominators_, await postDomTask);
            Interlocked.Exchange(ref reachability_, await reachTask);
        }

        public async Task CacheAllAsync() {
            await Task.Run(() => CacheAll());
        }

        public void InvalidateAll() {
            dominators_ = null;
            postDominators_ = null;
            reachability_ = null;
        }

        static Dictionary<FunctionIR, FunctionAnalysisCache> functionCacheMap_;
        static object lockObject_ = new object();

        static FunctionAnalysisCache()
        {
            functionCacheMap_ = new Dictionary<FunctionIR, FunctionAnalysisCache>();
        }

        public static FunctionAnalysisCache Get(FunctionIR function)
        {
            lock (lockObject_)
            {
                if(functionCacheMap_.TryGetValue(function, out var cache))
                {
                    return cache;
                }
                
                cache = new FunctionAnalysisCache(function);
                functionCacheMap_[function] = cache;
                return cache;
            }
        }

        public static bool Remove(FunctionIR function)
        {
            return functionCacheMap_.Remove(function);
        }
    }
}
