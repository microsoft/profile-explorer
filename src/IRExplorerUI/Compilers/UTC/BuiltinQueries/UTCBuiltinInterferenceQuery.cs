using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerUI.Query;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using System.Windows;
using IRExplorerCore.UTC;
using System.Windows.Media;

namespace IRExplorerUI.Compilers.UTC {
    class UTCBuiltinInterferenceActions : IElementQuery {
        enum MarkingScope {
            All,
            Block,
            Loop,
            LoopNest
        }

        public static QueryDefinition GetDefinition() {
            var query = new QueryDefinition(typeof(UTCBuiltinInterferenceActions),
                                                   "Alias marking",
                                                   "Alias query results for two values");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("Mark only reachable", QueryValueKind.Bool, false);
            query.Data.AddInput("Temporary marking", QueryValueKind.Bool, true);
            query.Data.AddInput("Show arrows", QueryValueKind.Bool, false);

            var a = query.Data.AddButton("All");
            a.HasDemiBoldText = true;
            a.Action = (sender, data) =>
                ((UTCBuiltinInterferenceActions)query.Data.Instance).Execute(query.Data, MarkingScope.All);

            var b = query.Data.AddButton("Block");
            b.Action = (sender, data) =>
                ((UTCBuiltinInterferenceActions)query.Data.Instance).Execute(query.Data, MarkingScope.Block);

            var c = query.Data.AddButton("Loop");
            c.Action = (sender, data) =>
                ((UTCBuiltinInterferenceActions)query.Data.Instance).Execute(query.Data, MarkingScope.Loop);

            var d = query.Data.AddButton("Loop nest");
            d.Action = (sender, data) =>
                ((UTCBuiltinInterferenceActions)query.Data.Instance).Execute(query.Data, MarkingScope.LoopNest);

            return query;
        }

        private int aliasedValues_;
        private int aliasedIndirectValues_;

        public ISession Session { get; private set; }

        public bool Initialize(ISession session) {
            Session = session;
            return true;
        }

        public bool Execute(QueryData data) {
            return Execute(data, MarkingScope.All);
        }

        private bool Execute(QueryData data, MarkingScope markingScope) {
            var element = data.GetInput<IRElement>(0);
            var onlyReachable = data.GetInput<bool>(1);
            var isTemporary = data.GetInput<bool>(2);
            var showArrows = data.GetInput<bool>(3);
            var func = element.ParentFunction;
            int pas = 0;

            data.ResetResults();
            var interfTag = func.GetTag<InterferenceTag>();

            if (interfTag == null) {
                data.SetOutputWarning("No interference info found", "Use -d2dbINTERF,ITFPAS to output interf logs");
                return false;
            }

            if (!(element is OperandIR op)) {
                data.SetOutputWarning("Invalid IR element", "Selected IR element is not an aliased operand");
                return false;
            }

            if (op.IsIndirection) {
                var pasTag = element.GetTag<PointsAtSetTag>();

                if (pasTag != null) {
                    pas = pasTag.Pas;
                    data.SetOutput("Query PAS", pas);
                }
                else {
                    data.SetOutputWarning("Indirection has no PAS", "Selected Indirection operand has no PAS info");
                    return false;
                }
            }
            else if (op.IsVariable && op.HasName) {
                if (!interfTag.SymToPasMap.TryGetValue(op.Name, out pas)) {
                    data.SetOutputWarning("Unaliased variable", "Selected variable is not an aliased operand");
                    return false;
                }
            }
            else {
                data.SetOutputWarning("Unaliased IR element", "Selected IR element is not an aliased operand");
                return false;
            }

            aliasedValues_ = 0;
            aliasedIndirectValues_ = 0;
            var block = element.ParentBlock;
            var document = Session.CurrentDocument;
            var highlightingType = isTemporary ? HighlighingType.Selected : HighlighingType.Marked;
            document.BeginMarkElementAppend(highlightingType);
            document.ClearConnectedElements();

            if (showArrows) {
                document.SetRootElement(element, new HighlightingStyle(Colors.Blue));
            }

            if (interfTag.InterferingPasMap.TryGetValue(pas, out var interPasses)) {
                foreach (var interfPas in interPasses) {
                    // Mark all symbols.
                    if (interfTag.PasToSymMap.TryGetValue(interfPas, out var interfSymbols)) {
                        foreach (var interfSymbol in interfSymbols) {
                            MarkAllSymbols(func, interfSymbol, block,
                                           markingScope, highlightingType, showArrows);
                        }
                    }

                    // Mark all indirections and calls.
                    MarkAllIndirections(func, interfPas, block,
                                        markingScope, highlightingType, showArrows);
                }
            }

            document.EndMarkElementAppend(highlightingType);
            data.SetOutput("Aliasing values", aliasedValues_);

            if(aliasedValues_ > 0) {
                data.SetOutput("Aliasing indirect values", aliasedIndirectValues_);
            }
            return true;
        }

