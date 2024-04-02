using System;
using System.Collections.Generic;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Profile;

namespace IRExplorerUI;

public class ProfileHistoryManager {
  private Stack<ProfileFunctionState> prevFunctionsStack_;
  private Stack<ProfileFunctionState> nextFunctionsStack_;
  private bool ignoreNextSaveFunctionState_;
  private Func<ProfileFunctionState> saveStateHandler_;
  private Action stateChangeHandler_;

  public ProfileHistoryManager(Func<ProfileFunctionState> saveStateHandler,
                               Action stateChangeHandler) {
    prevFunctionsStack_ = new Stack<ProfileFunctionState>();
    nextFunctionsStack_ = new Stack<ProfileFunctionState>();
    saveStateHandler_ = saveStateHandler;
    stateChangeHandler_ = stateChangeHandler;
  }

  public bool HasNextStates => nextFunctionsStack_.Count > 0;
  public bool HasPreviousStates => prevFunctionsStack_.Count > 0;

  public Stack<ProfileFunctionState> PreviousFunctions => prevFunctionsStack_;
  public Stack<ProfileFunctionState> NextFunctions => nextFunctionsStack_;

  public void SaveCurrentState() {
    SaveCurrentFunctionState(prevFunctionsStack_);
  }

  public ProfileFunctionState PopPreviousState() {
    if (prevFunctionsStack_.Count == 0) {
      return null;
    }

    // Save current function in the forward history.
    var state = prevFunctionsStack_.Pop();
    SaveCurrentFunctionState(nextFunctionsStack_);
    ignoreNextSaveFunctionState_ = true;
    stateChangeHandler_();
    return state;
  }

  public ProfileFunctionState PopNextState() {
    if (nextFunctionsStack_.Count == 0) {
      return null;
    }

    // Save current function in the backward history.
    var state = nextFunctionsStack_.Pop();
    SaveCurrentFunctionState(prevFunctionsStack_);
    ignoreNextSaveFunctionState_ = true;
    stateChangeHandler_();
    return state;
  }

  private void SaveCurrentFunctionState(Stack<ProfileFunctionState> stack) {
    if (ignoreNextSaveFunctionState_) {
      // Don't add to the history the function
      // from which the going back action was started.
      ignoreNextSaveFunctionState_ = false;
    }
    else {
      var state = saveStateHandler_();

      if (state == null) {
        return;
      }

      // Don't duplicate the state.
      if (stack.Count == 0 || !stack.Peek().Equals(state)) {
        stack.Push(state);
        stateChangeHandler_();
      }
    }
  }

  public void RevertToState(ProfileFunctionState state) {
    // Add the current state in the next stack,
    // plus all stats from the previous stack up to selected state.
    SaveCurrentFunctionState(nextFunctionsStack_);

    while (prevFunctionsStack_.Peek() != state) {
      var skippedState = prevFunctionsStack_.Pop();
      nextFunctionsStack_.Push(skippedState);
    }

    prevFunctionsStack_.Pop();
    ignoreNextSaveFunctionState_ = true;
    stateChangeHandler_();
  }

  public void ClearPreviousStates() {
    prevFunctionsStack_.Clear();
  }

  public void ClearNextStates() {
    nextFunctionsStack_.Clear();
  }

  public void Reset() {
    prevFunctionsStack_.Clear();
    nextFunctionsStack_.Clear();
    ignoreNextSaveFunctionState_ = false;
  }
}

public class ProfileFunctionState {
  public ProfileFunctionState(IRTextSection section, FunctionIR function,
                              ReadOnlyMemory<char> text,
                              ProfileSampleFilter profileFilter) {
    Section = section;
    Function = function;
    Text = text;
    ProfileFilter = profileFilter;
  }

  public IRTextSection Section { get; set; }
  public FunctionIR Function { get; set;}
  public ReadOnlyMemory<char> Text { get; set; }
  public ProfileSampleFilter ProfileFilter { get; set;}
  public TimeSpan Weight { get; set; }
  public ParsedIRTextSection ParsedSection =>
    new ParsedIRTextSection(Section, Text, Function);

  protected bool Equals(ProfileFunctionState other) {
    return Equals(Section, other.Section);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != this.GetType())
      return false;
    return Equals((ProfileFunctionState)obj);
  }

  public override int GetHashCode() {
    return (Section != null ? Section.GetHashCode() : 0);
  }
}