        private void MarkAllSymbols(FunctionIR func, string interfSymbol, BlockIR queryBlock,
                                    MarkingScope markingScope, HighlighingType highlightingType, bool showArrows) {
            var instrStyle = new HighlightingStyle(Brushes.Transparent, Pens.GetPen(Colors.Gray));

            foreach (var elem in func.AllElements) {
                if (elem is OperandIR op && 
                    op.IsVariable && op.HasName &&
                    op.Name == interfSymbol) {
                    if (ShouldMarkElement(op, markingScope, queryBlock)) {
                        MarkElement(op, instrStyle, highlightingType, showArrows);
                        aliasedValues_++;
                    }
                }
            }
        }

        private void MarkAllIndirections(FunctionIR func, int interfPas, BlockIR queryBlock,
                                         MarkingScope markingScope, HighlighingType highlightingType, bool showArrows) {
            var document = Session.CurrentDocument;
            var instrStyle = new HighlightingStyle(Brushes.Transparent, Pens.GetBoldPen(Colors.Gray));

            foreach (var element in func.AllElements) {
                if (!(element is OperandIR op)) {
                    continue;
                }

                var pasTag = op.GetTag<PointsAtSetTag>();

                if (pasTag != null && pasTag.Pas == interfPas) {
                    if (ShouldMarkElement(op, markingScope, queryBlock)) {
                        MarkElement(op, instrStyle, highlightingType, showArrows);
                        aliasedValues_++;
                        aliasedIndirectValues_++;
                    }
                }
            }
        }

        private void MarkElement(OperandIR op, HighlightingStyle instrStyle, HighlighingType highlightingType, bool showArrows) {
            var document = Session.CurrentDocument;

            if (op.IsDestinationOperand) {
                document.MarkElementAppend(op, Colors.Pink, highlightingType, false);
            }
            else {
                document.MarkElementAppend(op, Utils.ColorFromString("#AEA9FC"), highlightingType, false);
            }

            document.MarkElementAppend(op.ParentTuple, instrStyle, highlightingType, false);

            if (showArrows) {
                var style = op.IsDestinationOperand ? new HighlightingStyle(Colors.DarkRed, Pens.GetDashedPen(Colors.DarkRed, DashStyles.Dash, 1.5)) :
                                                      new HighlightingStyle(Colors.DarkBlue, Pens.GetDashedPen(Colors.DarkBlue, DashStyles.Dash, 1.5));
                document.AddConnectedElement(op, style);
            }
        }

        private bool ShouldMarkElement(OperandIR op, MarkingScope markingScope, BlockIR queryBlock) {
            switch (markingScope) {
                case MarkingScope.All: {
                    return true;
                }
                case MarkingScope.Block: {
                    return op.ParentBlock == queryBlock;
                }
                case MarkingScope.Loop: {
                    return AreBlocksInSameLoop(op.ParentBlock, queryBlock, false);
                }
                case MarkingScope.LoopNest: {
                    return AreBlocksInSameLoop(op.ParentBlock, queryBlock, true);
                }
            }

            return false;
        }

        private bool AreBlocksInSameLoop(BlockIR blockA, BlockIR blockB, bool checkLoopNest) {
            var tagA = blockA.GetTag<LoopBlockTag>();
            var tagB = blockB.GetTag<LoopBlockTag>();

            if (tagA != null && tagB != null) {
                if (checkLoopNest) {
                    return tagA.Loop.LoopNestRoot == tagB.Loop.LoopNestRoot;
                }
                else {
                    return tagA.Loop == tagB.Loop;
                }
            }

            return false;
        }
    }

    class UTCBuiltinInterferenceQuery : IElementQuery {
        public static QueryDefinition GetDefinition() {
            var query = new QueryDefinition(typeof(UTCBuiltinInterferenceQuery),
                                                   "Alias query",
                                                   "Alias query results for two values");
            query.Data.AddInput("Operand 1", QueryValueKind.Element);
            query.Data.AddInput("Operand 2", QueryValueKind.Element);
            query.Data.AddOutput("May Alias", QueryValueKind.Bool);
            return query;
        }

        public ISession Session { get; private set; }

        public bool Initialize(ISession session) {
            Session = session;
            return true;
        }

        public bool Execute(QueryData data) {
            var elementA = data.GetInput<IRElement>("Operand 1");
            var elementB = data.GetInput<IRElement>("Operand 2");
            var func = elementA.ParentFunction;

            data.ResetResults();
            data.SetOutputWarning("May Alias", "Not yet implemented!");
            return true;
        }
    }
}
